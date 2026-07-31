using sim6502.Backend;
using sim6502.Systems.Ultimate;

namespace sim6502tests.Backend;

/// <summary>
/// An IU64Connection backed by u64sim's own UciRegisters.
///
/// This gives a high-fidelity model of the real handshake for free, including
/// the availability bit's saturation quirk: it clears normally once the read
/// pointer passes the reply's length, but sticks when a reply exactly fills
/// its queue and the pointer saturates at the last slot instead of advancing
/// past it -- the behaviour that forces every drain loop to be bounded by its
/// own queue's capacity rather than trusting the bit to clear.
/// </summary>
public sealed class FakeU64Connection : IU64Connection
{
    private readonly UciRegisters _uci;
    private long _cycles;

    /// <summary>Every address passed to <see cref="WriteByte"/>, in order.</summary>
    public List<int> WrittenAddresses { get; } = new();

    /// <summary>Addresses outside the UCI block, so plain memory ops work.</summary>
    private readonly Dictionary<int, byte> _memory = new();

    public FakeU64Connection(int latencyCycles = 0, params (int Target, ICommandTarget Impl)[] targets)
    {
        _uci = new UciRegisters(latencyCycles)
        {
            ServiceEnabled = true,
            // Every access advances the clock, so a busy-wait loop makes progress
            // exactly as it would against a running CPU.
            CycleCounter = () => _cycles
        };

        foreach (var (id, impl) in targets)
            _uci.RegisterTarget(id, impl);
    }

    private static bool IsUci(int address) =>
        address >= UciConstants.BusIdAddress && address <= UciConstants.StatusAddress;

    public byte ReadByte(int address)
    {
        _cycles += 8;
        if (IsUci(address)) return _uci.Read(address);
        return _memory.TryGetValue(address, out var v) ? v : (byte)0;
    }

    public void WriteByte(int address, byte value)
    {
        WrittenAddresses.Add(address);
        _cycles += 8;
        if (IsUci(address)) _uci.Write(address, value);
        else _memory[address] = value;
    }

    public byte[] ReadBytes(int address, int length)
    {
        var result = new byte[length];
        for (var i = 0; i < length; i++) result[i] = ReadByte(address + i);
        return result;
    }

    public void WriteBytes(int address, byte[] data)
    {
        for (var i = 0; i < data.Length; i++) WriteByte(address + i, data[i]);
    }

    public int ResetCount { get; private set; }
    public void ResetMachine() => ResetCount++;

    public void Dispose() { }
}
