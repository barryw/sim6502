using NLog;

namespace sim6502.Systems;

/// <summary>
/// Commodore 64 memory map with full banking support via $01.
///
/// Memory regions:
/// - $0000-$0001: 6510 I/O port (DDR and Data)
/// - $0002-$9FFF: RAM (always)
/// - $A000-$BFFF: BASIC ROM or RAM (controlled by LORAM bit)
/// - $C000-$CFFF: RAM (always)
/// - $D000-$DFFF: I/O or CHAR ROM or RAM (controlled by CHAREN bit)
/// - $E000-$FFFF: KERNAL ROM or RAM (controlled by HIRAM bit)
///
/// Banking controlled by $01 bits 0-2:
/// - Bit 0 (LORAM):  1 = BASIC ROM visible, 0 = RAM
/// - Bit 1 (HIRAM):  1 = KERNAL ROM visible, 0 = RAM
/// - Bit 2 (CHAREN): 1 = I/O visible, 0 = CHAR ROM (when HIRAM or LORAM set)
///
/// IMPORTANT: Writes ALWAYS go to RAM, even when ROM is banked in.
/// This is the key C64 behavior that allows "under-ROM" RAM usage.
/// </summary>
public class C64MemoryMap : IMemoryMap
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    private byte[] _ram = new byte[0x10000];
    private byte[] _basicRom = new byte[0x2000];   // $A000-$BFFF
    private byte[] _kernalRom = new byte[0x2000];  // $E000-$FFFF
    private byte[] _charRom = new byte[0x1000];    // $D000-$DFFF (when visible)

    private byte _dataDirection = 0x2F; // Default DDR
    private byte _dataPort = 0x37;      // Default: all ROMs visible

    // I/O region stub storage (VIC, SID, CIA, etc.)
    private byte[] _ioRegisters = new byte[0x1000];

    public Action IncrementCycleCount { get; set; } = () => { };

    // Banking bits from $01
    private bool LoRam => (_dataPort & 0x01) != 0;  // BASIC visible
    private bool HiRam => (_dataPort & 0x02) != 0;  // KERNAL visible
    private bool Charen => (_dataPort & 0x04) != 0; // I/O vs CHAR ROM

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

        // $00-$01: Always I/O port
        if (address == 0x00) return _dataDirection;
        if (address == 0x01) return _dataPort;

        // $0002-$9FFF: Always RAM
        if (address < 0xA000) return _ram[address];

        // $A000-$BFFF: BASIC ROM or RAM
        if (address < 0xC000)
        {
            if (LoRam && HiRam) // Both bits must be set for BASIC
                return _basicRom[address - 0xA000];
            return _ram[address];
        }

        // $C000-$CFFF: Always RAM
        if (address < 0xD000) return _ram[address];

        // $D000-$DFFF: I/O, CHAR ROM, or RAM
        if (address < 0xE000)
        {
            if (!LoRam && !HiRam)
                return _ram[address]; // All RAM mode

            if (Charen)
                return _ioRegisters[address - 0xD000]; // I/O visible

            return _charRom[address - 0xD000]; // CHAR ROM visible
        }

        // $E000-$FFFF: KERNAL ROM or RAM
        if (HiRam)
            return _kernalRom[address - 0xE000];

        return _ram[address];
    }

    private void WriteInternal(int address, byte value)
    {
        address &= 0xFFFF;

        // $00-$01: I/O port
        if (address == 0x00)
        {
            _dataDirection = value;
            Logger.Trace($"C64 DDR = ${value:X2}");
            return;
        }

        if (address == 0x01)
        {
            _dataPort = value;
            Logger.Trace($"C64 Port = ${value:X2} (LORAM={LoRam}, HIRAM={HiRam}, CHAREN={Charen})");
            return;
        }

        // $D000-$DFFF: Write to I/O registers if visible, but ALSO write to RAM underneath
        // This is key C64 behavior - RAM always receives writes even when I/O is visible
        if (address >= 0xD000 && address < 0xE000)
        {
            if ((LoRam || HiRam) && Charen)
            {
                _ioRegisters[address - 0xD000] = value;
                // Fall through to also write to RAM!
            }
        }

        // ALL writes go to RAM (this is the key C64 behavior!)
        _ram[address] = value;
    }

    public void LoadRom(string name, byte[] data)
    {
        switch (name.ToLowerInvariant())
        {
            case "basic":
                if (data.Length > 0x2000)
                    throw new InvalidOperationException("BASIC ROM must be <= 8KB");
                Array.Copy(data, _basicRom, Math.Min(data.Length, 0x2000));
                Logger.Info($"Loaded BASIC ROM ({data.Length} bytes)");
                break;

            case "kernal":
                if (data.Length > 0x2000)
                    throw new InvalidOperationException("KERNAL ROM must be <= 8KB");
                Array.Copy(data, _kernalRom, Math.Min(data.Length, 0x2000));
                Logger.Info($"Loaded KERNAL ROM ({data.Length} bytes)");
                break;

            case "chargen":
            case "char":
                if (data.Length > 0x1000)
                    throw new InvalidOperationException("CHAR ROM must be <= 4KB");
                Array.Copy(data, _charRom, Math.Min(data.Length, 0x1000));
                Logger.Info($"Loaded CHAR ROM ({data.Length} bytes)");
                break;

            default:
                Logger.Warn($"Unknown ROM type '{name}' - ignored");
                break;
        }
    }

    public void LoadProgram(int address, byte[] data)
    {
        if (address + data.Length > 0x10000)
            throw new InvalidOperationException(
                $"Program at ${address:X4} with size {data.Length} exceeds 64KB");

        for (var i = 0; i < data.Length; i++)
            _ram[address + i] = data[i];

        Logger.Debug($"Loaded {data.Length} bytes at ${address:X4}");
    }

    public byte[] GetRam() => _ram;

    public void Reset()
    {
        _ram = new byte[0x10000];
        _ioRegisters = new byte[0x1000];
        _dataDirection = 0x2F;
        _dataPort = 0x37; // Default banking
        // Keep ROMs loaded
        Logger.Debug("C64 memory reset (ROMs preserved)");
    }
}
