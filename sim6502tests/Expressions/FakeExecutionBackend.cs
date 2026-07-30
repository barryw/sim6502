using System;
using System.Collections.Generic;
using sim6502.Backend;

namespace sim6502tests.Expressions;

/// <summary>
/// Minimal in-memory fake of <see cref="IExecutionBackend"/> used to exercise the
/// expression evaluators (MemoryCompare, BaseCompare) without spinning up a real
/// emulator backend. Only the memory read/write surface is implemented; everything
/// else throws because these tests never touch it.
/// </summary>
internal class FakeExecutionBackend : IExecutionBackend
{
    private readonly Dictionary<int, byte> _memory = new();

    public byte ReadByte(int address) => _memory.TryGetValue(address, out var v) ? v : (byte)0;

    public int ReadWord(int address) => ReadByte(address) | (ReadByte(address + 1) << 8);

    public void WriteByte(int address, byte value) => _memory[address] = value;

    public void WriteWord(int address, int value)
    {
        _memory[address] = (byte)(value & 0xFF);
        _memory[address + 1] = (byte)((value >> 8) & 0xFF);
    }

    public void LoadBinary(byte[] data, int address) => throw new NotImplementedException();
    public void WriteMemoryValue(int address, int value) => throw new NotImplementedException();
    public int GetRegister(string name) => throw new NotImplementedException();
    public void SetRegister(string name, int value) => throw new NotImplementedException();
    public bool GetFlag(string name) => throw new NotImplementedException();
    public void SetFlag(string name, bool value) => throw new NotImplementedException();

    public ExecutionResult ExecuteJsr(int address, int stopOnAddress, bool stopOnRts, bool failOnBrk) =>
        throw new NotImplementedException();

    public int GetCycles() => throw new NotImplementedException();
    public void ResetCycleCount() => throw new NotImplementedException();
    public void LoadSymbols(string path) => throw new NotImplementedException();
    public void SaveSnapshot(string name) => throw new NotImplementedException();
    public void RestoreSnapshot(string name) => throw new NotImplementedException();
    public void Reset() => throw new NotImplementedException();
    public void SetWarpMode(bool enabled) => throw new NotImplementedException();

    public bool TraceEnabled { get; set; }
    public void ClearTraceBuffer() => throw new NotImplementedException();
    public List<string> GetTraceBuffer() => throw new NotImplementedException();

    public void Dispose()
    {
    }
}
