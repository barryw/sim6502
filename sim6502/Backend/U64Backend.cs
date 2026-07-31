using System.Diagnostics;
using System.Text;
using NLog;
using sim6502.Systems.Ultimate;

namespace sim6502.Backend;

/// <summary>Raised when a UCI transaction cannot be completed or recovered.</summary>
public sealed class U64UciException : InvalidOperationException
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
///
/// Reply and status drains are bounded to their own queue's size. The
/// availability bit in $DF1C clears normally once the read pointer passes the
/// reply's length; it only sticks when a reply exactly fills its queue, because
/// the pointer saturates at the last slot instead of advancing past it. A "read
/// until the bit drops" loop would spin forever in that one case, so each drain
/// stops at its queue's own capacity instead of trusting the bit to clear.
/// </summary>
public sealed class U64Backend : IExecutionBackend, IUltimateBackend
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

    /// <summary>
    /// Push a UCI command through the C64-visible registers and collect its
    /// reply, walking every continuation part.
    ///
    /// Diverges from <see cref="UciRegisters.IssueHostCommand"/> on
    /// <see cref="UciConstants.NoReplyFlag"/>: that method bypasses the
    /// register block entirely and always returns the target's full reply,
    /// while this method honours the flag and returns
    /// <c>(string.Empty, Array.Empty&lt;byte&gt;())</c>, matching real
    /// hardware -- a real Ultimate never places a reply in the queues for a
    /// no-reply command. <see cref="U64SimBackend"/> calls
    /// <c>IssueHostCommand</c> under the same <c>IUltimateBackend</c>
    /// interface, and the two backends are the differential pair a test
    /// script runs <c>uci()</c> against (once on u64sim, once on real
    /// hardware, diffing the recorded status/data): a
    /// <c>uci($81, ...)</c> call will report a mismatch on that diff. That
    /// is expected, not a bug in either backend -- this one reflects real
    /// hardware, and <c>UciRegisters</c> is intentionally left alone here.
    /// </summary>
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

            // ERROR_BUSY is a pure latch: UciRegisters sets it when a push
            // arrives while the state isn't Idle, and clears it only on an
            // explicit ControlClearError write -- ControlAbort returns to Idle
            // without touching it. So "Idle with the latch still set" is a
            // stable, reachable state, and a stale bit there must not be
            // mistaken for this push having been rejected. Folding
            // ControlClearError into the same write (WriteControl evaluates its
            // clear-error clause before its push clause, command_protocol.vhd
            // lines 148-170) clears any stale latch first, so a set bit
            // afterwards unambiguously means this push was rejected.
            _connection.WriteByte(UciConstants.ControlAddress,
                (byte)(UciConstants.ControlPushCommand | UciConstants.ControlClearError));

            // A push rejected by the ERROR_BUSY latch is otherwise invisible: on
            // the reply path it looks like a full-budget timeout, and on the
            // no-reply path a non-Idle leftover state fails the settle loop's
            // `== StateBusy` test on its very first read, reporting an instant,
            // silent success for a command the Ultimate never accepted. One
            // check here covers both paths.
            var pushStatus = _connection.ReadByte(UciConstants.ControlAddress);
            if ((pushStatus & UciConstants.StatusError) != 0)
                throw new U64UciException(
                    $"The Ultimate's error latch rejected this command push " +
                    $"(status ${pushStatus:X2}).");

            if ((command[0] & UciConstants.NoReplyFlag) != 0)
            {
                // No reply will ever come, but the Ultimate has not necessarily
                // consumed the command yet: PUSH_CMD sets BUSY synchronously and
                // only the firmware's HANDSHAKE_ACCEPT_COMMAND resets the command
                // pointer. Returning while BUSY leaves the next command's bytes
                // appended to this one's.
                var settle = Stopwatch.StartNew();
                while ((_connection.ReadByte(UciConstants.ControlAddress) &
                        UciConstants.StatusStateMask) == UciConstants.StateBusy)
                {
                    if (settle.ElapsedMilliseconds >= _config.CommandBudgetMs)
                        throw new U64UciException(
                            $"A no-reply command was not consumed within {_config.CommandBudgetMs}ms; " +
                            "the interface is still busy and the next command would be appended to it.");
                }
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
            // Recovery must run whenever the transaction failed OR the cleanup
            // acknowledge itself failed. A throwing acknowledge on an otherwise
            // successful transaction still leaves the interface parked (e.g. in
            // DataLast): the next push then hits the ERROR_BUSY branch. Were the
            // error-latch check above not present, WaitForReply's StateDataLast
            // check would return immediately on the stale status, draining
            // nothing -- the next command would silently return empty status
            // and empty data instead of an error. That check turns this into a
            // loud U64UciException instead.
            var acknowledged = true;
            try
            {
                _connection.WriteByte(UciConstants.ControlAddress,
                    UciConstants.ControlDataAccept);
            }
            catch (Exception ex)
            {
                acknowledged = false;
                Logger.Warn($"UCI cleanup acknowledge failed: {ex.Message}");
            }

            if (!completed || !acknowledged)
            {
                try
                {
                    Recover();
                }
                catch (Exception ex)
                {
                    Logger.Warn($"UCI recovery failed: {ex.Message}");
                }
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

        // Best-effort: this read is purely for a more informative message, and
        // if the connection itself is what's failing, that failure must not
        // replace the timeout as the exception the caller sees.
        string lastStatus;
        try
        {
            lastStatus = $"${_connection.ReadByte(UciConstants.ControlAddress):X2}";
        }
        catch (Exception)
        {
            lastStatus = "unknown";
        }

        throw new U64UciException(
            $"The Ultimate did not answer within {_config.CommandBudgetMs}ms. " +
            $"Last status {lastStatus}. If the interface stays busy, only a power " +
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

    // ── Memory: real, by DMA on the cartridge bus ──
    //
    // CAVEAT: readmem/writemem go THROUGH THE PLA, so this is not raw RAM.
    // $D000-$DFFF reaches I/O (which is exactly why UCI traffic works at all),
    // and $A000-$BFFF / $E000-$FFFF return ROM when banked in. Upstream added a
    // `ramonly` parameter for this (GideonZ/1541ultimate#674) but it is not
    // present in fw 3.14d. Assertions about RAM under ROM or I/O will not agree
    // with the sim backend.

    public byte ReadByte(int address) => _connection.ReadByte(address);

    public void WriteByte(int address, byte value) => _connection.WriteByte(address, value);

    public int ReadWord(int address)
    {
        var bytes = _connection.ReadBytes(address, 2);
        if (bytes.Length < 2)
            throw new InvalidOperationException(
                $"Ultimate returned {bytes.Length} byte(s) reading a word at " +
                $"${address:X4}; expected 2");
        return bytes[0] | (bytes[1] << 8);
    }

    public void WriteWord(int address, int value)
    {
        _connection.WriteBytes(address, new[]
        {
            (byte)(value & 0xFF),
            (byte)((value >> 8) & 0xFF)
        });
    }

    public void WriteMemoryValue(int address, int value)
    {
        if (value > 0xFF) WriteWord(address, value);
        else WriteByte(address, (byte)value);
    }

    public void LoadBinary(byte[] data, int address)
    {
        ArgumentNullException.ThrowIfNull(data);
        Logger.Info($"Loading {data.Length} bytes to ${address:X4} over REST");
        _connection.WriteBytes(address, data);
    }

    public void Reset()
    {
        // PUT machine:reset resets the C64. It does not restart the Ultimate's
        // own command-interface task, so it will not clear a wedged UCI.
        _connection.ResetMachine();
        Logger.Info("C64 reset requested");
    }

    // ── Not reachable over REST ──

    private static NotSupportedException Unsupported(string member, string alternative) =>
        new($"'{member}' is not available on the u64 backend: the Ultimate's REST " +
            $"API exposes no equivalent. {alternative}");

    public int GetRegister(string name) =>
        throw Unsupported(nameof(GetRegister),
            "Reading CPU registers would need a resident 6502 stub, which this " +
            "milestone deliberately omits. Use --backend u64sim for register assertions.");

    public void SetRegister(string name, int value) =>
        throw Unsupported(nameof(SetRegister),
            "Use --backend u64sim for register assertions.");

    public bool GetFlag(string name) =>
        throw Unsupported(nameof(GetFlag),
            "Use --backend u64sim for flag assertions.");

    public void SetFlag(string name, bool value) =>
        throw Unsupported(nameof(SetFlag),
            "Use --backend u64sim for flag assertions.");

    public ExecutionResult ExecuteJsr(int address, int stopOnAddress, bool stopOnRts, bool failOnBrk) =>
        throw Unsupported(nameof(ExecuteJsr),
            "There is no breakpoint mechanism in the REST API. Drive the machine " +
            "with uci(), or use --backend u64sim to run 6502 code.");

    public int GetCycles() =>
        throw Unsupported(nameof(GetCycles),
            "Cycle counting would need CIA bracketing around a resident stub.");

    public void ResetCycleCount() =>
        throw Unsupported(nameof(ResetCycleCount),
            "Cycle counting would need CIA bracketing around a resident stub.");

    public void SaveSnapshot(string name) =>
        throw Unsupported(nameof(SaveSnapshot), "Snapshots have no REST equivalent.");

    public void RestoreSnapshot(string name) =>
        throw Unsupported(nameof(RestoreSnapshot), "Snapshots have no REST equivalent.");

    // ── Accepted and ignored ──
    //
    // Suites set these incidentally. Throwing would fail runs that are otherwise
    // perfectly valid, so they are logged no-ops instead.

    public void SetWarpMode(bool enabled) =>
        Logger.Debug($"SetWarpMode({enabled}) ignored: real hardware runs at 1MHz");

    public void LoadSymbols(string path) =>
        Logger.Debug($"LoadSymbols('{path}') ignored: symbols stay host-side on u64");

    public bool TraceEnabled
    {
        get => false;
        set
        {
            if (value) Logger.Warn("Tracing is not available on the u64 backend; ignoring");
        }
    }

    public void ClearTraceBuffer() { }

    public List<string> GetTraceBuffer() => new();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
    }
}
