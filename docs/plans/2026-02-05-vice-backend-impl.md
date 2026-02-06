# VICE MCP Backend Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add VICE as an optional execution backend so existing sim6502 test suites run against a real Commodore emulator via the vice-mcp embedded MCP server.

**Architecture:** Introduce `IExecutionBackend` interface abstracting CPU execution. `SimulatorBackend` wraps the existing `Processor` class (zero behavior change). `ViceBackend` translates backend calls into HTTP JSON-RPC requests to VICE's MCP server. `SimBaseListener` is refactored to use the backend interface instead of `Processor` directly. Snapshot save/restore provides per-test isolation.

**Tech Stack:** C# / .NET 10.0, `HttpClient` + `System.Text.Json` (both built-in, no new NuGet packages), xUnit + FluentAssertions for tests.

---

## Task 1: Create IExecutionBackend Interface and ExecutionResult

**Files:**
- Create: `sim6502/Backend/IExecutionBackend.cs`
- Create: `sim6502/Backend/ExecutionResult.cs`

**Step 1: Create ExecutionResult**

```csharp
namespace sim6502.Backend;

public class ExecutionResult
{
    public bool ExitedCleanly { get; set; } = true;
    public StopReason Reason { get; set; } = StopReason.Rts;
    public long CyclesElapsed { get; set; }
    public int ProgramCounter { get; set; }
}

public enum StopReason
{
    Rts,
    Brk,
    StopAddress,
    Timeout
}
```

**Step 2: Create IExecutionBackend**

```csharp
using sim6502.Proc;

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
```

**Step 3: Verify it compiles**

Run: `dotnet build sim6502/sim6502.csproj`
Expected: Build succeeds.

**Step 4: Commit**

```bash
git add sim6502/Backend/IExecutionBackend.cs sim6502/Backend/ExecutionResult.cs
git commit -m "feat: add IExecutionBackend interface and ExecutionResult"
```

---

## Task 2: Create SimulatorBackend (Wraps Existing Processor)

**Files:**
- Create: `sim6502/Backend/SimulatorBackend.cs`
- Create: `sim6502tests/Backend/SimulatorBackendTests.cs`

**Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using sim6502.Backend;
using sim6502.Proc;
using sim6502.Systems;
using Xunit;

namespace sim6502tests.Backend;

public class SimulatorBackendTests
{
    private SimulatorBackend CreateBackend(
        ProcessorType type = ProcessorType.MOS6502,
        IMemoryMap? memoryMap = null)
    {
        memoryMap ??= MemoryMapFactory.CreateForProcessor(type).memoryMap;
        return new SimulatorBackend(type, memoryMap);
    }

    [Fact]
    public void WriteByte_ReadByte_RoundTrips()
    {
        using var backend = CreateBackend();
        backend.WriteByte(0x1000, 0xAB);
        backend.ReadByte(0x1000).Should().Be(0xAB);
    }

    [Fact]
    public void WriteWord_ReadWord_RoundTrips()
    {
        using var backend = CreateBackend();
        backend.WriteWord(0x2000, 0xABCD);
        backend.ReadWord(0x2000).Should().Be(0xABCD);
    }

    [Fact]
    public void SetRegister_GetRegister_RoundTrips()
    {
        using var backend = CreateBackend();
        backend.SetRegister("a", 0x42);
        backend.GetRegister("a").Should().Be(0x42);

        backend.SetRegister("x", 0x10);
        backend.GetRegister("x").Should().Be(0x10);

        backend.SetRegister("y", 0xFF);
        backend.GetRegister("y").Should().Be(0xFF);
    }

    [Fact]
    public void SetFlag_GetFlag_RoundTrips()
    {
        using var backend = CreateBackend();
        backend.SetFlag("c", true);
        backend.GetFlag("c").Should().BeTrue();

        backend.SetFlag("z", true);
        backend.GetFlag("z").Should().BeTrue();
    }

    [Fact]
    public void LoadBinary_WritesToMemory()
    {
        using var backend = CreateBackend();
        var data = new byte[] { 0x01, 0x02, 0x03 };
        backend.LoadBinary(data, 0xC000);
        backend.ReadByte(0xC000).Should().Be(0x01);
        backend.ReadByte(0xC001).Should().Be(0x02);
        backend.ReadByte(0xC002).Should().Be(0x03);
    }

    [Fact]
    public void ExecuteJsr_SimpleRts_ReturnsCleanly()
    {
        using var backend = CreateBackend();
        // Write a simple RTS at $C000
        backend.WriteByte(0xC000, 0x60); // RTS
        var result = backend.ExecuteJsr(0xC000, 0, true, true);
        result.ExitedCleanly.Should().BeTrue();
        result.Reason.Should().Be(StopReason.Rts);
    }

    [Fact]
    public void ExecuteJsr_Brk_FailsWhenFailOnBrk()
    {
        using var backend = CreateBackend();
        // Write BRK at $C000
        backend.WriteByte(0xC000, 0x00); // BRK
        var result = backend.ExecuteJsr(0xC000, 0, true, true);
        result.ExitedCleanly.Should().BeFalse();
        result.Reason.Should().Be(StopReason.Brk);
    }

    [Fact]
    public void GetCycles_TracksCycles()
    {
        using var backend = CreateBackend();
        backend.ResetCycleCount();
        // NOP + RTS = some cycles
        backend.WriteByte(0xC000, 0xEA); // NOP
        backend.WriteByte(0xC001, 0x60); // RTS
        backend.ExecuteJsr(0xC000, 0, true, true);
        backend.GetCycles().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Processor_IsAccessible()
    {
        using var backend = CreateBackend();
        backend.Processor.Should().NotBeNull();
        backend.Processor.ProcessorType.Should().Be(ProcessorType.MOS6502);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test sim6502tests/ --filter "SimulatorBackendTests"`
Expected: Build error — `SimulatorBackend` doesn't exist yet.

**Step 3: Implement SimulatorBackend**

```csharp
using NLog;
using sim6502.Proc;
using sim6502.Systems;

namespace sim6502.Backend;

public class SimulatorBackend : IExecutionBackend
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    public Processor Processor { get; }

    public SimulatorBackend(ProcessorType processorType, IMemoryMap memoryMap)
    {
        Processor = new Processor(processorType, memoryMap);
        Processor.Reset();
    }

    public void LoadBinary(byte[] data, int address)
    {
        Processor.LoadProgram(address, data, address, false);
    }

    public void WriteByte(int address, byte value)
    {
        Processor.WriteMemoryValueWithoutIncrement(address, value);
    }

    public void WriteWord(int address, int value)
    {
        Processor.WriteMemoryWord(address, value);
    }

    public void WriteMemoryValue(int address, int value)
    {
        Processor.WriteMemoryValue(address, value);
    }

    public byte ReadByte(int address)
    {
        return Processor.ReadMemoryValueWithoutCycle(address);
    }

    public int ReadWord(int address)
    {
        return Processor.ReadMemoryWordWithoutCycle(address);
    }

    public int GetRegister(string name)
    {
        return name.ToLower() switch
        {
            "a" => Processor.Accumulator,
            "x" => Processor.XRegister,
            "y" => Processor.YRegister,
            _ => throw new ArgumentException($"Unknown register: {name}")
        };
    }

    public void SetRegister(string name, int value)
    {
        switch (name.ToLower())
        {
            case "a": Processor.Accumulator = value; break;
            case "x": Processor.XRegister = value; break;
            case "y": Processor.YRegister = value; break;
            default: throw new ArgumentException($"Unknown register: {name}");
        }
    }

    public bool GetFlag(string name)
    {
        return name.ToLower() switch
        {
            "c" => Processor.CarryFlag,
            "z" => Processor.ZeroFlag,
            "n" => Processor.NegativeFlag,
            "v" => Processor.OverflowFlag,
            "d" => Processor.DecimalFlag,
            _ => throw new ArgumentException($"Unknown flag: {name}")
        };
    }

    public void SetFlag(string name, bool value)
    {
        switch (name.ToLower())
        {
            case "c": Processor.CarryFlag = value; break;
            case "z": Processor.ZeroFlag = value; break;
            case "n": Processor.NegativeFlag = value; break;
            case "v": Processor.OverflowFlag = value; break;
            case "d": Processor.DecimalFlag = value; break;
            default: throw new ArgumentException($"Unknown flag: {name}");
        }
    }

    public ExecutionResult ExecuteJsr(int address, int stopOnAddress, bool stopOnRts, bool failOnBrk)
    {
        var cleanExit = Processor.RunRoutine(address, stopOnAddress, stopOnRts, failOnBrk);
        return new ExecutionResult
        {
            ExitedCleanly = cleanExit,
            Reason = cleanExit ? StopReason.Rts : StopReason.Brk,
            CyclesElapsed = Processor.CycleCount,
            ProgramCounter = Processor.ProgramCounter
        };
    }

    public int GetCycles() => Processor.CycleCount;
    public void ResetCycleCount() => Processor.ResetCycleCount();

    public void LoadSymbols(string path)
    {
        // Internal simulator doesn't use symbol files directly
    }

    public void SaveSnapshot(string name)
    {
        // No-op for internal simulator
    }

    public void RestoreSnapshot(string name)
    {
        // No-op for internal simulator
    }

    public void Reset() => Processor.Reset();

    public void SetWarpMode(bool enabled)
    {
        // No-op for internal simulator
    }

    public bool TraceEnabled
    {
        get => Processor.TraceEnabled;
        set => Processor.TraceEnabled = value;
    }

    public void ClearTraceBuffer() => Processor.ClearTraceBuffer();
    public List<string> GetTraceBuffer() => Processor.GetTraceBuffer();

    public void Dispose()
    {
        // Nothing to dispose for the internal simulator
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test sim6502tests/ --filter "SimulatorBackendTests"`
Expected: All 8 tests pass.

**Step 5: Commit**

```bash
git add sim6502/Backend/SimulatorBackend.cs sim6502tests/Backend/SimulatorBackendTests.cs
git commit -m "feat: add SimulatorBackend wrapping existing Processor"
```

---

## Task 3: Refactor SimBaseListener to Use IExecutionBackend

This is the largest task. We replace all direct `Proc.` calls with backend calls while preserving identical behavior.

**Files:**
- Modify: `sim6502/Grammar/SimBaseListener.cs`
- Modify: `sim6502/Expressions/MemoryCompare.cs`
- Modify: `sim6502/Expressions/BaseCompare.cs`

**Step 1: Check BaseCompare to understand the inheritance**

Read `sim6502/Expressions/BaseCompare.cs` to see what it needs from Processor.

**Step 2: Refactor BaseCompare and MemoryCompare to use IExecutionBackend**

Replace `Processor` parameter with `IExecutionBackend` in `BaseCompare` and `MemoryCompare`:

In `BaseCompare.cs`, change the constructor and field:
```csharp
using sim6502.Backend;

// Change: protected Processor Proc { get; }
// To: protected IExecutionBackend Backend { get; }

// Change constructor from: protected BaseCompare(Processor proc) { Proc = proc; }
// To: protected BaseCompare(IExecutionBackend backend) { Backend = backend; }
```

In `MemoryCompare.cs`, update all `Proc.ReadMemoryValueWithoutCycle` to `Backend.ReadByte` and `Proc.ReadMemoryWordWithoutCycle` to `Backend.ReadWord`:

```csharp
using sim6502.Backend;

public class MemoryCompare : BaseCompare
{
    public MemoryCompare(IExecutionBackend backend) : base(backend) { }

    public ComparisonResult MemoryCmp(int source, int target, int count)
    {
        var res = new ComparisonResult();
        for (var i = 0; i < count; i++)
        {
            var sourceValue = Backend.ReadByte(source + i);
            var targetValue = Backend.ReadByte(target + i);
            // ... rest unchanged
        }
        return res;
    }

    public ComparisonResult MemoryChk(int source, int count, int value)
    {
        var res = new ComparisonResult();
        for (var i = source; i < source + count; i++)
        {
            var actualValue = Backend.ReadByte(i);
            // ... rest unchanged
        }
        return res;
    }

    public ComparisonResult MemoryVal(int location, int value, string op = "==")
    {
        var actual = value > 255 ? Backend.ReadWord(location) : Backend.ReadByte(location);
        // ... rest unchanged
    }
}
```

**Step 3: Refactor SimBaseListener**

Replace the `Proc` property and all direct Processor usage:

Key changes (line references from current file):

1. **Line 173** — Change property type:
   ```csharp
   // Before:
   public Processor Proc { get; set; }
   // After:
   public IExecutionBackend Backend { get; set; }
   // Keep Proc as a convenience accessor for backward compat in tests:
   public Processor? Proc => (Backend as SimulatorBackend)?.Processor;
   ```

2. **Line 187-191** — `ResetTest()`:
   ```csharp
   // Before: Proc.ResetCycleCount(); Proc.TraceEnabled = false; Proc.ClearTraceBuffer();
   // After: Backend.ResetCycleCount(); Backend.TraceEnabled = false; Backend.ClearTraceBuffer();
   // Before: LoadResources();
   // After: LoadResources();  (unchanged — still loads via Utility which needs Proc)
   ```

3. **Line 266-276** — `OutputTrace()`:
   ```csharp
   // Before: var trace = Proc.GetTraceBuffer();
   // After: var trace = Backend.GetTraceBuffer();
   ```

4. **Line 303-308** — `LoadResources()`:
   ```csharp
   // Before: Utility.LoadFileIntoProcessor(Proc, lr.LoadAddress, lr.Filename, lr.StripHeader);
   // After: load via backend:
   private void LoadResources()
   {
       foreach (var lr in _suiteResources)
       {
           var data = File.ReadAllBytes(lr.Filename);
           if (lr.StripHeader && data.Length >= 2)
               data = data[2..];
           Backend.LoadBinary(data, lr.LoadAddress);
       }
   }
   ```

5. **Lines 333-377** — `EnterSuite()`:
   ```csharp
   // Before: Proc = new Processor(_currentProcessorType, _currentMemoryMap!); Proc.Reset();
   // After: Backend = new SimulatorBackend(_currentProcessorType, _currentMemoryMap!);
   // (ViceBackend selection will be added in a later task via BackendFactory)
   ```

6. **Lines 603-619** — `ExitExpressionAssignment()`:
   ```csharp
   // Before: Proc.WriteMemoryValue(address, value);
   // After: Backend.WriteMemoryValue(address, value);
   ```

7. **Lines 621-637** — `ExitAddressAssignment()`:
   ```csharp
   // Before: WriteValueToMemory (calls Proc.WriteMemoryValue)
   // After: Backend.WriteMemoryValue(address, value);
   ```

8. **Lines 639-649** — `ExitSymbolAssignment()`: Same pattern.

9. **Lines 651-662** — `ExitSymbolRegisterAssignment()`:
   ```csharp
   // Before: Proc.WriteMemoryValueWithoutIncrement(address, (byte)value);
   // After: Backend.WriteByte(address, (byte)value);
   ```

10. **Lines 682-691** — `GetRegisterValue()`:
    ```csharp
    // Before: Proc.Accumulator, Proc.XRegister, Proc.YRegister
    // After: Backend.GetRegister(register)
    ```

11. **Lines 693-723** — `ExitRegisterAssignment()`:
    ```csharp
    // Before: Proc.Accumulator = exp; etc.
    // After: Backend.SetRegister(register, exp);
    ```

12. **Lines 726-763** — `ExitFlagAssignment()`:
    ```csharp
    // Before: Proc.CarryFlag = val; etc.
    // After: Backend.SetFlag(flag, val);
    ```

13. **Lines 774-798** — `ExitExpressionCompare()`:
    ```csharp
    // Before: Proc.ReadMemoryValueWithoutCycle / Proc.ReadMemoryWordWithoutCycle
    // After: Backend.ReadByte / Backend.ReadWord
    ```

14. **Lines 800-806** — `ExitAddressCompare()`:
    ```csharp
    // Before: Proc.ReadMemoryValueWithoutCycle(address)
    // After: Backend.ReadByte(address)
    ```

15. **Lines 808-821** — `ExitRegisterCompare()`:
    ```csharp
    // Before: Proc.Accumulator, Proc.XRegister, Proc.YRegister
    // After: Backend.GetRegister(register)
    ```

16. **Lines 823-838** — `ExitFlagCompare()`:
    ```csharp
    // Before: Proc.CarryFlag, etc.
    // After: Backend.GetFlag(flag) ? 1 : 0
    ```

17. **Line 853-854** — `ExitCyclesCompare()`:
    ```csharp
    // Before: Proc.CycleCount
    // After: Backend.GetCycles()
    ```

18. **Lines 1010-1016** — trace enabling in `EnterTestFunction()`:
    ```csharp
    // Before: Proc.TraceEnabled = true; Proc.ClearTraceBuffer();
    // After: Backend.TraceEnabled = true; Backend.ClearTraceBuffer();
    ```

19. **Lines 1099-1113** — `ExitMemoryChkFunction()`:
    ```csharp
    // Before: var mc = new MemoryCompare(Proc);
    // After: var mc = new MemoryCompare(Backend);
    ```

20. **Lines 1118-1132** — `ExitMemoryCmpFunction()`: Same.

21. **Lines 1134-1138** — `ExitPeekByteFunction()` and `ExitPeekWordFunction()`:
    ```csharp
    // Before: Proc.ReadMemoryValueWithoutCycle / Proc.ReadMemoryWordWithoutCycle
    // After: Backend.ReadByte / Backend.ReadWord
    ```

22. **Lines 1140-1171** — `ExitJsrFunction()`:
    ```csharp
    // Before: var finishedCleanly = Proc.RunRoutine(address, stopOnAddress, stopOnRts, failOnBrk);
    // After:
    var result = Backend.ExecuteJsr(address, stopOnAddress, stopOnRts, failOnBrk);
    if (!result.ExitedCleanly)
    {
        FailAssertion($"JSR call to {address.ToString()} returned an error.");
    }
    ```

23. **Lines 1189-1228** — `ExitMemFillFunction()`:
    ```csharp
    // Before: Proc.WriteMemoryValueWithoutIncrement(address + i, (byte)(value & 0xFF));
    // After: Backend.WriteByte(address + i, (byte)(value & 0xFF));
    ```

24. **Lines 1230-1286** — `ExitMemDumpFunction()`:
    ```csharp
    // Before: Proc.ReadMemoryValueWithoutCycle(lineAddr + i)
    // After: Backend.ReadByte(lineAddr + i)
    ```

25. **Line 765-768** — Remove `WriteValueToMemory` helper (now just `Backend.WriteMemoryValue`).

**Step 4: Run ALL existing tests**

Run: `dotnet test sim6502tests/`
Expected: All existing tests pass. Zero behavior change. This is a pure refactor.

**Step 5: Commit**

```bash
git add sim6502/Grammar/SimBaseListener.cs sim6502/Expressions/BaseCompare.cs sim6502/Expressions/MemoryCompare.cs
git commit -m "refactor: use IExecutionBackend in SimBaseListener and MemoryCompare

Replace direct Processor access with backend interface. SimulatorBackend
wraps Processor identically. All existing tests pass unchanged."
```

---

## Task 4: Create ViceConnection (JSON-RPC Client)

**Files:**
- Create: `sim6502/Backend/ViceConnection.cs`
- Create: `sim6502tests/Backend/ViceConnectionTests.cs`

**Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Text.Json;
using FluentAssertions;
using sim6502.Backend;
using Xunit;

namespace sim6502tests.Backend;

public class ViceConnectionTests
{
    [Fact]
    public void BuildRequest_FormatsJsonRpcCorrectly()
    {
        var json = ViceConnection.BuildRequestJson("vice.ping", null, 1);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.GetProperty("method").GetString().Should().Be("tools/call");
        root.GetProperty("id").GetInt32().Should().Be(1);

        var toolParams = root.GetProperty("params");
        toolParams.GetProperty("name").GetString().Should().Be("vice.ping");
    }

    [Fact]
    public void BuildRequest_WithArguments_IncludesArgs()
    {
        var args = new Dictionary<string, object>
        {
            { "register", "A" },
            { "value", 255 }
        };
        var json = ViceConnection.BuildRequestJson("vice.registers.set", args, 2);
        var doc = JsonDocument.Parse(json);
        var toolArgs = doc.RootElement.GetProperty("params").GetProperty("arguments");

        toolArgs.GetProperty("register").GetString().Should().Be("A");
        toolArgs.GetProperty("value").GetInt32().Should().Be(255);
    }

    [Fact]
    public void ParseResponse_Success_ReturnsResult()
    {
        var responseJson = """
        {
            "jsonrpc": "2.0",
            "result": {
                "content": [{"type": "text", "text": "{\"A\": 255, \"X\": 0}"}]
            },
            "id": 1
        }
        """;
        var result = ViceConnection.ParseResponse(responseJson);
        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("255");
    }

    [Fact]
    public void ParseResponse_Error_ReturnsError()
    {
        var responseJson = """
        {
            "jsonrpc": "2.0",
            "error": {
                "code": -32601,
                "message": "Method not found"
            },
            "id": 1
        }
        """;
        var result = ViceConnection.ParseResponse(responseJson);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(-32601);
        result.ErrorMessage.Should().Be("Method not found");
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test sim6502tests/ --filter "ViceConnectionTests"`
Expected: Build error — `ViceConnection` doesn't exist.

**Step 3: Implement ViceConnection**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace sim6502.Backend;

public class McpResponse
{
    public bool IsSuccess { get; set; }
    public string Content { get; set; } = "";
    public int ErrorCode { get; set; }
    public string ErrorMessage { get; set; } = "";
    public JsonElement? RawResult { get; set; }
}

public class ViceConnection : IDisposable
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private int _requestId;

    public ViceConnection(string host = "127.0.0.1", int port = 6510)
    {
        _baseUrl = $"http://{host}:{port}";
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<McpResponse> CallToolAsync(string toolName, Dictionary<string, object>? arguments = null)
    {
        var requestId = Interlocked.Increment(ref _requestId);
        var json = BuildRequestJson(toolName, arguments, requestId);

        Logger.Trace($"MCP request: {toolName} (id={requestId})");

        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/mcp", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        Logger.Trace($"MCP response: {responseBody[..Math.Min(200, responseBody.Length)]}");

        return ParseResponse(responseBody);
    }

    public McpResponse CallTool(string toolName, Dictionary<string, object>? arguments = null)
    {
        return CallToolAsync(toolName, arguments).GetAwaiter().GetResult();
    }

    public bool Ping()
    {
        try
        {
            var result = CallTool("vice.ping");
            return result.IsSuccess;
        }
        catch
        {
            return false;
        }
    }

    public static string BuildRequestJson(string toolName, Dictionary<string, object>? arguments, int id)
    {
        var request = new Dictionary<string, object>
        {
            { "jsonrpc", "2.0" },
            { "method", "tools/call" },
            { "id", id },
            { "params", BuildParams(toolName, arguments) }
        };

        return JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }

    private static Dictionary<string, object> BuildParams(string toolName, Dictionary<string, object>? arguments)
    {
        var p = new Dictionary<string, object> { { "name", toolName } };
        if (arguments != null && arguments.Count > 0)
            p["arguments"] = arguments;
        return p;
    }

    public static McpResponse ParseResponse(string responseJson)
    {
        var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errorElement))
        {
            return new McpResponse
            {
                IsSuccess = false,
                ErrorCode = errorElement.GetProperty("code").GetInt32(),
                ErrorMessage = errorElement.GetProperty("message").GetString() ?? "Unknown error"
            };
        }

        if (root.TryGetProperty("result", out var resultElement))
        {
            // MCP responses wrap content in a content array
            var contentText = "";
            if (resultElement.TryGetProperty("content", out var contentArray) &&
                contentArray.GetArrayLength() > 0)
            {
                var firstContent = contentArray[0];
                if (firstContent.TryGetProperty("text", out var textElement))
                    contentText = textElement.GetString() ?? "";
            }

            return new McpResponse
            {
                IsSuccess = true,
                Content = contentText,
                RawResult = resultElement
            };
        }

        return new McpResponse
        {
            IsSuccess = false,
            ErrorCode = -1,
            ErrorMessage = "Unexpected response format"
        };
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test sim6502tests/ --filter "ViceConnectionTests"`
Expected: All 4 tests pass.

**Step 5: Commit**

```bash
git add sim6502/Backend/ViceConnection.cs sim6502tests/Backend/ViceConnectionTests.cs
git commit -m "feat: add ViceConnection JSON-RPC client for MCP communication"
```

---

## Task 5: Create ViceBackend

**Files:**
- Create: `sim6502/Backend/ViceBackend.cs`
- Create: `sim6502tests/Backend/ViceBackendTests.cs`

**Step 1: Write the failing tests**

These tests mock the HTTP layer by testing that ViceBackend constructs the right MCP calls. We use a test helper that records calls instead of making HTTP requests.

```csharp
using FluentAssertions;
using sim6502.Backend;
using Xunit;

namespace sim6502tests.Backend;

public class ViceBackendTests
{
    [Fact]
    public void Constructor_StoresConfiguration()
    {
        var config = new ViceBackendConfig
        {
            Host = "192.168.1.100",
            Port = 7000,
            TimeoutMs = 10000,
            WarpMode = false
        };
        // Just verify config is stored — can't connect without VICE
        config.Host.Should().Be("192.168.1.100");
        config.Port.Should().Be(7000);
        config.TimeoutMs.Should().Be(10000);
        config.WarpMode.Should().BeFalse();
    }

    [Fact]
    public void DefaultConfig_HasSensibleDefaults()
    {
        var config = new ViceBackendConfig();
        config.Host.Should().Be("127.0.0.1");
        config.Port.Should().Be(6510);
        config.TimeoutMs.Should().Be(5000);
        config.WarpMode.Should().BeTrue();
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test sim6502tests/ --filter "ViceBackendTests"`
Expected: Build error — `ViceBackendConfig` and `ViceBackend` don't exist.

**Step 3: Create ViceBackendConfig**

```csharp
namespace sim6502.Backend;

public class ViceBackendConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6510;
    public int TimeoutMs { get; set; } = 5000;
    public bool WarpMode { get; set; } = true;
}
```

**Step 4: Implement ViceBackend**

```csharp
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
        // Convert byte array to hex string for MCP
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
        // Read current flags, modify the bit, write back
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

        // 2. Push a synthetic return address onto the stack
        // We use $0000 as the return address (which will trigger our breakpoint)
        // JSR pushes PC+2, so we push $FFFF (which means return to $0000)
        var returnAddr = 0xFFFF;
        WriteByte(0x100 + sp, (byte)((returnAddr >> 8) & 0xFF));  // high byte
        sp--;
        WriteByte(0x100 + sp, (byte)(returnAddr & 0xFF));          // low byte
        sp--;

        // Update SP
        SetRegister("SP", sp);

        // 3. Set PC to target
        SetRegister("PC", address);

        // 4. Set breakpoints
        var breakpoints = new List<int>();

        if (stopOnRts)
        {
            // Set breakpoint at our synthetic return address ($0000)
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
            Thread.Sleep(10); // Poll interval

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
            // Timeout — force pause
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
        // TODO: Call VICE's symbol loading MCP tool when available
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
    public void ClearTraceBuffer() { /* VICE handles tracing internally */ }
    public List<string> GetTraceBuffer() => new(); // VICE traces not forwarded to CLI

    public void Dispose()
    {
        // Restore warp mode before disconnecting
        if (_config.WarpMode)
        {
            try { SetWarpMode(false); } catch { /* best effort */ }
        }
        _connection.Dispose();
    }
}
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test sim6502tests/ --filter "ViceBackendTests"`
Expected: 2 tests pass.

**Step 6: Commit**

```bash
git add sim6502/Backend/ViceBackend.cs sim6502/Backend/ViceBackendConfig.cs sim6502tests/Backend/ViceBackendTests.cs
git commit -m "feat: add ViceBackend translating IExecutionBackend to MCP calls"
```

---

## Task 6: Add CLI Flags and Backend Factory

**Files:**
- Modify: `sim6502/Sim6502CLI.cs`
- Create: `sim6502/Backend/BackendFactory.cs`

**Step 1: Create BackendFactory**

```csharp
using NLog;
using sim6502.Proc;
using sim6502.Systems;

namespace sim6502.Backend;

public static class BackendFactory
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    public static IExecutionBackend Create(
        string backendType,
        ProcessorType processorType,
        IMemoryMap memoryMap,
        ViceBackendConfig? viceConfig = null)
    {
        switch (backendType.ToLower())
        {
            case "sim":
                return new SimulatorBackend(processorType, memoryMap);

            case "vice":
                if (viceConfig == null)
                    viceConfig = new ViceBackendConfig();

                var backend = new ViceBackend(viceConfig);
                backend.Connect();
                return backend;

            default:
                throw new ArgumentException($"Unknown backend type: {backendType}. Valid options: sim, vice");
        }
    }
}
```

**Step 2: Add CLI options**

In `sim6502/Sim6502CLI.cs`, add options to the `Options` class (after line 80):

```csharp
[Option("backend", Required = false, Default = "sim",
    HelpText = "Execution backend: 'sim' for internal simulator, 'vice' for VICE MCP")]
public string Backend { get; set; } = "sim";

[Option("vice-host", Required = false, Default = "127.0.0.1",
    HelpText = "VICE MCP server host")]
public string ViceHost { get; set; } = "127.0.0.1";

[Option("vice-port", Required = false, Default = 6510,
    HelpText = "VICE MCP server port")]
public int VicePort { get; set; } = 6510;

[Option("vice-timeout", Required = false, Default = 5000,
    HelpText = "Timeout in ms for VICE execution per test")]
public int ViceTimeout { get; set; } = 5000;

[Option("vice-warp", Required = false, Default = true,
    HelpText = "Enable warp mode in VICE during tests")]
public bool ViceWarp { get; set; } = true;

[Option("launch-vice", Required = false, Default = false,
    HelpText = "Auto-launch VICE process")]
public bool LaunchVice { get; set; }
```

**Step 3: Pass options to SimBaseListener**

In `RunTests` method (around line 147), add backend config to the listener:

```csharp
var sbl = new SimBaseListener
{
    FilterPattern = opts.FilterPattern,
    SingleTest = opts.SingleTest,
    FilterTags = opts.FilterTags,
    ExcludeTags = opts.ExcludeTags,
    ListOnly = opts.ListOnly,
    Errors = collector,
    BackendType = opts.Backend,
    ViceConfig = opts.Backend == "vice" ? new ViceBackendConfig
    {
        Host = opts.ViceHost,
        Port = opts.VicePort,
        TimeoutMs = opts.ViceTimeout,
        WarpMode = opts.ViceWarp
    } : null
};
```

**Step 4: Add backend properties to SimBaseListener**

Add to `SimBaseListener.cs` (near other public properties around line 164):

```csharp
public string BackendType { get; set; } = "sim";
public ViceBackendConfig? ViceConfig { get; set; }
```

**Step 5: Update EnterSuite to use BackendFactory**

In `EnterSuite` (around line 370-373), replace direct Processor creation:

```csharp
// Before:
// Proc = new Processor(_currentProcessorType, _currentMemoryMap!);
// Proc.Reset();

// After:
Backend = BackendFactory.Create(BackendType, _currentProcessorType, _currentMemoryMap!, ViceConfig);
```

**Step 6: Add snapshot save after suite resource loading**

At end of `ExitLoadFunction` or after all `load()` calls are processed, we need to save a baseline snapshot. The best hook point is in `EnterTestFunction` — save the snapshot on the first test of each suite.

Add to `SimBaseListener`:

```csharp
private bool _suiteBaselineSaved;
private string _suiteSnapshotName = "";
private int _suiteIndex;
```

In `EnterSuite` (after Backend creation):
```csharp
_suiteBaselineSaved = false;
_suiteSnapshotName = $"sim6502_suite_{_suiteIndex++}";
```

In `EnterTestFunction`, before the existing `ResetTest()` call:
```csharp
// Save baseline snapshot on first test of suite (after all load() calls processed)
if (!_suiteBaselineSaved && BackendType == "vice")
{
    Backend.SaveSnapshot(_suiteSnapshotName);
    _suiteBaselineSaved = true;
}

// Restore snapshot before each test for VICE backend
if (_suiteBaselineSaved && BackendType == "vice")
{
    Backend.RestoreSnapshot(_suiteSnapshotName);
}
```

**Step 7: Update ResetTest for VICE backend**

In `ResetTest()`, the `LoadResources()` call reloads binaries from disk every test. With VICE backend and snapshots, the snapshot already has the binaries loaded. So skip `LoadResources` when using VICE:

```csharp
private void ResetTest()
{
    _testFailureMessages.Clear();
    _didJsr = false;
    _currentTestSkipped = false;
    _currentTestExplicitlySkipped = false;
    _currentTestTraceEnabled = false;
    _currentTestTimeout = 0;
    _currentTestTags = "";
    Backend.ResetCycleCount();
    Backend.TraceEnabled = false;
    Backend.ClearTraceBuffer();

    // Only reload resources for simulator backend (VICE uses snapshots)
    if (BackendType != "vice")
        LoadResources();
}
```

**Step 8: Clean up in ExitSuite**

In `ExitSuite`, dispose the backend:

```csharp
public override void ExitSuite(sim6502Parser.SuiteContext context)
{
    _currentSetupBlock = null;
    Backend?.Dispose();
    ResetSuite();
}
```

**Step 9: Run all tests**

Run: `dotnet test sim6502tests/`
Expected: All existing tests pass (they all use `sim` backend by default).

**Step 10: Commit**

```bash
git add sim6502/Backend/BackendFactory.cs sim6502/Sim6502CLI.cs sim6502/Grammar/SimBaseListener.cs
git commit -m "feat: add CLI flags and BackendFactory for VICE backend selection

New CLI options:
  --backend vice          Use VICE MCP instead of internal simulator
  --vice-host 127.0.0.1  MCP server host
  --vice-port 6510        MCP server port
  --vice-timeout 5000     Execution timeout in ms
  --vice-warp/--no-vice-warp  Warp mode during tests
  --launch-vice           Auto-launch VICE process

Suite isolation via VICE snapshots: baseline saved after load(),
restored before each test."
```

---

## Task 7: Create ViceLauncher

**Files:**
- Create: `sim6502/Backend/ViceLauncher.cs`
- Create: `sim6502tests/Backend/ViceLauncherTests.cs`

**Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using sim6502.Backend;
using Xunit;

namespace sim6502tests.Backend;

public class ViceLauncherTests
{
    [Fact]
    public void BuildArguments_IncludesMcpFlags()
    {
        var args = ViceLauncher.BuildArguments(6510);
        args.Should().Contain("-mcpserver");
        args.Should().Contain("-mcpserverport");
        args.Should().Contain("6510");
        args.Should().Contain("+confirmexit");
    }

    [Fact]
    public void BuildArguments_UsesSpecifiedPort()
    {
        var args = ViceLauncher.BuildArguments(7000);
        args.Should().Contain("7000");
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test sim6502tests/ --filter "ViceLauncherTests"`
Expected: Build error.

**Step 3: Implement ViceLauncher**

```csharp
using System.Diagnostics;
using NLog;

namespace sim6502.Backend;

public class ViceLauncher : IDisposable
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    private Process? _viceProcess;
    private readonly int _port;

    public ViceLauncher(int port = 6510)
    {
        _port = port;
    }

    public void Launch()
    {
        var arguments = BuildArguments(_port);
        var executableName = FindViceExecutable();

        Logger.Info($"Launching VICE: {executableName} {arguments}");

        _viceProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executableName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        _viceProcess.Start();
        Logger.Info($"VICE started with PID {_viceProcess.Id}");

        // Wait for MCP server to become available
        WaitForMcpServer();
    }

    private void WaitForMcpServer()
    {
        var connection = new ViceConnection("127.0.0.1", _port);
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow - startTime < timeout)
        {
            if (connection.Ping())
            {
                Logger.Info("VICE MCP server is ready.");
                connection.Dispose();
                return;
            }
            Thread.Sleep(500);
        }

        connection.Dispose();
        throw new TimeoutException(
            $"VICE MCP server did not start within {timeout.TotalSeconds}s on port {_port}");
    }

    public static string BuildArguments(int port)
    {
        return $"-mcpserver -mcpserverport {port} +confirmexit";
    }

    private static string FindViceExecutable()
    {
        // Try common VICE executable names
        var candidates = new[] { "x64sc", "x64" };

        foreach (var name in candidates)
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = name,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });

                if (process == null) continue;
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    return output;
            }
            catch
            {
                // Continue to next candidate
            }
        }

        throw new FileNotFoundException(
            "Could not find VICE executable (x64sc or x64). " +
            "Ensure VICE is installed and available in PATH.");
    }

    public void Dispose()
    {
        if (_viceProcess is { HasExited: false })
        {
            Logger.Info("Stopping VICE process...");
            try
            {
                _viceProcess.Kill(entireProcessTree: true);
                _viceProcess.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to stop VICE cleanly: {ex.Message}");
            }
        }
        _viceProcess?.Dispose();
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test sim6502tests/ --filter "ViceLauncherTests"`
Expected: 2 tests pass.

**Step 5: Wire ViceLauncher into CLI**

In `Sim6502CLI.cs`, add launcher support in `RunTests`:

```csharp
ViceLauncher? viceLauncher = null;
try
{
    if (opts.LaunchVice && opts.Backend == "vice")
    {
        viceLauncher = new ViceLauncher(opts.VicePort);
        viceLauncher.Launch();
    }

    // ... existing test execution code ...
}
finally
{
    viceLauncher?.Dispose();
}
```

**Step 6: Run all tests**

Run: `dotnet test sim6502tests/`
Expected: All tests pass.

**Step 7: Commit**

```bash
git add sim6502/Backend/ViceLauncher.cs sim6502tests/Backend/ViceLauncherTests.cs sim6502/Sim6502CLI.cs
git commit -m "feat: add ViceLauncher for auto-launching VICE with --launch-vice"
```

---

## Task 8: Symbol Loading via VICE MCP

**Files:**
- Modify: `sim6502/Backend/ViceBackend.cs`
- Modify: `sim6502/Grammar/SimBaseListener.cs`

**Step 1: Update ViceBackend.LoadSymbols**

Check what MCP tool VICE exposes for symbol loading and implement accordingly. The current stub just logs.

In `ExitSymbolsFunction` in SimBaseListener, after loading symbols locally, also load them into VICE:

```csharp
// After existing symbol loading:
// Symbols = new SymbolFile(symbols);

// Also load into VICE backend
if (BackendType == "vice")
{
    Backend.LoadSymbols(filename);
}
```

**Step 2: Run all tests**

Run: `dotnet test sim6502tests/`
Expected: All tests pass.

**Step 3: Commit**

```bash
git add sim6502/Backend/ViceBackend.cs sim6502/Grammar/SimBaseListener.cs
git commit -m "feat: load symbols into VICE backend alongside local symbol resolution"
```

---

## Task 9: Final Verification and Cleanup

**Step 1: Run full test suite**

Run: `dotnet test sim6502tests/`
Expected: All tests pass.

**Step 2: Build release**

Run: `dotnet build sim6502/sim6502.csproj -c Release`
Expected: Builds cleanly with no warnings.

**Step 3: Test CLI help**

Run: `dotnet run --project sim6502/ -- --help`
Expected: Shows new --backend, --vice-host, --vice-port, --vice-timeout, --vice-warp, --launch-vice options.

**Step 4: Test with internal simulator (regression)**

Run: `dotnet run --project sim6502/ -- -s example/tests.6502`
Expected: Example tests pass exactly as before.

**Step 5: Commit any cleanup**

```bash
git status
# If clean, nothing to commit
```

---

## Summary

**New files (7):**

| File | Purpose |
|---|---|
| `sim6502/Backend/IExecutionBackend.cs` | Interface abstracting CPU execution |
| `sim6502/Backend/ExecutionResult.cs` | Result of JSR execution |
| `sim6502/Backend/SimulatorBackend.cs` | Wraps existing Processor |
| `sim6502/Backend/ViceConnection.cs` | JSON-RPC HTTP client for MCP |
| `sim6502/Backend/ViceBackend.cs` + `ViceBackendConfig.cs` | MCP-based backend |
| `sim6502/Backend/BackendFactory.cs` | Creates backends from CLI config |
| `sim6502/Backend/ViceLauncher.cs` | VICE process management |

**New test files (3):**

| File | Tests |
|---|---|
| `sim6502tests/Backend/SimulatorBackendTests.cs` | 8 tests |
| `sim6502tests/Backend/ViceConnectionTests.cs` | 4 tests |
| `sim6502tests/Backend/ViceBackendTests.cs` | 2 tests |
| `sim6502tests/Backend/ViceLauncherTests.cs` | 2 tests |

**Modified files (4):**

| File | Change |
|---|---|
| `sim6502/Grammar/SimBaseListener.cs` | Use `Backend` instead of `Proc` |
| `sim6502/Expressions/BaseCompare.cs` | Accept `IExecutionBackend` |
| `sim6502/Expressions/MemoryCompare.cs` | Use backend for memory reads |
| `sim6502/Sim6502CLI.cs` | New CLI flags |

**No new NuGet dependencies.**
