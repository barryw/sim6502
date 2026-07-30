using System.Text.Json;
using FluentAssertions;
using sim6502.Backend;
using Xunit;

namespace sim6502tests.Backend;

/// <summary>
/// Records all CallTool invocations and returns configurable canned responses.
/// </summary>
internal class MockViceConnection : IViceConnection
{
    public List<(string ToolName, Dictionary<string, object>? Args)> Calls { get; } = new();
    public bool PingResult { get; set; } = true;
    public bool Disposed { get; private set; }

    private readonly Dictionary<string, Queue<McpResponse>> _responses = new();
    private McpResponse _defaultResponse = new() { IsSuccess = true, Content = "{}" };

    public void SetResponse(string toolName, McpResponse response)
    {
        if (!_responses.ContainsKey(toolName))
            _responses[toolName] = new Queue<McpResponse>();
        _responses[toolName].Enqueue(response);
    }

    public void SetDefaultResponse(McpResponse response) => _defaultResponse = response;

    public McpResponse CallTool(string toolName, Dictionary<string, object>? arguments = null)
    {
        Calls.Add((toolName, arguments != null ? new Dictionary<string, object>(arguments) : null));

        if (_responses.TryGetValue(toolName, out var queue) && queue.Count > 0)
            return queue.Dequeue();

        return _defaultResponse;
    }

    public Task<McpResponse> CallToolAsync(string toolName, Dictionary<string, object>? arguments = null)
    {
        return Task.FromResult(CallTool(toolName, arguments));
    }

    public bool Ping() => PingResult;

    public void Dispose() { Disposed = true; }

    public List<(string ToolName, Dictionary<string, object>? Args)> GetCallsForTool(string toolName)
    {
        return Calls.Where(c => c.ToolName == toolName).ToList();
    }

    public bool WasToolCalled(string toolName) => Calls.Any(c => c.ToolName == toolName);
}

public class ViceBackendContractTests
{
    private static ViceBackendConfig DefaultConfig => new()
    {
        Host = "127.0.0.1",
        Port = 6510,
        TimeoutMs = 5000,
        WarpMode = false
    };

    private static McpResponse SuccessResponse(string json) => new()
    {
        IsSuccess = true,
        Content = json
    };

    private static McpResponse SuccessEmpty => new()
    {
        IsSuccess = true,
        Content = "{}"
    };

    // ── Memory contracts ──

    [Fact]
    public void ReadByte_CallsCorrectTool_WithCorrectParams()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.memory.read", SuccessResponse("{\"data\": [\"AB\"]}"));

        var backend = new ViceBackend(DefaultConfig, mock);
        backend.ReadByte(0x1234);

        var calls = mock.GetCallsForTool("vice.memory.read");
        calls.Should().HaveCount(1);
        calls[0].Args.Should().ContainKey("address").WhoseValue.Should().Be(0x1234);
        calls[0].Args.Should().ContainKey("size").WhoseValue.Should().Be(1);
    }

    [Fact]
    public void ReadByte_ParsesArrayResponse()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.memory.read", SuccessResponse("{\"data\": [\"FF\"]}"));

        var backend = new ViceBackend(DefaultConfig, mock);
        var result = backend.ReadByte(0x0000);

        result.Should().Be(0xFF);
    }

    [Fact]
    public void WriteByte_SendsDataAsNumberArray()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.WriteByte(0x1000, 0xFF);

        var calls = mock.GetCallsForTool("vice.memory.write");
        calls.Should().HaveCount(1);
        calls[0].Args.Should().ContainKey("address").WhoseValue.Should().Be(0x1000);
        var data = calls[0].Args!["data"];
        data.Should().BeAssignableTo<int[]>();
        ((int[])data).Should().Equal(255);
    }

    [Fact]
    public void LoadBinary_SendsDataAsNumberArray()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.LoadBinary(new byte[] { 1, 2, 3 }, 0xC000);

        var calls = mock.GetCallsForTool("vice.memory.write");
        calls.Should().HaveCount(1);
        calls[0].Args.Should().ContainKey("address").WhoseValue.Should().Be(0xC000);
        var data = calls[0].Args!["data"];
        data.Should().BeAssignableTo<object[]>();
        ((object[])data).Should().Equal(1, 2, 3);
    }

    // ── Checkpoint contracts ──

    private void SetupExecuteJsrMock(MockViceConnection mock, int checkpointNum = 1)
    {
        // registers.get for SP read (initial)
        mock.SetResponse("vice.registers.get",
            SuccessResponse("{\"SP\": 253, \"PC\": 0, \"A\": 0, \"X\": 0, \"Y\": 0}"));

        // registers.set calls (SP, PC) — just need success
        mock.SetResponse("vice.registers.set", SuccessEmpty);
        mock.SetResponse("vice.registers.set", SuccessEmpty);

        // checkpoint.add response
        mock.SetResponse("vice.checkpoint.add",
            SuccessResponse($"{{\"checkpoint_num\": {checkpointNum}}}"));

        // registers.get after execution (blocks via trap)
        mock.SetResponse("vice.registers.get",
            SuccessResponse("{\"SP\": 253, \"PC\": 0, \"A\": 0, \"X\": 0, \"Y\": 0}"));

        // memory.read for BRK check at finalPc
        mock.SetResponse("vice.memory.read",
            SuccessResponse("{\"data\": [\"60\"]}"));

        // checkpoint.delete — just need success
        mock.SetResponse("vice.checkpoint.delete", SuccessEmpty);

        // cycles.stopwatch
        mock.SetResponse("vice.cycles.stopwatch",
            SuccessResponse("{\"cycles\": 42}"));
    }

    [Fact]
    public void ExecuteJsr_UsesCheckpointAdd()
    {
        var mock = new MockViceConnection();
        SetupExecuteJsrMock(mock);

        var backend = new ViceBackend(DefaultConfig, mock);
        backend.ExecuteJsr(0x1000, 0, true, false);

        mock.WasToolCalled("vice.checkpoint.add").Should().BeTrue();
        mock.WasToolCalled("vice.breakpoints.set").Should().BeFalse();

        var addCalls = mock.GetCallsForTool("vice.checkpoint.add");
        addCalls.Should().HaveCount(1);
        addCalls[0].Args.Should().ContainKey("start");
    }

    [Fact]
    public void ExecuteJsr_ParsesCheckpointNum()
    {
        var mock = new MockViceConnection();
        SetupExecuteJsrMock(mock, checkpointNum: 7);

        var backend = new ViceBackend(DefaultConfig, mock);
        backend.ExecuteJsr(0x1000, 0, true, false);

        // Verify it used checkpoint_num=7 in the delete call
        var deleteCalls = mock.GetCallsForTool("vice.checkpoint.delete");
        deleteCalls.Should().HaveCount(1);
        deleteCalls[0].Args.Should().ContainKey("checkpoint_num").WhoseValue.Should().Be(7);
    }

    [Fact]
    public void ExecuteJsr_DeletesCheckpointsWithCorrectParams()
    {
        var mock = new MockViceConnection();
        SetupExecuteJsrMock(mock, checkpointNum: 3);

        var backend = new ViceBackend(DefaultConfig, mock);
        backend.ExecuteJsr(0x1000, 0, true, false);

        mock.WasToolCalled("vice.checkpoint.delete").Should().BeTrue();
        mock.WasToolCalled("vice.breakpoints.delete").Should().BeFalse();

        var deleteCalls = mock.GetCallsForTool("vice.checkpoint.delete");
        deleteCalls[0].Args.Should().ContainKey("checkpoint_num");
        deleteCalls[0].Args.Should().NotContainKey("id");
    }

    // ── Execution contracts ──

    [Fact]
    public void ExecuteJsr_DoesNotPollGetState()
    {
        var mock = new MockViceConnection();
        SetupExecuteJsrMock(mock);

        var backend = new ViceBackend(DefaultConfig, mock);
        backend.ExecuteJsr(0x1000, 0, true, false);

        mock.WasToolCalled("vice.execution.get_state").Should().BeFalse();
    }

    [Fact]
    public void ExecuteJsr_BlocksOnRegistersGetAfterRun()
    {
        var mock = new MockViceConnection();
        SetupExecuteJsrMock(mock);

        var backend = new ViceBackend(DefaultConfig, mock);
        backend.ExecuteJsr(0x1000, 0, true, false);

        // Find the index of execution.run and the next registers.get
        var runIndex = mock.Calls.FindIndex(c => c.ToolName == "vice.execution.run");
        runIndex.Should().BeGreaterThanOrEqualTo(0, "execution.run should be called");

        var nextRegGetIndex = mock.Calls.FindIndex(runIndex + 1, c => c.ToolName == "vice.registers.get");
        nextRegGetIndex.Should().Be(runIndex + 1,
            "registers.get should be the very next call after execution.run (trap mechanism)");
    }

    // ── Flag contracts ──

    [Fact]
    public void GetFlag_ReadsIndividualBoolean()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.registers.get",
            SuccessResponse("{\"C\": true, \"Z\": false, \"N\": true, \"V\": false, \"D\": false}"));

        var backend = new ViceBackend(DefaultConfig, mock);
        var carry = backend.GetFlag("C");
        carry.Should().BeTrue();

        // Should NOT try to read a "P" register
        var regCalls = mock.GetCallsForTool("vice.registers.get");
        regCalls.Should().HaveCount(1);
        // No bitmask manipulation needed — just boolean read
    }

    [Fact]
    public void SetFlag_SetsIndividualFlag()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.SetFlag("C", true);

        var setCalls = mock.GetCallsForTool("vice.registers.set");
        setCalls.Should().HaveCount(1);
        setCalls[0].Args.Should().ContainKey("register").WhoseValue.Should().Be("C");
        setCalls[0].Args.Should().ContainKey("value").WhoseValue.Should().Be(1);

        // Should NOT read registers first (no read-modify-write of P)
        mock.WasToolCalled("vice.registers.get").Should().BeFalse();
    }

    // ── Flag error paths ──

    [Fact]
    public void GetFlag_Failure_Throws()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.registers.get", new McpResponse { IsSuccess = false, ErrorMessage = "no flags" });
        var backend = new ViceBackend(DefaultConfig, mock);

        var act = () => backend.GetFlag("C");

        act.Should().Throw<InvalidOperationException>().WithMessage("*no flags*");
    }

    [Fact]
    public void GetFlag_UnknownFlag_ThrowsArgumentException()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.registers.get", SuccessResponse("{\"C\": true}"));
        var backend = new ViceBackend(DefaultConfig, mock);

        var act = () => backend.GetFlag("Q");

        act.Should().Throw<ArgumentException>().WithMessage("*Unknown flag*");
    }

    [Fact]
    public void SetFlag_Failure_Throws()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.registers.set", new McpResponse { IsSuccess = false, ErrorMessage = "bad flag" });
        var backend = new ViceBackend(DefaultConfig, mock);

        var act = () => backend.SetFlag("C", true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*bad flag*");
    }

    // ── Cycle contracts ──

    [Fact]
    public void GetCycles_MissingCyclesProperty_ReturnsZero()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.cycles.stopwatch", SuccessResponse("{}"));
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.GetCycles().Should().Be(0);
    }

    [Fact]
    public void GetCycles_UsesStopwatchTool()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.cycles.stopwatch",
            SuccessResponse("{\"cycles\": 1234}"));

        var backend = new ViceBackend(DefaultConfig, mock);
        var cycles = backend.GetCycles();

        cycles.Should().Be(1234);

        mock.WasToolCalled("vice.cycles.stopwatch").Should().BeTrue();
        mock.WasToolCalled("vice.trace.cycles.get").Should().BeFalse();

        var calls = mock.GetCallsForTool("vice.cycles.stopwatch");
        calls[0].Args.Should().ContainKey("action").WhoseValue.Should().Be("read");
    }

    [Fact]
    public void ResetCycleCount_UsesStopwatchTool()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.ResetCycleCount();

        mock.WasToolCalled("vice.cycles.stopwatch").Should().BeTrue();
        mock.WasToolCalled("vice.trace.cycles.reset").Should().BeFalse();

        var calls = mock.GetCallsForTool("vice.cycles.stopwatch");
        calls[0].Args.Should().ContainKey("action").WhoseValue.Should().Be("reset");
    }

    // ── Reset/config contracts ──

    [Fact]
    public void Reset_UsesMachineReset()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.Reset();

        mock.WasToolCalled("vice.machine.reset").Should().BeTrue();
        mock.WasToolCalled("vice.execution.reset").Should().BeFalse();
    }

    [Fact]
    public void SetWarpMode_CallsConfigSetWithCorrectToolName()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.SetWarpMode(true);

        mock.WasToolCalled("vice.machine.config.set").Should().BeTrue();
        var call = mock.GetCallsForTool("vice.machine.config.set").First();
        var resources = call.Args!["resources"] as Dictionary<string, object>;
        resources.Should().NotBeNull();
        resources!["WarpMode"].Should().Be(1);
    }

    [Fact]
    public void SetWarpMode_DisabledSetsWarpModeToZero()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.SetWarpMode(false);

        mock.WasToolCalled("vice.machine.config.set").Should().BeTrue();
        var call = mock.GetCallsForTool("vice.machine.config.set").First();
        var resources = call.Args!["resources"] as Dictionary<string, object>;
        resources.Should().NotBeNull();
        resources!["WarpMode"].Should().Be(0);
    }

    // ── Connect contracts ──

    [Fact]
    public void Connect_PingSucceeds_PausesExecution()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock); // WarpMode false

        backend.Connect();

        mock.WasToolCalled("vice.execution.pause").Should().BeTrue();
        mock.WasToolCalled("vice.machine.config.set").Should().BeFalse();
    }

    [Fact]
    public void Connect_WarpModeEnabled_AlsoSetsWarpMode()
    {
        var mock = new MockViceConnection();
        var config = new ViceBackendConfig { Host = "127.0.0.1", Port = 6510, TimeoutMs = 5000, WarpMode = true };
        var backend = new ViceBackend(config, mock);

        backend.Connect();

        mock.WasToolCalled("vice.machine.config.set").Should().BeTrue();
    }

    [Fact]
    public void Connect_PingFails_ThrowsAndNeverPauses()
    {
        var mock = new MockViceConnection { PingResult = false };
        var backend = new ViceBackend(DefaultConfig, mock);

        var act = () => backend.Connect();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Could not connect*");
        mock.WasToolCalled("vice.execution.pause").Should().BeFalse();
    }

    // ── Word / multi-byte memory contracts ──

    [Fact]
    public void WriteWord_WritesLowThenHighByte()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.WriteWord(0x2000, 0xABCD);

        var calls = mock.GetCallsForTool("vice.memory.write");
        calls.Should().HaveCount(2);
        calls[0].Args!["address"].Should().Be(0x2000);
        ((int[])calls[0].Args!["data"]).Should().Equal(0xCD);
        calls[1].Args!["address"].Should().Be(0x2001);
        ((int[])calls[1].Args!["data"]).Should().Equal(0xAB);
    }

    [Fact]
    public void WriteMemoryValue_ByteRange_WritesSingleByte()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.WriteMemoryValue(0x1000, 0x42);

        mock.GetCallsForTool("vice.memory.write").Should().HaveCount(1);
    }

    [Fact]
    public void WriteMemoryValue_WordRange_WritesTwoBytes()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.WriteMemoryValue(0x1000, 0x1234);

        mock.GetCallsForTool("vice.memory.write").Should().HaveCount(2);
    }

    [Fact]
    public void ReadWord_CombinesLoAndHiBytes()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.memory.read", SuccessResponse("{\"data\": [\"CD\"]}"));
        mock.SetResponse("vice.memory.read", SuccessResponse("{\"data\": [\"AB\"]}"));
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.ReadWord(0x2000).Should().Be(0xABCD);
    }

    [Fact]
    public void ReadByte_Failure_Throws()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.memory.read", new McpResponse { IsSuccess = false, ErrorMessage = "bad addr" });
        var backend = new ViceBackend(DefaultConfig, mock);

        var act = () => backend.ReadByte(0x9999);

        act.Should().Throw<InvalidOperationException>().WithMessage("*bad addr*");
    }

    [Fact]
    public void LoadBinary_Failure_Throws()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.memory.write", new McpResponse { IsSuccess = false, ErrorMessage = "write failed" });
        var backend = new ViceBackend(DefaultConfig, mock);

        var act = () => backend.LoadBinary(new byte[] { 1 }, 0xC000);

        act.Should().Throw<InvalidOperationException>().WithMessage("*write failed*");
    }

    // ── Register contracts ──

    [Fact]
    public void GetRegister_ReturnsParsedValue()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.registers.get", SuccessResponse("{\"A\": 66}"));
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.GetRegister("a").Should().Be(66);
    }

    [Fact]
    public void GetRegister_Failure_Throws()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.registers.get", new McpResponse { IsSuccess = false, ErrorMessage = "no regs" });
        var backend = new ViceBackend(DefaultConfig, mock);

        var act = () => backend.GetRegister("a");

        act.Should().Throw<InvalidOperationException>().WithMessage("*no regs*");
    }

    [Fact]
    public void SetRegister_SendsUppercaseNameAndValue()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.SetRegister("pc", 0xC000);

        var calls = mock.GetCallsForTool("vice.registers.set");
        calls[0].Args!["register"].Should().Be("PC");
        calls[0].Args!["value"].Should().Be(0xC000);
    }

    [Fact]
    public void SetRegister_Failure_Throws()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.registers.set", new McpResponse { IsSuccess = false, ErrorMessage = "bad reg" });
        var backend = new ViceBackend(DefaultConfig, mock);

        var act = () => backend.SetRegister("q", 1);

        act.Should().Throw<InvalidOperationException>().WithMessage("*bad reg*");
    }

    // ── Snapshot / symbol contracts ──

    [Fact]
    public void SaveSnapshot_Success_SendsName()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.SaveSnapshot("mysave");

        mock.GetCallsForTool("vice.snapshot.save")[0].Args!["name"].Should().Be("mysave");
    }

    [Fact]
    public void SaveSnapshot_Failure_Throws()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.snapshot.save", new McpResponse { IsSuccess = false, ErrorMessage = "disk full" });
        var backend = new ViceBackend(DefaultConfig, mock);

        var act = () => backend.SaveSnapshot("mysave");

        act.Should().Throw<InvalidOperationException>().WithMessage("*disk full*");
    }

    [Fact]
    public void RestoreSnapshot_Success_SendsName()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.RestoreSnapshot("mysave");

        mock.GetCallsForTool("vice.snapshot.load")[0].Args!["name"].Should().Be("mysave");
    }

    [Fact]
    public void RestoreSnapshot_Failure_Throws()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.snapshot.load", new McpResponse { IsSuccess = false, ErrorMessage = "not found" });
        var backend = new ViceBackend(DefaultConfig, mock);

        var act = () => backend.RestoreSnapshot("missing");

        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public void LoadSymbols_Success_SendsPath()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        var act = () => backend.LoadSymbols("/tmp/prog.sym");

        act.Should().NotThrow();
        mock.GetCallsForTool("vice.symbols.load")[0].Args!["path"].Should().Be("/tmp/prog.sym");
    }

    [Fact]
    public void LoadSymbols_Failure_LogsWarningInsteadOfThrowing()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.symbols.load", new McpResponse { IsSuccess = false, ErrorMessage = "bad symbols" });
        var backend = new ViceBackend(DefaultConfig, mock);

        var act = () => backend.LoadSymbols("/bad.sym");

        act.Should().NotThrow();
    }

    // ── Trace / Dispose contracts ──

    [Fact]
    public void TraceEnabled_CanBeSetAndRead()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.TraceEnabled = true;

        backend.TraceEnabled.Should().BeTrue();
    }

    [Fact]
    public void ClearTraceBuffer_DoesNotThrow()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        var act = () => backend.ClearTraceBuffer();

        act.Should().NotThrow();
    }

    [Fact]
    public void GetTraceBuffer_ReturnsEmptyList()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.GetTraceBuffer().Should().BeEmpty();
    }

    [Fact]
    public void Dispose_DisposesUnderlyingConnection()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.Dispose();

        mock.Disposed.Should().BeTrue();
    }

    // ── ExecuteJsr edge cases ──

    [Fact]
    public void ExecuteJsr_StopOnAddressReached_ReturnsStopAddressReason()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.registers.get",
            SuccessResponse("{\"SP\": 253, \"PC\": 0, \"A\": 0, \"X\": 0, \"Y\": 0}"));
        mock.SetResponse("vice.registers.set", SuccessEmpty);
        mock.SetResponse("vice.registers.set", SuccessEmpty);
        // Two checkpoints: one for RTS, one for the stop address.
        mock.SetResponse("vice.checkpoint.add", SuccessResponse("{\"checkpoint_num\": 1}"));
        mock.SetResponse("vice.checkpoint.add", SuccessResponse("{\"checkpoint_num\": 2}"));
        mock.SetResponse("vice.registers.get",
            SuccessResponse("{\"SP\": 253, \"PC\": 8192, \"A\": 0, \"X\": 0, \"Y\": 0}"));
        mock.SetResponse("vice.memory.read", SuccessResponse("{\"data\": [\"EA\"]}")); // NOP, not BRK
        mock.SetResponse("vice.checkpoint.delete", SuccessEmpty);
        mock.SetResponse("vice.checkpoint.delete", SuccessEmpty);
        mock.SetResponse("vice.cycles.stopwatch", SuccessResponse("{\"cycles\": 99}"));

        var backend = new ViceBackend(DefaultConfig, mock);
        var result = backend.ExecuteJsr(0x1000, 0x2000, true, false);

        result.Reason.Should().Be(StopReason.StopAddress);
        result.ProgramCounter.Should().Be(0x2000);
        result.ExitedCleanly.Should().BeTrue();
    }

    [Fact]
    public void ExecuteJsr_TrapTimesOut_ReturnsTimeoutReasonAndForcesPause()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.registers.get",
            SuccessResponse("{\"SP\": 253, \"PC\": 0, \"A\": 0, \"X\": 0, \"Y\": 0}"));
        mock.SetResponse("vice.registers.set", SuccessEmpty);
        mock.SetResponse("vice.registers.set", SuccessEmpty);
        mock.SetResponse("vice.checkpoint.add", SuccessResponse("{\"checkpoint_num\": 1}"));
        // Final registers.get fails (trap timed out) but still carries a parsable PC.
        mock.SetResponse("vice.registers.get", new McpResponse
        {
            IsSuccess = false,
            Content = "{\"SP\": 253, \"PC\": 4096, \"A\": 0, \"X\": 0, \"Y\": 0}",
            ErrorMessage = "trap timeout"
        });
        mock.SetResponse("vice.execution.pause", SuccessEmpty);
        mock.SetResponse("vice.memory.read", SuccessResponse("{\"data\": [\"60\"]}"));
        mock.SetResponse("vice.checkpoint.delete", SuccessEmpty);
        mock.SetResponse("vice.cycles.stopwatch", SuccessResponse("{\"cycles\": 5}"));

        var backend = new ViceBackend(DefaultConfig, mock);
        var result = backend.ExecuteJsr(0x1000, 0, true, false);

        result.Reason.Should().Be(StopReason.Timeout);
        result.ExitedCleanly.Should().BeFalse();
        mock.WasToolCalled("vice.execution.pause").Should().BeTrue();
    }

    [Fact]
    public void ExecuteJsr_HitsBrkWithFailOnBrk_ReturnsBrkReasonAndDirtyExit()
    {
        var mock = new MockViceConnection();
        mock.SetResponse("vice.registers.get",
            SuccessResponse("{\"SP\": 253, \"PC\": 0, \"A\": 0, \"X\": 0, \"Y\": 0}"));
        mock.SetResponse("vice.registers.set", SuccessEmpty);
        mock.SetResponse("vice.registers.set", SuccessEmpty);
        mock.SetResponse("vice.checkpoint.add", SuccessResponse("{\"checkpoint_num\": 1}"));
        mock.SetResponse("vice.registers.get",
            SuccessResponse("{\"SP\": 253, \"PC\": 1024, \"A\": 0, \"X\": 0, \"Y\": 0}"));
        mock.SetResponse("vice.memory.read", SuccessResponse("{\"data\": [\"00\"]}")); // BRK opcode
        mock.SetResponse("vice.checkpoint.delete", SuccessEmpty);
        mock.SetResponse("vice.cycles.stopwatch", SuccessResponse("{\"cycles\": 7}"));

        var backend = new ViceBackend(DefaultConfig, mock);
        var result = backend.ExecuteJsr(0x1000, 0, true, true);

        result.Reason.Should().Be(StopReason.Brk);
        result.ExitedCleanly.Should().BeFalse();
    }
}
