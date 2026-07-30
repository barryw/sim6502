// Ported from GideonZ/1541ultimate (GPL-3.0):
//   fpga/io/command_interface/vhdl_source/command_protocol.vhd  (C64-side protocol)
//   software/io/command_interface/command_intf.cc               (service loop)
// Original author: Gideon Zweijtzer. See NOTICE.

using NLog;

namespace sim6502.Systems.Ultimate;

/// <summary>
/// The Ultimate Command Interface as the C64 sees it: five registers at
/// $DF1B-$DF1F backed by three queues, plus the Ultimate-side service loop that
/// dispatches completed commands to an <see cref="ICommandTarget"/>.
///
/// The real UCI is asynchronous — the C64 writes PUSH_CMD then polls $DF1C while
/// the state is Busy. Answering instantly would let a client with a broken or
/// missing busy-wait loop pass here and fail on hardware, so the Busy state is
/// held for <see cref="LatencyCycles"/> CPU cycles before the response appears.
///
/// Not modelled: IRQ delivery (upstream's <c>slot_resp.irq &lt;= state(1) and
/// cmd_irq_en</c> and the <c>io_irq</c> output), DMA, and the freeze latch
/// (<c>write_ff00 and trigger -&gt; freeze_i</c>). Also not modelled: upstream
/// returns 0x49 instead of 0xC9 from $DF1D while an IRQ is pending
/// (<c>irq_n &amp; "1001001"</c>); this port always returns 0xC9.
/// <see cref="_freeze"/>, <see cref="_trigger"/> and
/// <see cref="_commandIrqEnabled"/> exist only to track that state faithfully —
/// nothing consumes them yet.
///
/// A reply or status that exactly fills its queue leaves the corresponding
/// availability bit permanently set: the pointer saturates at the last valid
/// slot (upstream's <c>/=</c>, i.e. !=, saturation guard) while the validity
/// test <c>(pointer - start) &lt; length</c> stays true for that slot forever,
/// so the C64 would re-read the last byte indefinitely. This is upstream's
/// behaviour (command_protocol.vhd lines 131-135, 176-180), reproduced here
/// deliberately rather than "fixed" — a real client must track how many bytes
/// it expects rather than reading until the availability bit clears.
/// </summary>
public sealed class UciRegisters : IIOHandler
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    private static readonly EmptyCommandTarget Empty = new();

    private readonly byte[] _ram = new byte[UciConstants.BackingStoreSize];
    private readonly ICommandTarget?[] _targets = new ICommandTarget?[UciConstants.MaxTarget + 1];

    private int _commandPointer  = UciConstants.CommandBufferStart;
    private int _responsePointer = UciConstants.ResponseBufferStart;
    private int _statusPointer   = UciConstants.StatusBufferStart;
    private int _responseLength;
    private int _statusLength;

    private byte _state = UciConstants.StateIdle;   // already shifted into bits 5-4
    private bool _errorBusy;
    private bool _newCommand;      // handshakeIn[0]
    private bool _dataAccepted;    // handshakeIn[1]
    private bool _abort;           // handshakeIn[2]
    private bool _freeze;
    private bool _trigger;
    private bool _commandIrqEnabled;

    private ICommandTarget? _activeTarget;
    private long _serviceAtCycle;

    /// <summary>Guard against a target that never reports a last part.</summary>
    private const int MaxContinuationParts = 4096;

    public UciRegisters(int latencyCycles = 0)
    {
        if (latencyCycles < 0)
            throw new ArgumentOutOfRangeException(nameof(latencyCycles),
                "UCI latency must not be negative");
        LatencyCycles = latencyCycles;
    }

    /// <summary>Cycles the Busy state is held before a response becomes visible.</summary>
    public int LatencyCycles { get; }

    /// <summary>
    /// Source of the current CPU cycle count. Wired to the processor by the
    /// backend; defaults to a constant so unit tests can run with latency 0.
    /// </summary>
    public Func<long> CycleCounter { get; set; } = () => 0;

    /// <summary>
    /// When false the Ultimate-side service loop never runs, so tests can observe
    /// intermediate protocol state. The backend sets this true.
    /// </summary>
    public bool ServiceEnabled { get; set; }

    /// <summary>Value returned when the C64 reads $DF1B.</summary>
    public byte BusId { get; set; }

    /// <summary>Bytes written to the command register since the last reset.</summary>
    public int CommandLength => _commandPointer - UciConstants.CommandBufferStart;

    public void RegisterTarget(int targetId, ICommandTarget target)
    {
        if (targetId < 0 || targetId > UciConstants.MaxTarget)
            throw new ArgumentOutOfRangeException(nameof(targetId),
                $"Target id must be 0-{UciConstants.MaxTarget}");
        _targets[targetId] = target ?? throw new ArgumentNullException(nameof(target));
    }

    private bool ResponseValid =>
        (_responsePointer - UciConstants.ResponseBufferStart) < _responseLength
        && (_state & 0x20) != 0
        && !_abort;

    private bool StatusValid =>
        (_statusPointer - UciConstants.StatusBufferStart) < _statusLength
        && (_state & 0x20) != 0
        && !_abort;

    /// <summary>The byte the C64 reads from $DF1C.</summary>
    public byte StatusByte
    {
        get
        {
            byte value = _state;
            if (ResponseValid) value |= UciConstants.StatusResponseAvailable;
            if (StatusValid)   value |= UciConstants.StatusStatusAvailable;
            if (_errorBusy)    value |= UciConstants.StatusError;
            if (_abort)        value |= UciConstants.StatusAbortSet;
            if (_dataAccepted) value |= UciConstants.StatusDataAcceptedSet;
            if (_newCommand)   value |= UciConstants.StatusNewCommandSet;
            return value;
        }
    }

    public byte Read(int address)
    {
        switch (address)
        {
            case UciConstants.BusIdAddress:
                return BusId;

            case UciConstants.ControlAddress:
                ServicePending();
                return StatusByte;

            case UciConstants.CommandAddress:
                return UciConstants.Identifier;

            case UciConstants.ResponseAddress:
            {
                var value = ResponseValid ? _ram[_responsePointer] : (byte)0x00;
                _commandIrqEnabled = false;
                if (_responsePointer != UciConstants.ResponseBufferEnd)
                    _responsePointer++;
                return value;
            }

            case UciConstants.StatusAddress:
            {
                var value = StatusValid ? _ram[_statusPointer] : (byte)0x00;
                _commandIrqEnabled = false;
                if (_statusPointer != UciConstants.StatusBufferEnd)
                    _statusPointer++;
                return value;
            }

            default:
                return 0xFF;
        }
    }

    public void Write(int address, byte value)
    {
        switch (address)
        {
            case UciConstants.CommandAddress:
                _ram[_commandPointer] = value;
                if (_commandPointer != UciConstants.CommandBufferEnd)
                    _commandPointer++;
                break;

            case UciConstants.ControlAddress:
                WriteControl(value);
                break;

            default:
                // $DF1B, $DF1E, $DF1F are read-only from the C64 side.
                break;
        }
    }

    // Order of these clauses matches command_protocol.vhd lines 148-170.
    private void WriteControl(byte value)
    {
        if ((value & UciConstants.ControlClearError) != 0)
            _errorBusy = false;

        if ((value & UciConstants.ControlPushCommand) != 0)
        {
            _freeze  = (value & UciConstants.ControlDma) != 0;
            _trigger = (value & UciConstants.ControlTrigger) != 0;

            if (_state == UciConstants.StateIdle)
            {
                _state = UciConstants.StateBusy;
                _newCommand = true;
                ArmService();
            }
            else
            {
                _errorBusy = true;
            }

            _commandIrqEnabled = (value & UciConstants.ControlIrqEnable) != 0;
        }

        if ((value & UciConstants.ControlDataAccept) != 0 && (_state & 0x20) != 0)
        {
            // Only "data more" leaves the accepted flag set for the Ultimate to see.
            _dataAccepted = (_state & 0x10) != 0;
            _state &= unchecked((byte)~0x20);
            _commandIrqEnabled = false;
            if (_dataAccepted) ArmService();
        }

        if ((value & UciConstants.ControlAbort) != 0)
        {
            _abort = true;
            ArmService();
        }

        ServicePending();
    }

    private void ArmService() => _serviceAtCycle = CycleCounter() + LatencyCycles;

    private void ServicePending()
    {
        if (!ServiceEnabled) return;
        if (!_newCommand && !_dataAccepted && !_abort) return;
        if (CycleCounter() < _serviceAtCycle) return;
        ServiceUltimate();
    }

    // Mirrors CommandInterface::run_task, command_intf.cc lines 116-171.
    //
    // Upstream latches status_byte from its queue once and tests that copy in
    // all three branches. Testing the live fields instead would skip the later
    // branches whenever the abort branch's HANDSHAKE_RESET clears their flags —
    // observable from a single write of DATA_ACCEPT|ABORT.
    private void ServiceUltimate()
    {
        var abort = _abort;
        var dataAccepted = _dataAccepted;
        var newCommand = _newCommand;

        if (abort)
        {
            _activeTarget?.Abort(_responsePointer - UciConstants.ResponseBufferStart);
            HandshakeOut(UciConstants.HandshakeReset);
        }

        if (dataAccepted)
        {
            if (_activeTarget != null)
            {
                CopyResult(_activeTarget.GetMoreData());
            }
            else
            {
                Logger.Warn("UCI: more data requested but no target is active");
            }
            HandshakeOut(UciConstants.HandshakeAcceptNextData);
        }

        if (newCommand)
        {
            var length = CommandLength;
            if (length > 0)
            {
                var targetId = _ram[UciConstants.CommandBufferStart] & UciConstants.TargetMask;
                var noReply  = (_ram[UciConstants.CommandBufferStart] & UciConstants.NoReplyFlag) != 0;
                _activeTarget = _targets[targetId] ?? Empty;

                var command = new byte[length];
                Array.Copy(_ram, UciConstants.CommandBufferStart, command, 0, length);

                if (Logger.IsTraceEnabled)
                    Logger.Trace($"UCI: target ${targetId:X2} command " +
                                 $"${(length > 1 ? command[1] : 0):X2} ({length} bytes)");
                var reply = _activeTarget.ParseCommand(command);

                HandshakeOut(UciConstants.HandshakeAcceptCommand);

                if (noReply) HandshakeOut(UciConstants.HandshakeReset);
                else CopyResult(reply);
            }
            else
            {
                Logger.Debug("UCI: null command");
                _responseLength = 0;
                _statusLength = 0;
                HandshakeOut(UciConstants.HandshakeAcceptCommand);
                HandshakeOut(UciConstants.HandshakeValidateLast);
            }
        }
    }

    // Mirrors CommandInterface::copy_result, command_intf.cc lines 173-191.
    private void CopyResult(UciReply reply)
    {
        // A misbehaving ICommandTarget can return default(UciReply); Data and
        // Status are null on a default record struct. Don't trust the target.
        var data = reply.Data ?? Array.Empty<byte>();
        var statusText = reply.Status ?? string.Empty;
        if (data.Length > UciConstants.ResponseBufferSize)
        {
            Logger.Warn($"UCI: reply of {data.Length} bytes exceeds the " +
                        $"{UciConstants.ResponseBufferSize}-byte response buffer; truncating");
            data = data[..UciConstants.ResponseBufferSize];
        }

        var status = System.Text.Encoding.ASCII.GetBytes(statusText);
        if (status.Length > UciConstants.StatusBufferSize)
        {
            Logger.Warn($"UCI: status of {status.Length} bytes exceeds the " +
                        $"{UciConstants.StatusBufferSize}-byte status buffer; truncating");
            status = status[..UciConstants.StatusBufferSize];
        }

        Array.Copy(data, 0, _ram, UciConstants.ResponseBufferStart, data.Length);
        Array.Copy(status, 0, _ram, UciConstants.StatusBufferStart, status.Length);
        _responseLength = data.Length;
        _statusLength = status.Length;

        HandshakeOut(reply.LastPart
            ? UciConstants.HandshakeValidateLast
            : UciConstants.HandshakeValidateMore);
    }

    // Mirrors command_protocol.vhd lines 209-232 (c_cif_io_handshake_out).
    private void HandshakeOut(byte value)
    {
        if ((value & 0x01) != 0)
        {
            _newCommand = false;
            _commandPointer = UciConstants.CommandBufferStart;
        }

        if ((value & 0x02) != 0)
            _dataAccepted = false;

        if ((value & 0x04) != 0)
            _abort = false;

        if ((value & 0x10) != 0)
        {
            _trigger = false;
            _freeze = false;
            // Set the data bit; bit 5 of the handshake value carries the "more" bit.
            _state = (byte)(0x20 | ((value & 0x20) != 0 ? 0x10 : 0x00));
            ResetResponse();
        }

        if ((value & 0x80) != 0)
        {
            _freeze = false;
            _trigger = false;
            ResetResponse();
            _state = UciConstants.StateIdle;
        }
    }

    private void ResetResponse()
    {
        _responsePointer = UciConstants.ResponseBufferStart;
        _statusPointer = UciConstants.StatusBufferStart;
    }

    /// <summary>
    /// Run a command from the host, bypassing the C64-visible registers. Used by
    /// the DSL's uci() function. Walks every continuation part and concatenates
    /// the data.
    /// </summary>
    public (string Status, byte[] Data) IssueHostCommand(byte[] command)
    {
        if (command.Length < 2)
            throw new ArgumentException("A UCI command needs at least a target byte and a command byte",
                nameof(command));

        var targetId = command[0] & UciConstants.TargetMask;
        var target = _targets[targetId] ?? Empty;

        var data = new List<byte>();
        var status = UciConstants.StatusEmpty;

        var reply = target.ParseCommand(command);
        var parts = 0;
        while (true)
        {
            data.AddRange(reply.Data);
            if (reply.Status.Length > 0) status = reply.Status;
            if (reply.LastPart) break;

            if (++parts > MaxContinuationParts)
            {
                Logger.Error($"UCI: target ${targetId:X2} produced more than " +
                             $"{MaxContinuationParts} parts without a last part; giving up");
                break;
            }
            reply = target.GetMoreData();
        }

        return (status, data.ToArray());
    }
}
