using System.Text.Json;
using NLog;

namespace sim6502.Backend;

/// <summary>
/// Backend that connects to the e6502 Avalonia emulator via TCP.
/// Implements both IExecutionBackend (assembly-level) and IHighLevelBackend (BASIC-level).
/// </summary>
public class NovaVmBackend : IExecutionBackend, IHighLevelBackend
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    private readonly INovaVmConnection _connection;
    private readonly NovaVmBackendConfig _config;
    private int _cycleCount;

    public NovaVmBackend(NovaVmBackendConfig config)
    {
        _config = config;
        _connection = new NovaVmConnection(config.Host, config.Port, config.TimeoutMs);
    }

    internal NovaVmBackend(NovaVmBackendConfig config, INovaVmConnection connection)
    {
        _config = config;
        _connection = connection;
    }

    public void Connect()
    {
        Logger.Info($"Connecting to e6502 emulator at {_config.Host}:{_config.Port}...");
        _connection.Connect();

        if (!_connection.Ping())
            throw new InvalidOperationException(
                $"Could not connect to e6502 emulator at {_config.Host}:{_config.Port}. Is the Avalonia app running?");

        Logger.Info("Connected to e6502 emulator.");
    }

    #region IExecutionBackend — Memory

    public void LoadBinary(byte[] data, int address)
    {
        // Poke bytes one at a time — the TCP protocol has single-byte poke
        for (int i = 0; i < data.Length; i++)
            WriteByte(address + i, data[i]);
    }

    public void WriteByte(int address, byte value)
    {
        _connection.Send("poke", new Dictionary<string, object>
        {
            { "address", address },
            { "value", (int)value }
        });
    }

    public void WriteWord(int address, int value)
    {
        WriteByte(address, (byte)(value & 0xFF));
        WriteByte(address + 1, (byte)((value >> 8) & 0xFF));
    }

    public void WriteMemoryValue(int address, int value)
    {
        if (value > 255)
            WriteWord(address, value);
        else
            WriteByte(address, (byte)value);
    }

    public byte ReadByte(int address)
    {
        var result = _connection.Send("peek", new Dictionary<string, object>
        {
            { "address", address }
        });
        return (byte)result.GetProperty("value").GetInt32();
    }

    public int ReadWord(int address)
    {
        var lo = ReadByte(address);
        var hi = ReadByte(address + 1);
        return hi * 256 + lo;
    }

    #endregion

    #region IExecutionBackend — Registers & Flags

    public int GetRegister(string name)
    {
        var state = _connection.Send("dbg_state");
        var regName = name.ToLower();
        if (state.TryGetProperty(regName, out var val))
            return val.GetInt32();
        throw new ArgumentException($"Unknown register: {name}");
    }

    public void SetRegister(string name, int value)
    {
        // The e6502 TCP protocol doesn't have a direct set-register command.
        // Use poke to write to the CPU state indirectly, or pause+step.
        // For now, this is unsupported — NovaVM tests use BASIC-level commands.
        throw new NotSupportedException(
            "NovaVM backend does not support direct register writes. Use BASIC-level commands instead.");
    }

    public bool GetFlag(string name)
    {
        var state = _connection.Send("dbg_state");
        var flagName = name.ToLower() + "f";
        if (state.TryGetProperty(flagName, out var val))
            return val.GetInt32() != 0;
        throw new ArgumentException($"Unknown flag: {name}");
    }

    public void SetFlag(string name, bool value)
    {
        throw new NotSupportedException(
            "NovaVM backend does not support direct flag writes. Use BASIC-level commands instead.");
    }

    #endregion

    #region IExecutionBackend — Execution

    public ExecutionResult ExecuteJsr(int address, int stopOnAddress, bool stopOnRts, bool failOnBrk)
    {
        // NovaVM tests run BASIC programs, not JSR calls.
        // Assembly-level JSR execution would require pause/step/breakpoint support.
        throw new NotSupportedException(
            "NovaVM backend does not support JSR execution. Use basic()/run() for BASIC-level testing, " +
            "or use the 'sim' backend for assembly-level tests.");
    }

    public int GetCycles() => _cycleCount;

    public void ResetCycleCount() => _cycleCount = 0;

    #endregion

    #region IExecutionBackend — Symbols, Snapshots, Config

    public void LoadSymbols(string path)
    {
        // Symbols are only meaningful for the internal simulator
        Logger.Trace($"LoadSymbols ignored for NovaVM backend: {path}");
    }

    public void SaveSnapshot(string name)
    {
        // No snapshot support — test isolation via ColdStart()
        Logger.Trace($"SaveSnapshot ignored for NovaVM backend: {name}");
    }

    public void RestoreSnapshot(string name)
    {
        // No snapshot support — test isolation via ColdStart()
        Logger.Trace($"RestoreSnapshot ignored for NovaVM backend: {name}");
    }

    public void Reset()
    {
        ColdStart();
    }

    public void SetWarpMode(bool enabled)
    {
        // e6502 runs at full speed already
    }

    public bool TraceEnabled { get; set; }
    public void ClearTraceBuffer() { }
    public List<string> GetTraceBuffer() => new();

    #endregion

    #region IHighLevelBackend

    public void SendText(string text)
    {
        _connection.Send("type_text", new Dictionary<string, object>
        {
            { "text", text },
            { "delay_ms", 2 }
        });
    }

    public void SendKey(string key)
    {
        _connection.Send("send_key", new Dictionary<string, object>
        {
            { "key", key }
        });
    }

    public string[] ReadScreen()
    {
        var result = _connection.Send("read_screen");
        var lines = result.GetProperty("lines");
        var screenLines = new string[lines.GetArrayLength()];
        for (int i = 0; i < screenLines.Length; i++)
            screenLines[i] = lines[i].GetString() ?? "";
        return screenLines;
    }

    public string ReadLine(int row)
    {
        var result = _connection.Send("read_line", new Dictionary<string, object>
        {
            { "row", row }
        });
        return result.GetProperty("text").GetString() ?? "";
    }

    public (int x, int y) GetCursor()
    {
        var result = _connection.Send("get_cursor");
        return (result.GetProperty("x").GetInt32(), result.GetProperty("y").GetInt32());
    }

    public void WaitForText(string text, int timeoutMs = 5000)
    {
        var result = _connection.Send("wait_ready", new Dictionary<string, object>
        {
            { "text", text },
            { "timeout_ms", timeoutMs }
        });

        if (result.TryGetProperty("found", out var found) && !found.GetBoolean())
            throw new TimeoutException($"Timed out waiting for text '{text}' after {timeoutMs}ms");
    }

    public void ColdStart()
    {
        // Step 1: CTRL-C to break any running program, wait for Ready.
        _connection.Send("cold_start");
        WaitForText("Ready", _config.TimeoutMs);

        // Step 2: type "RESET" to invoke EhBASIC's RESET command
        // (LAB_RESET in basic.asm: writes VCMD_SYSRESET to VGC then
        // JMP ($FFFC) — a proper cold reboot that clears BASIC program
        // memory, variables, VGC state). CTRL-C alone leaves program
        // lines like "10 PRINT 42" resident between tests; RESET is
        // what makes per-test isolation real.
        _connection.Send("type_text", new Dictionary<string, object>
        {
            { "text", "RESET" },
            { "delay_ms", 2 }
        });
        _connection.Send("send_key", new Dictionary<string, object> { { "key", "ENTER" } });

        // Step 3: wait for Ready again — RESET re-runs the banner so
        // "Ready" will re-appear after a full cold-boot cycle.
        WaitForText("Ready", _config.TimeoutMs);
    }

    public void Pause()
    {
        _connection.Send("dbg_pause");
    }

    public void Resume()
    {
        _connection.Send("dbg_resume");
    }

    public void RunCycles(int count)
    {
        _connection.Send("run_cycles", new Dictionary<string, object>
        {
            { "cycles", count }
        });
    }

    public void WaitForMemory(int addr, byte value, int timeoutMs = 5000)
    {
        var result = _connection.Send("watch", new Dictionary<string, object>
        {
            { "address", addr },
            { "value", (int)value },
            { "timeout_ms", timeoutMs }
        });

        if (result.TryGetProperty("matched", out var matched) && !matched.GetBoolean())
        {
            var actual = result.TryGetProperty("actual", out var a) ? a.GetInt32() : -1;
            throw new TimeoutException(
                $"Timed out waiting for mem[${addr:X4}] == ${value:X2} (actual: ${actual:X2}) after {timeoutMs}ms");
        }
    }

    #endregion

    public void Dispose()
    {
        _connection.Dispose();
    }
}
