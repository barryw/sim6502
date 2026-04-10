namespace sim6502.Backend;

/// <summary>
/// High-level backend for BASIC-level integration testing.
/// Requires a running emulator with screen/keyboard support.
/// </summary>
public interface IHighLevelBackend
{
    void SendText(string text);
    void SendKey(string key);
    string[] ReadScreen();
    string ReadLine(int row);
    (int x, int y) GetCursor();
    void WaitForText(string text, int timeoutMs = 5000);
    void ColdStart();
    void Pause();
    void Resume();
    void RunCycles(int count);
    void WaitForMemory(int addr, byte value, int timeoutMs = 5000);
}
