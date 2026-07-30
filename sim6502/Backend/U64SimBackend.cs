using NLog;
using sim6502.Proc;
using sim6502.Systems;
using sim6502.Systems.Ultimate;

namespace sim6502.Backend;

/// <summary>
/// A simulated Ultimate 64: sim6502's own 6510 core with the Ultimate Command
/// Interface mapped at $DF1B-$DF1F, two Ultimate DOS targets, and a control
/// target. Every <see cref="IExecutionBackend"/> member delegates to an inner
/// <see cref="SimulatorBackend"/>; the Ultimate behaviour is additive.
/// </summary>
public class U64SimBackend : IExecutionBackend
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    private readonly SimulatorBackend _sim;
    private readonly UltimateDosTarget _dosOne;
    private readonly UltimateDosTarget _dosTwo;
    private readonly ControlTarget _control;

    public U64SimBackend(U64SimBackendConfig config, IMemoryMap memoryMap)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(memoryMap);

        if (string.IsNullOrWhiteSpace(config.FsRoot))
            throw new ArgumentException(
                "The u64sim backend needs a filesystem root. Set --u64sim-fs-root, " +
                "or ultimate(fs_root = \"...\") in your suite file.",
                nameof(config));

        _sim = new SimulatorBackend(ProcessorType.MOS6510, memoryMap);

        // Targets $01 and $02 keep independent state, so each gets its own view.
        // The DOS target owns the filesystem it's handed (and disposes it), so
        // there's no need to hold a field here beyond construction.
        var dosFileSystemOne = new UltimateFileSystem(config.FsRoot, config.MountName);
        var dosFileSystemTwo = new UltimateFileSystem(config.FsRoot, config.MountName);
        _dosOne = new UltimateDosTarget(dosFileSystemOne, config.DosVersion);
        _dosTwo = new UltimateDosTarget(dosFileSystemTwo, config.DosVersion);
        _control = new ControlTarget(new[] { _dosOne, _dosTwo }, config.ModelName);

        Uci = new UciRegisters(config.UciLatencyCycles)
        {
            // Busy is held relative to the processor's own cycle count, so a
            // polling loop in 6502 code really does advance it.
            CycleCounter = () => _sim.Processor.CycleCount,
            ServiceEnabled = true,
            BusId = config.BusId
        };

        Uci.RegisterTarget(1, _dosOne);
        Uci.RegisterTarget(2, _dosTwo);
        Uci.RegisterTarget(4, _control);

        memoryMap.RegisterIoHandler(UciConstants.BusIdAddress, UciConstants.StatusAddress, Uci);

        Logger.Info($"u64sim ready: /{config.MountName} -> '{config.FsRoot}', " +
                    $"UCI latency {config.UciLatencyCycles} cycles");
    }

    /// <summary>The UCI register block, exposed for tests and the DSL.</summary>
    internal UciRegisters Uci { get; }

    /// <summary>
    /// Run a UCI command from the host rather than from 6502 code, walking every
    /// continuation part. Backs the DSL's uci() function.
    /// </summary>
    public (string Status, byte[] Data) IssueUciCommand(byte[] command)
        => Uci.IssueHostCommand(command);

    public void LoadBinary(byte[] data, int address) => _sim.LoadBinary(data, address);
    public void WriteByte(int address, byte value) => _sim.WriteByte(address, value);
    public void WriteWord(int address, int value) => _sim.WriteWord(address, value);
    public void WriteMemoryValue(int address, int value) => _sim.WriteMemoryValue(address, value);
    public byte ReadByte(int address) => _sim.ReadByte(address);
    public int ReadWord(int address) => _sim.ReadWord(address);

    public int GetRegister(string name) => _sim.GetRegister(name);
    public void SetRegister(string name, int value) => _sim.SetRegister(name, value);
    public bool GetFlag(string name) => _sim.GetFlag(name);
    public void SetFlag(string name, bool value) => _sim.SetFlag(name, value);

    public ExecutionResult ExecuteJsr(int address, int stopOnAddress, bool stopOnRts, bool failOnBrk)
        => _sim.ExecuteJsr(address, stopOnAddress, stopOnRts, failOnBrk);

    public int GetCycles() => _sim.GetCycles();
    public void ResetCycleCount() => _sim.ResetCycleCount();

    public void LoadSymbols(string path) => _sim.LoadSymbols(path);
    public void SaveSnapshot(string name) => _sim.SaveSnapshot(name);
    public void RestoreSnapshot(string name) => _sim.RestoreSnapshot(name);

    public void Reset()
    {
        _sim.Reset();
        _control.ParseCommand(new byte[] { 0x04, ControlTarget.CmdReboot });
    }

    public void SetWarpMode(bool enabled) => _sim.SetWarpMode(enabled);

    public bool TraceEnabled
    {
        get => _sim.TraceEnabled;
        set => _sim.TraceEnabled = value;
    }

    public void ClearTraceBuffer() => _sim.ClearTraceBuffer();
    public List<string> GetTraceBuffer() => _sim.GetTraceBuffer();

    public void Dispose()
    {
        // Each DOS target owns the filesystem it was handed and disposes it.
        _dosOne.Dispose();
        _dosTwo.Dispose();
        _sim.Dispose();
    }
}
