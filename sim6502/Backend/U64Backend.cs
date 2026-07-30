using System.Diagnostics;
using System.Text;
using NLog;
using sim6502.Systems.Ultimate;

namespace sim6502.Backend;

/// <summary>Raised when a UCI transaction cannot be completed or recovered.</summary>
public class U64UciException : InvalidOperationException
{
    public U64UciException(string message) : base(message) { }
}

/// <summary>
/// A real Ultimate 64 reached over its REST API.
///
/// Scoped as a differential instrument for u64sim rather than a general
/// execution backend: it carries UCI traffic, reads and writes memory by DMA,
/// and resets the machine. Registers, flags, cycle counts and ExecuteJsr have no
/// REST equivalent and are not emulated -- see the spec's "Supported and
/// unsupported members".
/// </summary>
public sealed class U64Backend : IUltimateBackend, IDisposable
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Upper bound on a single reply or status drain.
    ///
    /// Bounded on purpose. The availability bit in $DF1C never clears once set --
    /// an upstream wart pinned by u64sim's tests and confirmed on silicon -- so a
    /// "read until the bit drops" loop would spin forever. Sized to the UCI's own
    /// response buffer.
    /// </summary>
    private const int MaxDrain = UciConstants.ResponseBufferSize;

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

            var data = new List<byte>();
            var status = new List<byte>();

            while (true)
            {
                var state = WaitForReply();

                DrainInto(data, UciConstants.ResponseAddress,
                    UciConstants.StatusResponseAvailable);
                DrainInto(status, UciConstants.StatusAddress,
                    UciConstants.StatusStatusAvailable);

                if ((state & UciConstants.StatusStateMask) != UciConstants.StateDataMore)
                    break;

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

            if ((status & UciConstants.StatusStateMask) == UciConstants.StateBusy)
                continue;
        }

        var last = _connection.ReadByte(UciConstants.ControlAddress);
        throw new U64UciException(
            $"The Ultimate did not answer within {_config.CommandBudgetMs}ms. " +
            $"Last status ${last:X2}. If the interface stays busy, only a power " +
            "cycle clears it -- see GideonZ/1541ultimate#740 for one command " +
            "known to wedge it.");
    }

    /// <summary>Drain one FIFO, bounded because the availability bit never clears.</summary>
    private void DrainInto(List<byte> sink, int address, byte availableBit)
    {
        var taken = 0;
        while (taken < MaxDrain &&
               (_connection.ReadByte(UciConstants.ControlAddress) & availableBit) != 0)
        {
            var b = _connection.ReadByte(address);
            if (b == 0) break;
            sink.Add(b);
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
