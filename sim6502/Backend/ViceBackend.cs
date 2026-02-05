using System.Text.Json;
using NLog;

namespace sim6502.Backend;

public class ViceBackend : IExecutionBackend
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    private readonly ViceConnection _connection;
    private readonly ViceBackendConfig _config;

    public ViceBackend(ViceBackendConfig config)
    {
        _config = config;
        _connection = new ViceConnection(config.Host, config.Port);
    }

    public void Connect()
    {
        Logger.Info($"Connecting to VICE MCP server at {_config.Host}:{_config.Port}...");

        if (!_connection.Ping())
        {
            throw new InvalidOperationException(
                $"Could not connect to VICE MCP server at {_config.Host}:{_config.Port}. " +
                "Is VICE running with -mcpserver?");
        }

        Logger.Info("Connected to VICE MCP server.");

        // Pause execution for setup
        _connection.CallTool("vice.execution.pause");

        // Enable warp mode if configured
        if (_config.WarpMode)
            SetWarpMode(true);
    }

    public void LoadBinary(byte[] data, int address)
    {
        var hexData = Convert.ToHexString(data);
        var args = new Dictionary<string, object>
        {
            { "address", address },
            { "data", hexData }
        };
        var result = _connection.CallTool("vice.memory.write", args);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to write memory at ${address:X4}: {result.ErrorMessage}");
    }

    public void WriteByte(int address, byte value)
    {
        var args = new Dictionary<string, object>
        {
            { "address", address },
            { "data", value.ToString("X2") }
        };
        _connection.CallTool("vice.memory.write", args);
    }

    public void WriteWord(int address, int value)
    {
        var lo = (byte)(value & 0xFF);
        var hi = (byte)((value >> 8) & 0xFF);
        WriteByte(address, lo);
        WriteByte(address + 1, hi);
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
        var args = new Dictionary<string, object>
        {
            { "address", address },
            { "length", 1 }
        };
        var result = _connection.CallTool("vice.memory.read", args);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to read memory at ${address:X4}: {result.ErrorMessage}");

        var content = JsonDocument.Parse(result.Content);
        var data = content.RootElement.GetProperty("data").GetString() ?? "";
        return Convert.ToByte(data[..2], 16);
    }

    public int ReadWord(int address)
    {
        var lo = ReadByte(address);
        var hi = ReadByte(address + 1);
        return hi * 256 + lo;
    }

    public int GetRegister(string name)
    {
        var result = _connection.CallTool("vice.registers.get");
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to get registers: {result.ErrorMessage}");

        var content = JsonDocument.Parse(result.Content);
        var regName = name.ToUpper();
        return content.RootElement.GetProperty(regName).GetInt32();
    }

    public void SetRegister(string name, int value)
    {
        var args = new Dictionary<string, object>
        {
            { "register", name.ToUpper() },
            { "value", value }
        };
        var result = _connection.CallTool("vice.registers.set", args);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to set register {name}: {result.ErrorMessage}");
    }

    public bool GetFlag(string name)
    {
        var result = _connection.CallTool("vice.registers.get");
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to get flags: {result.ErrorMessage}");

        var content = JsonDocument.Parse(result.Content);
        var flags = content.RootElement.GetProperty("P").GetInt32();

        return name.ToLower() switch
        {
            "c" => (flags & 0x01) != 0,
            "z" => (flags & 0x02) != 0,
            "d" => (flags & 0x08) != 0,
            "v" => (flags & 0x40) != 0,
            "n" => (flags & 0x80) != 0,
            _ => throw new ArgumentException($"Unknown flag: {name}")
        };
    }

    public void SetFlag(string name, bool value)
    {
        var result = _connection.CallTool("vice.registers.get");
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to get flags: {result.ErrorMessage}");

        var content = JsonDocument.Parse(result.Content);
        var flags = content.RootElement.GetProperty("P").GetInt32();

        var bit = name.ToLower() switch
        {
            "c" => 0x01,
            "z" => 0x02,
            "d" => 0x08,
            "v" => 0x40,
            "n" => 0x80,
            _ => throw new ArgumentException($"Unknown flag: {name}")
        };

        flags = value ? flags | bit : flags & ~bit;

        var args = new Dictionary<string, object>
        {
            { "register", "P" },
            { "value", flags }
        };
        _connection.CallTool("vice.registers.set", args);
    }

    public ExecutionResult ExecuteJsr(int address, int stopOnAddress, bool stopOnRts, bool failOnBrk)
    {
        Logger.Trace($"VICE ExecuteJsr: ${address:X4}, stopOnAddr=${stopOnAddress:X4}, stopOnRts={stopOnRts}, failOnBrk={failOnBrk}");

        // 1. Read current stack pointer
        var regs = _connection.CallTool("vice.registers.get");
        var regsDoc = JsonDocument.Parse(regs.Content);
        var sp = regsDoc.RootElement.GetProperty("SP").GetInt32();

        // 2. Push synthetic return address onto stack ($FFFF so RTS goes to $0000)
        var returnAddr = 0xFFFF;
        WriteByte(0x100 + sp, (byte)((returnAddr >> 8) & 0xFF));
        sp--;
        WriteByte(0x100 + sp, (byte)(returnAddr & 0xFF));
        sp--;

        // Update SP
        SetRegister("SP", sp);

        // 3. Set PC to target
        SetRegister("PC", address);

        // 4. Set breakpoints
        var breakpoints = new List<int>();

        if (stopOnRts)
        {
            var bpResult = _connection.CallTool("vice.breakpoints.set", new Dictionary<string, object>
            {
                { "address", 0x0000 }
            });
            if (bpResult.IsSuccess)
            {
                var bpDoc = JsonDocument.Parse(bpResult.Content);
                if (bpDoc.RootElement.TryGetProperty("id", out var bpId))
                    breakpoints.Add(bpId.GetInt32());
            }
        }

        if (stopOnAddress > 0)
        {
            var bpResult = _connection.CallTool("vice.breakpoints.set", new Dictionary<string, object>
            {
                { "address", stopOnAddress }
            });
            if (bpResult.IsSuccess)
            {
                var bpDoc = JsonDocument.Parse(bpResult.Content);
                if (bpDoc.RootElement.TryGetProperty("id", out var bpId))
                    breakpoints.Add(bpId.GetInt32());
            }
        }

        // 5. Run execution
        _connection.CallTool("vice.execution.run");

        // 6. Wait for execution to stop (poll)
        var startTime = DateTime.UtcNow;
        var stopped = false;
        var hitBrk = false;

        while (!stopped && (DateTime.UtcNow - startTime).TotalMilliseconds < _config.TimeoutMs)
        {
            Thread.Sleep(10);

            var stateResult = _connection.CallTool("vice.execution.get_state");
            if (!stateResult.IsSuccess) continue;

            var stateDoc = JsonDocument.Parse(stateResult.Content);
            if (stateDoc.RootElement.TryGetProperty("state", out var stateElem))
            {
                var state = stateElem.GetString();
                if (state == "PAUSED" || state == "BREAKPOINT")
                    stopped = true;
            }
        }

        if (!stopped)
        {
            _connection.CallTool("vice.execution.pause");
            Logger.Warn($"Execution timed out after {_config.TimeoutMs}ms");
        }

        // 7. Read final state
        var finalRegs = _connection.CallTool("vice.registers.get");
        var finalDoc = JsonDocument.Parse(finalRegs.Content);
        var finalPc = finalDoc.RootElement.GetProperty("PC").GetInt32();

        // Check if we stopped on BRK
        var memAtPc = ReadByte(finalPc);
        if (memAtPc == 0x00 && failOnBrk)
            hitBrk = true;

        // 8. Clean up breakpoints
        foreach (var bpId in breakpoints)
        {
            _connection.CallTool("vice.breakpoints.delete", new Dictionary<string, object>
            {
                { "id", bpId }
            });
        }

        // 9. Get cycles
        var cycleResult = _connection.CallTool("vice.trace.cycles.get");
        long cycles = 0;
        if (cycleResult.IsSuccess)
        {
            var cycleDoc = JsonDocument.Parse(cycleResult.Content);
            if (cycleDoc.RootElement.TryGetProperty("cycles", out var cycleElem))
                cycles = cycleElem.GetInt64();
        }

        var reason = !stopped ? StopReason.Timeout :
                     hitBrk ? StopReason.Brk :
                     (stopOnAddress > 0 && finalPc == stopOnAddress) ? StopReason.StopAddress :
                     StopReason.Rts;

        return new ExecutionResult
        {
            ExitedCleanly = !hitBrk && stopped,
            Reason = reason,
            CyclesElapsed = cycles,
            ProgramCounter = finalPc
        };
    }

    public int GetCycles()
    {
        var result = _connection.CallTool("vice.trace.cycles.get");
        if (!result.IsSuccess) return 0;

        var doc = JsonDocument.Parse(result.Content);
        if (doc.RootElement.TryGetProperty("cycles", out var cycleElem))
            return (int)cycleElem.GetInt64();
        return 0;
    }

    public void ResetCycleCount()
    {
        _connection.CallTool("vice.trace.cycles.reset");
    }

    public void LoadSymbols(string path)
    {
        Logger.Info($"Loading symbols into VICE: {path}");
    }

    public void SaveSnapshot(string name)
    {
        var args = new Dictionary<string, object> { { "name", name } };
        var result = _connection.CallTool("vice.snapshot.save", args);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to save snapshot '{name}': {result.ErrorMessage}");
        Logger.Info($"Saved VICE snapshot: {name}");
    }

    public void RestoreSnapshot(string name)
    {
        var args = new Dictionary<string, object> { { "name", name } };
        var result = _connection.CallTool("vice.snapshot.load", args);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to load snapshot '{name}': {result.ErrorMessage}");
        Logger.Trace($"Restored VICE snapshot: {name}");
    }

    public void Reset()
    {
        _connection.CallTool("vice.execution.reset", new Dictionary<string, object>
        {
            { "mode", "soft" }
        });
    }

    public void SetWarpMode(bool enabled)
    {
        _connection.CallTool("vice.config.set", new Dictionary<string, object>
        {
            { "resource", "WarpMode" },
            { "value", enabled ? 1 : 0 }
        });
        Logger.Info($"VICE warp mode: {(enabled ? "enabled" : "disabled")}");
    }

    public bool TraceEnabled { get; set; }
    public void ClearTraceBuffer() { }
    public List<string> GetTraceBuffer() => new();

    public void Dispose()
    {
        if (_config.WarpMode)
        {
            try { SetWarpMode(false); } catch { /* best effort */ }
        }
        _connection.Dispose();
    }
}
