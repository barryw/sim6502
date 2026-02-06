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

    public bool Ping() => true;

    public void Dispose() { }

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

    // ── Cycle contracts ──

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
    public void SetWarpMode_DoesNotCallConfigSet()
    {
        var mock = new MockViceConnection();
        var backend = new ViceBackend(DefaultConfig, mock);

        backend.SetWarpMode(true);
        backend.SetWarpMode(false);

        mock.WasToolCalled("vice.config.set").Should().BeFalse();
        mock.Calls.Should().BeEmpty("warp mode is a no-op");
    }
}
