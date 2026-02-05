namespace sim6502.Backend;

public interface IExecutionBackend : IDisposable
{
    // Memory operations
    void LoadBinary(byte[] data, int address);
    void WriteByte(int address, byte value);
    void WriteWord(int address, int value);
    void WriteMemoryValue(int address, int value);
    byte ReadByte(int address);
    int ReadWord(int address);

    // Register operations
    int GetRegister(string name);
    void SetRegister(string name, int value);

    // Flag operations
    bool GetFlag(string name);
    void SetFlag(string name, bool value);

    // Execution
    ExecutionResult ExecuteJsr(int address, int stopOnAddress, bool stopOnRts, bool failOnBrk);
    int GetCycles();
    void ResetCycleCount();

    // Symbols
    void LoadSymbols(string path);

    // State management
    void SaveSnapshot(string name);
    void RestoreSnapshot(string name);
    void Reset();

    // Configuration
    void SetWarpMode(bool enabled);

    // Trace support
    bool TraceEnabled { get; set; }
    void ClearTraceBuffer();
    List<string> GetTraceBuffer();
}
