using NLog;
using SixtyFiveXX;
using SixtyFiveXX.Variants;
using sim6502.Systems;

namespace sim6502.Backend;

/// <summary>
/// Executes test suites on a SixtyFiveXX core.
/// </summary>
/// <remarks>
/// The processor is chosen at run time, from a <c>processor(...)</c> declaration in the
/// DSL, but SixtyFiveXX closes over its variant as a <em>type</em> parameter so the JIT
/// can fold the variant tests away. <see cref="Core{TVariant}"/> bridges the two: one
/// generic class, instantiated once per variant by the constructor's switch, behind a
/// non-generic interface. That keeps <c>new SimulatorBackend(type, map)</c> working for
/// every existing caller and stops five hand-maintained copies drifting apart.
/// </remarks>
public class SimulatorBackend : IExecutionBackend
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    private readonly ICore _core;
    private readonly MemoryMapBus _bus;
    private readonly List<string> _trace = [];

    /// <summary>The processor this backend was built for.</summary>
    public ProcessorType ProcessorType { get; }

    /// <summary>Creates a backend running <paramref name="processorType"/> over <paramref name="memoryMap"/>.</summary>
    public SimulatorBackend(ProcessorType processorType, IMemoryMap memoryMap)
    {
        ArgumentNullException.ThrowIfNull(memoryMap);

        ProcessorType = processorType;
        _bus = new MemoryMapBus(memoryMap);

        _core = processorType switch
        {
            ProcessorType.MOS6502 => new Core<Mos6502Variant>(_bus),

            // Deliberately the 6502 variant, not Mos6510Variant. The 6510's instruction
            // set IS the 6502's; what makes it a 6510 is the $00/$01 port, and in this
            // simulator that port belongs to the memory map — C64MemoryMap drives its
            // entire banking scheme from $01. SixtyFiveXX's Mos6510Variant answers those
            // two addresses inside the CPU, before the bus, so choosing it here would
            // stop every bank switch reaching the map and freeze a C64 in its power-on
            // configuration.
            ProcessorType.MOS6510 => new Core<Mos6502Variant>(_bus),

            ProcessorType.WDC65C02 => new Core<Wdc65C02Variant>(_bus),

            _ => throw new ArgumentOutOfRangeException(
                nameof(processorType), processorType, "No SixtyFiveXX core for this processor."),
        };

        _core.Reset();
    }

    // ── Memory ────────────────────────────────────────────────────────────────────
    //
    // These are the harness reaching into memory, not the processor running, so they use
    // the map's WithoutCycle pair: arranging a test or asserting on it afterwards must not
    // look like a cycle the program spent.

    /// <inheritdoc />
    public void LoadBinary(byte[] data, int address) => _bus.Map.LoadProgram(address, data);

    /// <inheritdoc />
    public void WriteByte(int address, byte value) => _bus.Map.WriteWithoutCycle(address, value);

    /// <inheritdoc />
    public void WriteWord(int address, int value)
    {
        _bus.Map.WriteWithoutCycle(address, (byte)(value & 0xFF));
        _bus.Map.WriteWithoutCycle(address + 1, (byte)((value >> 8) & 0xFF));
    }

    /// <summary>
    /// Writes a byte, or a word when the value will not fit in one.
    /// </summary>
    /// <remarks>
    /// The width is chosen from the <em>value</em>, not from the caller's intent, which is
    /// how the DSL has always behaved: <c>poke</c> of <c>$FF</c> writes one byte and
    /// <c>poke</c> of <c>$100</c> writes two. Truncating to a byte here instead silently
    /// dropped the high half of every word a suite poked.
    /// </remarks>
    public void WriteMemoryValue(int address, int value)
    {
        if (value > 0xFF) WriteWord(address, value);
        else WriteByte(address, (byte)value);
    }

    /// <inheritdoc />
    public byte ReadByte(int address) => _bus.Map.ReadWithoutCycle(address);

    /// <inheritdoc />
    public int ReadWord(int address) =>
        _bus.Map.ReadWithoutCycle(address) | (_bus.Map.ReadWithoutCycle(address + 1) << 8);

    // ── Registers and flags ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public int GetRegister(string name) => name.ToLowerInvariant() switch
    {
        "a" => _core.A,
        "x" => _core.X,
        "y" => _core.Y,
        _ => throw new ArgumentException($"Unknown register: {name}"),
    };

    /// <inheritdoc />
    public void SetRegister(string name, int value)
    {
        switch (name.ToLowerInvariant())
        {
            case "a": _core.A = (byte)value; break;
            case "x": _core.X = (byte)value; break;
            case "y": _core.Y = (byte)value; break;
            default: throw new ArgumentException($"Unknown register: {name}");
        }
    }

    /// <inheritdoc />
    public bool GetFlag(string name) => _core.GetFlag(FlagFor(name));

    /// <inheritdoc />
    public void SetFlag(string name, bool value) => _core.SetFlag(FlagFor(name), value);

    private static byte FlagFor(string name) => name.ToLowerInvariant() switch
    {
        "c" => Flag.C,
        "z" => Flag.Z,
        "n" => Flag.N,
        "v" => Flag.V,
        "d" => Flag.D,
        _ => throw new ArgumentException($"Unknown flag: {name}"),
    };

    // ── Execution ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a routine, stopping on its closing <c>RTS</c>, on a <c>BRK</c>, or on
    /// <paramref name="stopOnAddress"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Despite the name it does not perform a <c>JSR</c>: the program counter is set to
    /// <paramref name="address"/> and nothing is pushed. The depth count starts at one so
    /// the routine's own <c>RTS</c> closes it, which means that final <c>RTS</c> pops
    /// whatever the caller happened to leave on the stack.
    /// </para>
    /// <para>
    /// Three details are load-bearing and none are obvious. The opcode is inspected
    /// <em>before</em> the step, so the instruction that ends the run still executes — the
    /// closing <c>RTS</c> runs and the program counter lands on the return address.
    /// <paramref name="stopOnAddress"/> is honoured only when greater than zero. And
    /// because the stop-address check also precedes the step, arriving at that address
    /// still costs one more instruction. All three reproduce the previous engine exactly;
    /// every one of them is observable to an existing test suite.
    /// </para>
    /// </remarks>
    public ExecutionResult ExecuteJsr(int address, int stopOnAddress, bool stopOnRts, bool failOnBrk)
    {
        const byte jsr = 0x20, rts = 0x60, brk = 0x00;

        _core.PC = (ushort)address;

        var depth = 1;
        var keepRunning = true;
        var exitedCleanly = true;
        var hitBrk = false;

        do
        {
            // Peeked without a cycle: this is the harness looking ahead, not the processor
            // fetching. The processor fetches the same byte itself when it steps.
            var opcode = _bus.Map.ReadWithoutCycle(_core.PC);

            if (opcode == jsr) depth++;

            if (opcode == rts)
            {
                depth--;
                if (depth == 0 && stopOnRts) keepRunning = false;
            }

            if (opcode == brk)
            {
                keepRunning = false;
                hitBrk = true;
                if (failOnBrk) exitedCleanly = false;
            }

            if (_core.PC == stopOnAddress && stopOnAddress > 0) keepRunning = false;

            TraceCurrentInstruction();
            _core.Step();
        } while (keepRunning);

        return new ExecutionResult
        {
            ExitedCleanly = exitedCleanly,
            Reason = hitBrk ? StopReason.Brk : StopReason.Rts,
            CyclesElapsed = (int)_core.Cycles,
            ProgramCounter = _core.PC,
        };
    }

    /// <summary>
    /// Records the instruction about to execute, if anything is listening.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two audiences, and they are enabled separately. <c>--trace</c> only lowers NLog's
    /// level, so trace output reaches the console through <see cref="Logger"/> and is
    /// emitted for <em>every</em> instruction; the in-memory buffer is filled only when a
    /// test asks for it with <c>trace = true</c>, because that is what the DSL reports
    /// back per test. Logging unconditionally while buffering conditionally is what the
    /// previous engine did, and dropping the log half made <c>--trace</c> print nothing.
    /// </para>
    /// <para>
    /// The guard matters: composing a line disassembles an instruction, so doing it on
    /// every step when nobody is watching would be pure cost on the hot path.
    /// </para>
    /// </remarks>
    private void TraceCurrentInstruction()
    {
        if (!TraceEnabled && !Logger.IsDebugEnabled) return;

        var line = _core.TraceLine();
        if (TraceEnabled) _trace.Add(line);
        Logger.Trace(line);
    }

    /// <inheritdoc />
    public int GetCycles() => (int)_core.Cycles;

    /// <inheritdoc />
    public void ResetCycleCount() => _core.ResetCycleCount();

    /// <inheritdoc />
    public void Reset() => _core.Reset();

    // ── Not applicable to an in-process simulator ─────────────────────────────────

    /// <inheritdoc />
    public void LoadSymbols(string path) { }

    /// <inheritdoc />
    public void SaveSnapshot(string name) { }

    /// <inheritdoc />
    public void RestoreSnapshot(string name) { }

    /// <inheritdoc />
    public void SetWarpMode(bool enabled) { }

    // ── Trace ─────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public bool TraceEnabled { get; set; }

    /// <inheritdoc />
    public void ClearTraceBuffer() => _trace.Clear();

    /// <inheritdoc />
    public List<string> GetTraceBuffer() => _trace;

    /// <inheritdoc />
    public void Dispose() { }

    /// <summary>
    /// The variant-agnostic face of a SixtyFiveXX core, so the backend holds one field
    /// rather than repeating a switch at every call site.
    /// </summary>
    private interface ICore
    {
        ushort PC { get; set; }
        byte A { get; set; }
        byte X { get; set; }
        byte Y { get; set; }
        long Cycles { get; }

        bool GetFlag(byte flag);
        void SetFlag(byte flag, bool value);
        void Step();
        void Reset();
        void ResetCycleCount();
        string TraceLine();
    }

    /// <summary>One closed core. Instantiated once per variant by the constructor.</summary>
    private sealed class Core<TVariant> : ICore where TVariant : struct, ICpuVariant
    {
        private readonly Cpu<RefBus, TVariant> _cpu;
        private readonly MemoryMapBus _bus;

        public Core(MemoryMapBus bus)
        {
            _bus = bus;
            _cpu = new Cpu<RefBus, TVariant>(new RefBus(bus));
        }

        public ushort PC { get => _cpu.State.PC; set => _cpu.State.PC = value; }
        public byte A { get => _cpu.State.A; set => _cpu.State.A = value; }
        public byte X { get => _cpu.State.X; set => _cpu.State.X = value; }
        public byte Y { get => _cpu.State.Y; set => _cpu.State.Y = value; }
        public long Cycles => _cpu.Cycles;

        public bool GetFlag(byte flag) => (_cpu.State.P & flag) != 0;

        public void SetFlag(byte flag, bool value)
        {
            if (value) _cpu.State.P |= flag;
            else _cpu.State.P = (byte)(_cpu.State.P & ~flag);
        }

        public void Step() => _cpu.Step();

        public void ResetCycleCount() => _cpu.ResetCycleCount();

        /// <summary>
        /// Puts the processor in the state a test starts from: interrupts masked, the
        /// stack where a reset leaves it, registers clear and the cycle count zeroed.
        /// </summary>
        /// <remarks>
        /// The seven-cycle reset sequence is deliberately not run. It would fetch a vector
        /// from <c>$FFFC</c>, and a test suite supplies its own entry point rather than a
        /// reset vector — in a bare memory map those bytes are zero, so running it would
        /// leave the program counter at <c>$0000</c> and charge seven cycles for it.
        /// </remarks>
        public void Reset()
        {
            _cpu.State.A = 0;
            _cpu.State.X = 0;
            _cpu.State.Y = 0;
            _cpu.State.S = 0xFD;
            _cpu.State.P = Flag.U | Flag.I;
            _cpu.ResetCycleCount();
        }

        /// <summary>
        /// One line of execution trace, in the format sim6502 has always emitted. Called
        /// before the instruction runs, so the program counter still points at it.
        /// </summary>
        /// <remarks>
        /// The mnemonic and operand come from SixtyFiveXX's disassembler, which is driven
        /// by the same opcode table this core executes, so the trace cannot describe an
        /// instruction other than the one that runs. The register and flag decoration is
        /// added here — which is the reason that disassembler returns operand text rather
        /// than a finished line.
        /// </remarks>
        public string TraceLine()
        {
            var decoded = Disassembler.Decode<RefBus, TVariant>(new RefBus(_bus), _cpu.State.PC);
            var s = _cpu.State;

            return $"${s.PC:X4}: {decoded.Mnemonic} {decoded.Operand.PadRight(10)} " +
                   $"A=${s.A:X2} X=${s.X:X2} Y=${s.Y:X2} SP=${s.S:X2} {Flags(s.P)}";
        }

        /// <summary>
        /// The flags, in the exact eight characters sim6502 has always printed: upper case
        /// when set, lower case when clear.
        /// </summary>
        /// <remarks>
        /// <strong>The <c>-bd</c> in the middle is three literal characters, not flags.</strong>
        /// The original formatter hardcoded them, so a trace has never shown the state of
        /// <c>B</c> or <c>D</c> — a program in decimal mode looks identical to one that is
        /// not. That is reproduced rather than fixed: the trace is output other tools read,
        /// and quietly changing its shape during a core swap would be a breaking change
        /// wearing the clothes of a bug fix. Worth correcting deliberately, on its own.
        /// </remarks>
        private static string Flags(byte p)
        {
            char On(byte flag, char c) => (p & flag) != 0 ? c : char.ToLowerInvariant(c);

            return $"{On(Flag.N, 'N')}{On(Flag.V, 'V')}-bd{On(Flag.I, 'I')}{On(Flag.Z, 'Z')}{On(Flag.C, 'C')}";
        }
    }
}
