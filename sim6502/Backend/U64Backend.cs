using System.Diagnostics;
using System.Text;
using NLog;
using sim6502.Systems.Ultimate;

namespace sim6502.Backend;

/// <summary>Raised when a UCI transaction cannot be completed or recovered.</summary>
public sealed class U64UciException : InvalidOperationException
{
    public U64UciException(string message) : base(message) { }
    public U64UciException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// A real Ultimate 64 reached over its REST API.
///
/// Scoped as a differential instrument for u64sim rather than a general
/// execution backend: it carries UCI traffic, reads and writes memory by DMA,
/// and resets the machine. Registers, flags, cycle counts and ExecuteJsr have no
/// REST equivalent and are not emulated -- see the spec's "Supported and
/// unsupported members".
///
/// Reply and status drains are bounded to their own queue's size. The
/// availability bit in $DF1C clears normally once the read pointer passes the
/// reply's length; it only sticks when a reply exactly fills its queue, because
/// the pointer saturates at the last slot instead of advancing past it. A "read
/// until the bit drops" loop would spin forever in that one case, so each drain
/// stops at its queue's own capacity instead of trusting the bit to clear.
/// </summary>
public sealed class U64Backend : IUltimateBackend, IDisposable
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Guard against a target that never reports a last part. Mirrors
    /// UciRegisters' own MaxContinuationParts -- the two are sibling walks of
    /// the same continuation protocol and must not disagree.
    /// </summary>
    private const int MaxContinuationParts = 4096;

    private readonly IU64Connection _connection;
    private readonly U64BackendConfig _config;
    private bool _disposed;

    public U64Backend(U64BackendConfig config, IU64Connection connection)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public U64Backend(U64BackendConfig config)
        : this(config, new U64RestConnection(config))
    {
    }

    public (string Status, byte[] Data) IssueUciCommand(byte[] command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Length == 0)
            throw new ArgumentException("A UCI command needs at least a target byte",
                nameof(command));

        var completed = false;
        try
        {
            foreach (var b in command)
                _connection.WriteByte(UciConstants.CommandAddress, b);

            _connection.WriteByte(UciConstants.ControlAddress,
                UciConstants.ControlPushCommand);

            if ((command[0] & UciConstants.NoReplyFlag) != 0)
            {
                // By definition there is no reply to wait for. Inferring this
                // from the state register instead (e.g. "idle after push")
                // would race the Ultimate actually servicing the command.
                completed = true;
                return (string.Empty, Array.Empty<byte>());
            }

            var data = new List<byte>();
            var status = new List<byte>();
            var parts = 0;

            while (true)
            {
                var state = WaitForReply();

                DrainInto(data, UciConstants.ResponseAddress,
                    UciConstants.StatusResponseAvailable, UciConstants.ResponseBufferSize);
                DrainInto(status, UciConstants.StatusAddress,
                    UciConstants.StatusStatusAvailable, UciConstants.StatusBufferSize);

                if ((state & UciConstants.StatusStateMask) != UciConstants.StateDataMore)
                    break;

                if (++parts > MaxContinuationParts)
                    throw new U64UciException(
                        $"UCI reply did not finish after {MaxContinuationParts} continuation " +
                        "parts; the target may never report a last part.");

                // More parts follow: acknowledge and go round again.
                _connection.WriteByte(UciConstants.ControlAddress,
                    UciConstants.ControlDataAccept);
            }

            completed = true;
            return (Encoding.ASCII.GetString(status.ToArray()), data.ToArray());
        }
        finally
        {
            try
            {
                _connection.WriteByte(UciConstants.ControlAddress,
                    UciConstants.ControlDataAccept);
                if (!completed) Recover();
            }
            catch (Exception ex)
            {
                // Never let cleanup mask the original failure.
                Logger.Warn($"UCI cleanup failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Wait for a reply, treating BUSY as progress rather than as a deadline.
    ///
    /// Getting this wrong is not a theoretical risk: a 2.5s wall-clock timeout
    /// against a legitimately-busy command left a real machine mid-transaction
    /// and needed a power cycle.
    /// </summary>
    private byte WaitForReply()
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.ElapsedMilliseconds < _config.CommandBudgetMs)
        {
            var status = _connection.ReadByte(UciConstants.ControlAddress);

            if ((status & (UciConstants.StatusResponseAvailable |
                           UciConstants.StatusStatusAvailable)) != 0)
                return status;

            // A reply with no data and no status (e.g. a zero-length read) sets
            // neither availability bit, yet the transaction is done: the state
            // has settled on a data state. Waiting out the full budget here
            // would misdiagnose a completed command as a hang.
            var state = (byte)(status & UciConstants.StatusStateMask);
            if (state == UciConstants.StateDataLast || state == UciConstants.StateDataMore)
                return status;
        }

        var last = _connection.ReadByte(UciConstants.ControlAddress);
        throw new U64UciException(
            $"The Ultimate did not answer within {_config.CommandBudgetMs}ms. " +
            $"Last status ${last:X2}. If the interface stays busy, only a power " +
            "cycle clears it -- see GideonZ/1541ultimate#740 for one command " +
            "known to wedge it.");
    }

    /// <summary>
    /// Drain one FIFO into <paramref name="sink"/>, up to <paramref name="bound"/>
    /// bytes -- the queue's own size (<see cref="UciConstants.ResponseBufferSize"/>
    /// for a reply, <see cref="UciConstants.StatusBufferSize"/> for a status).
    ///
    /// The availability bit clears normally once the read pointer passes the
    /// reply's length; it only sticks when a reply exactly fills its queue,
    /// because the pointer saturates at the last slot instead of advancing past
    /// it -- see the class summary. Bounding by the queue's own capacity covers
    /// that case without assuming every byte is non-zero: C64 binaries routinely
    /// contain $00, and a value of zero is not end-of-data on this bus.
    /// </summary>
    private void DrainInto(List<byte> sink, int address, byte availableBit, int bound)
    {
        var taken = 0;
        while (taken < bound &&
               (_connection.ReadByte(UciConstants.ControlAddress) & availableBit) != 0)
        {
            sink.Add(_connection.ReadByte(address));
            taken++;
        }
    }

    /// <summary>
    /// Release a stuck transaction. Safe when already idle.
    ///
    /// This is best-effort: on real firmware some commands leave Busy latched and
    /// no write to $DF1C clears it.
    /// </summary>
    private void Recover()
    {
        _connection.WriteByte(UciConstants.ControlAddress, UciConstants.ControlAbort);
        _connection.WriteByte(UciConstants.ControlAddress, UciConstants.ControlClearError);
        _connection.WriteByte(UciConstants.ControlAddress, UciConstants.ControlDataAccept);
        _connection.WriteByte(UciConstants.ControlAddress, 0x00);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
    }
}
