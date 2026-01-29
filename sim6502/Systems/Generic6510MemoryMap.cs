using NLog;

namespace sim6502.Systems;

/// <summary>
/// Generic 6510 memory map with flat 64KB RAM plus $00/$01 I/O port.
/// No banking or ROM overlays - just the processor port registers.
/// </summary>
public class Generic6510MemoryMap : IMemoryMap
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
    private byte[] _ram = new byte[0x10000];
    private byte _dataDirection = 0x00;
    private byte _dataPort = 0x00;

    public Action IncrementCycleCount { get; set; } = () => { };

    public byte Read(int address)
    {
        IncrementCycleCount();
        return ReadInternal(address);
    }

    public void Write(int address, byte value)
    {
        IncrementCycleCount();
        WriteInternal(address, value);
    }

    public byte ReadWithoutCycle(int address) => ReadInternal(address);

    public void WriteWithoutCycle(int address, byte value) => WriteInternal(address, value);

    private byte ReadInternal(int address)
    {
        address &= 0xFFFF;
        return address switch
        {
            0x00 => _dataDirection,
            0x01 => _dataPort,
            _ => _ram[address]
        };
    }

    private void WriteInternal(int address, byte value)
    {
        address &= 0xFFFF;
        switch (address)
        {
            case 0x00:
                _dataDirection = value;
                Logger.Trace($"6510 DDR = ${value:X2}");
                break;
            case 0x01:
                _dataPort = value;
                Logger.Trace($"6510 Data Port = ${value:X2}");
                break;
            default:
                _ram[address] = value;
                break;
        }
    }

    public void LoadRom(string name, byte[] data)
    {
        Logger.Warn($"LoadRom('{name}') ignored - generic 6510 has no ROM support");
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
        _dataDirection = 0x00;
        _dataPort = 0x00;
        Logger.Debug("Memory and I/O registers reset");
    }
}
