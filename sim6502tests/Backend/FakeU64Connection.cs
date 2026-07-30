using sim6502.Backend;
using sim6502.Systems.Ultimate;

namespace sim6502tests.Backend;

/// <summary>
/// An IU64Connection backed by u64sim's own UciRegisters.
///
/// This gives a high-fidelity model of the real handshake for free, including
/// the upstream wart where the availability bit never clears -- the behaviour
/// that forces every drain loop to be bounded.
/// </summary>
public sealed class FakeU64Connection : IU64Connection
{
    private readonly UciRegisters _uci;
    private long _cycles;

    public int ReadCount { get; private set; }
    public int WriteCount { get; private set; }

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
        ReadCount++;
        _cycles += 8;
        if (IsUci(address)) return _uci.Read(address);
        return _memory.TryGetValue(address, out var v) ? v : (byte)0;
    }

    public void WriteByte(int address, byte value)
    {
        WriteCount++;
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

    public void Dispose() { }
}
