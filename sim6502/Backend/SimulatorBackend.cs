using NLog;
using sim6502.Proc;
using sim6502.Systems;

namespace sim6502.Backend;

public class SimulatorBackend : IExecutionBackend
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    public Processor Processor { get; }

    public SimulatorBackend(ProcessorType processorType, IMemoryMap memoryMap)
    {
        Processor = new Processor(processorType, memoryMap);
        Processor.Reset();
    }

    public void LoadBinary(byte[] data, int address)
    {
        Processor.LoadProgram(address, data, address, false);
    }

    public void WriteByte(int address, byte value)
    {
        Processor.WriteMemoryValueWithoutIncrement(address, value);
    }

    public void WriteWord(int address, int value)
    {
        Processor.WriteMemoryWord(address, value);
    }

    public void WriteMemoryValue(int address, int value)
    {
        Processor.WriteMemoryValue(address, value);
    }

    public byte ReadByte(int address)
    {
        return Processor.ReadMemoryValueWithoutCycle(address);
    }

    public int ReadWord(int address)
    {
        return Processor.ReadMemoryWordWithoutCycle(address);
    }

    public int GetRegister(string name)
    {
        return name.ToLower() switch
        {
            "a" => Processor.Accumulator,
            "x" => Processor.XRegister,
            "y" => Processor.YRegister,
            _ => throw new ArgumentException($"Unknown register: {name}")
        };
    }

    public void SetRegister(string name, int value)
    {
        switch (name.ToLower())
        {
            case "a": Processor.Accumulator = value; break;
            case "x": Processor.XRegister = value; break;
            case "y": Processor.YRegister = value; break;
            default: throw new ArgumentException($"Unknown register: {name}");
        }
    }

    public bool GetFlag(string name)
    {
        return name.ToLower() switch
        {
            "c" => Processor.CarryFlag,
            "z" => Processor.ZeroFlag,
            "n" => Processor.NegativeFlag,
            "v" => Processor.OverflowFlag,
            "d" => Processor.DecimalFlag,
            _ => throw new ArgumentException($"Unknown flag: {name}")
        };
    }

    public void SetFlag(string name, bool value)
    {
        switch (name.ToLower())
        {
            case "c": Processor.CarryFlag = value; break;
            case "z": Processor.ZeroFlag = value; break;
            case "n": Processor.NegativeFlag = value; break;
            case "v": Processor.OverflowFlag = value; break;
            case "d": Processor.DecimalFlag = value; break;
            default: throw new ArgumentException($"Unknown flag: {name}");
        }
    }

    public ExecutionResult ExecuteJsr(int address, int stopOnAddress, bool stopOnRts, bool failOnBrk)
    {
        var cleanExit = Processor.RunRoutine(address, stopOnAddress, stopOnRts, failOnBrk);
        return new ExecutionResult
        {
            ExitedCleanly = cleanExit,
            Reason = cleanExit ? StopReason.Rts : StopReason.Brk,
            CyclesElapsed = Processor.CycleCount,
            ProgramCounter = Processor.ProgramCounter
        };
    }

    public int GetCycles() => Processor.CycleCount;
    public void ResetCycleCount() => Processor.ResetCycleCount();

    public void LoadSymbols(string path) { }
    public void SaveSnapshot(string name) { }
    public void RestoreSnapshot(string name) { }
    public void Reset() => Processor.Reset();
    public void SetWarpMode(bool enabled) { }

    public bool TraceEnabled
    {
        get => Processor.TraceEnabled;
        set => Processor.TraceEnabled = value;
    }

    public void ClearTraceBuffer() => Processor.ClearTraceBuffer();
    public List<string> GetTraceBuffer() => Processor.GetTraceBuffer();

    public void Dispose() { }
}
