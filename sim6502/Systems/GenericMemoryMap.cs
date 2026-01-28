using NLog;

namespace sim6502.Systems;

/// <summary>
/// Generic memory map with flat 64KB RAM.
/// No banking, no ROM overlays. Used for generic_6502, generic_6510, generic_65c02.
/// </summary>
public class GenericMemoryMap : IMemoryMap
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
    private byte[] _ram = new byte[0x10000];

    public Action IncrementCycleCount { get; set; } = () => { };

    public byte Read(int address)
    {
        IncrementCycleCount();
        return _ram[address & 0xFFFF];
    }

    public void Write(int address, byte value)
    {
        IncrementCycleCount();
        _ram[address & 0xFFFF] = value;
    }

    public byte ReadWithoutCycle(int address)
    {
        var value = _ram[address & 0xFFFF];
        Logger.Trace($"Read BYTE {value:X2} from ${address:X4}");
        return value;
    }

    public void WriteWithoutCycle(int address, byte value)
    {
        Logger.Trace($"Write BYTE {value:X2} to ${address:X4}");
        _ram[address & 0xFFFF] = value;
    }

    public void LoadRom(string name, byte[] data)
    {
        // Generic systems don't have ROM - log and ignore
        Logger.Warn($"LoadRom('{name}') ignored - generic systems have no ROM support");
    }

    public void LoadProgram(int address, byte[] data)
    {
        if (address + data.Length > 0x10000)
            throw new InvalidOperationException(
                $"Program at ${address:X4} with size {data.Length} exceeds 64KB address space");

        for (var i = 0; i < data.Length; i++)
            _ram[address + i] = data[i];

        Logger.Debug($"Loaded {data.Length} bytes at ${address:X4}");
    }

    public byte[] GetRam() => _ram;

    public void Reset()
    {
        _ram = new byte[0x10000];
        Logger.Debug("Memory reset to all zeros");
    }
}
