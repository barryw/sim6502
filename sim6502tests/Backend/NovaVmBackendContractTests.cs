using System.Text.Json;
using FluentAssertions;
using sim6502.Backend;
using Xunit;

namespace sim6502tests.Backend;

/// <summary>
/// Mock INovaVmConnection that records calls and returns configurable canned responses.
/// </summary>
internal class MockNovaVmConnection : INovaVmConnection
{
    public List<(string Command, Dictionary<string, object>? Args)> Calls { get; } = new();
    public bool Connected { get; set; } = true;

    private readonly Dictionary<string, Queue<JsonElement>> _responses = new();
    private JsonElement _defaultResponse = JsonDocument.Parse("""{"ok":true}""").RootElement;

    public void SetResponse(string command, string json)
    {
        if (!_responses.ContainsKey(command))
            _responses[command] = new Queue<JsonElement>();
        _responses[command].Enqueue(JsonDocument.Parse(json).RootElement);
    }

    public void SetDefaultResponse(string json)
    {
        _defaultResponse = JsonDocument.Parse(json).RootElement;
    }

    public void Connect() => Connected = true;
    public bool IsConnected => Connected;

    public JsonElement Send(string command, Dictionary<string, object>? args = null)
    {
        Calls.Add((command, args != null ? new Dictionary<string, object>(args) : null));

        if (_responses.TryGetValue(command, out var queue) && queue.Count > 0)
            return queue.Dequeue();

        return _defaultResponse;
    }

    public bool Ping() => Connected;
    public void Dispose() { }

    public List<(string Command, Dictionary<string, object>? Args)> GetCallsFor(string command)
        => Calls.Where(c => c.Command == command).ToList();

    public bool WasCalled(string command) => Calls.Any(c => c.Command == command);
}

public class NovaVmBackendContractTests
{
    private static NovaVmBackendConfig DefaultConfig => new()
    {
        Host = "127.0.0.1",
        Port = 6502,
        TimeoutMs = 10000
    };

    private static (NovaVmBackend backend, MockNovaVmConnection mock) CreateBackend()
    {
        var mock = new MockNovaVmConnection();
        var backend = new NovaVmBackend(DefaultConfig, mock);
        return (backend, mock);
    }

    // ── Memory: ReadByte / WriteByte ──

    [Fact]
    public void ReadByte_SendsPeekWithAddress()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("peek", """{"ok":true,"address":4096,"value":42}""");

        var result = backend.ReadByte(0x1000);

        result.Should().Be(42);
        var calls = mock.GetCallsFor("peek");
        calls.Should().HaveCount(1);
        calls[0].Args.Should().ContainKey("address").WhoseValue.Should().Be(0x1000);
    }

    [Fact]
    public void ReadByte_ZeroAddress_Works()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("peek", """{"ok":true,"address":0,"value":0}""");

        backend.ReadByte(0x0000).Should().Be(0);
    }

    [Fact]
    public void ReadByte_MaxAddress_Works()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("peek", """{"ok":true,"address":65535,"value":255}""");

        backend.ReadByte(0xFFFF).Should().Be(255);
    }

    [Fact]
    public void WriteByte_SendsPokeWithAddressAndValue()
    {
        var (backend, mock) = CreateBackend();
        backend.WriteByte(0x2000, 0xAA);

        var calls = mock.GetCallsFor("poke");
        calls.Should().HaveCount(1);
        calls[0].Args.Should().ContainKey("address").WhoseValue.Should().Be(0x2000);
        calls[0].Args.Should().ContainKey("value").WhoseValue.Should().Be(0xAA);
    }

    [Fact]
    public void WriteWord_SendsTwoPokes_LittleEndian()
    {
        var (backend, mock) = CreateBackend();
        backend.WriteWord(0x3000, 0xABCD);

        var calls = mock.GetCallsFor("poke");
        calls.Should().HaveCount(2);

        // Low byte first
        calls[0].Args!["address"].Should().Be(0x3000);
        calls[0].Args!["value"].Should().Be(0xCD);

        // High byte second
        calls[1].Args!["address"].Should().Be(0x3001);
        calls[1].Args!["value"].Should().Be(0xAB);
    }

    [Fact]
    public void ReadWord_ReadsTwoBytes_LittleEndian()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("peek", """{"ok":true,"value":205}""");  // 0xCD = low
        mock.SetResponse("peek", """{"ok":true,"value":171}""");  // 0xAB = high

        var result = backend.ReadWord(0x3000);
        result.Should().Be(0xABCD);
    }

    [Fact]
    public void WriteMemoryValue_ByteValue_SendsOnePoke()
    {
        var (backend, mock) = CreateBackend();
        backend.WriteMemoryValue(0x1000, 0x42);

        mock.GetCallsFor("poke").Should().HaveCount(1);
    }

    [Fact]
    public void WriteMemoryValue_WordValue_SendsTwoPokes()
    {
        var (backend, mock) = CreateBackend();
        backend.WriteMemoryValue(0x1000, 0x1234);

        mock.GetCallsFor("poke").Should().HaveCount(2);
    }

    [Fact]
    public void LoadBinary_PokesEachByte()
    {
        var (backend, mock) = CreateBackend();
        backend.LoadBinary(new byte[] { 0x01, 0x02, 0x03 }, 0xC000);

        var calls = mock.GetCallsFor("poke");
        calls.Should().HaveCount(3);
        calls[0].Args!["address"].Should().Be(0xC000);
        calls[0].Args!["value"].Should().Be(1);
        calls[1].Args!["address"].Should().Be(0xC001);
        calls[1].Args!["value"].Should().Be(2);
        calls[2].Args!["address"].Should().Be(0xC002);
        calls[2].Args!["value"].Should().Be(3);
    }

    [Fact]
    public void LoadBinary_EmptyArray_NoPokes()
    {
        var (backend, mock) = CreateBackend();
        backend.LoadBinary(Array.Empty<byte>(), 0xC000);

        mock.GetCallsFor("poke").Should().BeEmpty();
    }

    // ── Registers ──

    [Fact]
    public void GetRegister_SendsDbgState_ReturnsValue()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("dbg_state",
            """{"ok":true,"a":42,"x":10,"y":20,"sp":253,"pc":49152,"nf":0,"vf":0,"df":0,"if":1,"zf":0,"cf":0}""");

        backend.GetRegister("a").Should().Be(42);
        mock.WasCalled("dbg_state").Should().BeTrue();
    }

    [Fact]
    public void GetRegister_CaseInsensitive()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("dbg_state",
            """{"ok":true,"a":42,"x":10,"y":20,"sp":253,"pc":49152,"nf":0,"vf":0,"df":0,"if":1,"zf":0,"cf":0}""");

        backend.GetRegister("A").Should().Be(42);
    }

    [Fact]
    public void GetRegister_UnknownRegister_Throws()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("dbg_state",
            """{"ok":true,"a":0,"x":0,"y":0,"sp":255,"pc":0,"nf":0,"vf":0,"df":0,"if":0,"zf":0,"cf":0}""");

        var act = () => backend.GetRegister("q");
        act.Should().Throw<ArgumentException>().WithMessage("*Unknown register*");
    }

    [Fact]
    public void SetRegister_ThrowsNotSupported()
    {
        var (backend, _) = CreateBackend();
        var act = () => backend.SetRegister("a", 42);
        act.Should().Throw<NotSupportedException>();
    }

    // ── Flags ──

    [Fact]
    public void GetFlag_ReadsFromDbgState()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("dbg_state",
            """{"ok":true,"a":0,"x":0,"y":0,"sp":255,"pc":0,"nf":0,"vf":0,"df":0,"if":1,"zf":0,"cf":1}""");

        backend.GetFlag("c").Should().BeTrue();
    }

    [Fact]
    public void GetFlag_FalseWhenZero()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("dbg_state",
            """{"ok":true,"a":0,"x":0,"y":0,"sp":255,"pc":0,"nf":0,"vf":0,"df":0,"if":1,"zf":0,"cf":0}""");

        backend.GetFlag("c").Should().BeFalse();
    }

    [Fact]
    public void GetFlag_UnknownFlag_Throws()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("dbg_state",
            """{"ok":true,"a":0,"x":0,"y":0,"sp":255,"pc":0,"nf":0,"vf":0,"df":0,"if":0,"zf":0,"cf":0}""");

        var act = () => backend.GetFlag("q");
        act.Should().Throw<ArgumentException>().WithMessage("*Unknown flag*");
    }

    [Fact]
    public void SetFlag_ThrowsNotSupported()
    {
        var (backend, _) = CreateBackend();
        var act = () => backend.SetFlag("c", true);
        act.Should().Throw<NotSupportedException>();
    }

    // ── ExecuteJsr ──

    [Fact]
    public void ExecuteJsr_ThrowsNotSupported()
    {
        var (backend, _) = CreateBackend();
        var act = () => backend.ExecuteJsr(0x1000, 0, true, false);
        act.Should().Throw<NotSupportedException>();
    }

    // ── Cycles ──

    [Fact]
    public void GetCycles_ReturnsInternalCounter()
    {
        var (backend, _) = CreateBackend();
        backend.GetCycles().Should().Be(0);
    }

    [Fact]
    public void ResetCycleCount_ZerosCounter()
    {
        var (backend, _) = CreateBackend();
        backend.ResetCycleCount();
        backend.GetCycles().Should().Be(0);
    }

    // ── Symbols, Snapshots, Config (no-ops) ──

    [Fact]
    public void LoadSymbols_DoesNotThrow()
    {
        var (backend, mock) = CreateBackend();
        backend.LoadSymbols("/some/path.sym");
        mock.Calls.Should().BeEmpty(); // no TCP call
    }

    [Fact]
    public void SaveSnapshot_DoesNotThrow()
    {
        var (backend, mock) = CreateBackend();
        backend.SaveSnapshot("test");
        mock.Calls.Should().BeEmpty();
    }

    [Fact]
    public void RestoreSnapshot_DoesNotThrow()
    {
        var (backend, mock) = CreateBackend();
        backend.RestoreSnapshot("test");
        mock.Calls.Should().BeEmpty();
    }

    [Fact]
    public void SetWarpMode_DoesNotThrow()
    {
        var (backend, mock) = CreateBackend();
        backend.SetWarpMode(true);
        mock.Calls.Should().BeEmpty();
    }

    [Fact]
    public void TraceEnabled_DefaultsFalse()
    {
        var (backend, _) = CreateBackend();
        backend.TraceEnabled.Should().BeFalse();
    }

    [Fact]
    public void GetTraceBuffer_ReturnsEmpty()
    {
        var (backend, _) = CreateBackend();
        backend.GetTraceBuffer().Should().BeEmpty();
    }

    // ── IHighLevelBackend: SendText ──

    [Fact]
    public void SendText_SendsTypeTextCommand()
    {
        var (backend, mock) = CreateBackend();
        backend.SendText("10 PRINT \"HELLO\"");

        var calls = mock.GetCallsFor("type_text");
        calls.Should().HaveCount(1);
        calls[0].Args!["text"].Should().Be("10 PRINT \"HELLO\"");
        calls[0].Args!["delay_ms"].Should().Be(2);
    }

    [Fact]
    public void SendText_EmptyString_StillSends()
    {
        var (backend, mock) = CreateBackend();
        backend.SendText("");

        mock.GetCallsFor("type_text").Should().HaveCount(1);
    }

    // ── IHighLevelBackend: SendKey ──

    [Fact]
    public void SendKey_SendsKeyCommand()
    {
        var (backend, mock) = CreateBackend();
        backend.SendKey("ENTER");

        var calls = mock.GetCallsFor("send_key");
        calls.Should().HaveCount(1);
        calls[0].Args!["key"].Should().Be("ENTER");
    }

    [Fact]
    public void SendKey_CtrlC_PassesThrough()
    {
        var (backend, mock) = CreateBackend();
        backend.SendKey("CTRL-C");

        mock.GetCallsFor("send_key")[0].Args!["key"].Should().Be("CTRL-C");
    }

    // ── IHighLevelBackend: ReadScreen ──

    [Fact]
    public void ReadScreen_ReturnsLineArray()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("read_screen",
            """{"ok":true,"lines":["Ready","10 PRINT X",""],"cursor_x":0,"cursor_y":2}""");

        var lines = backend.ReadScreen();
        lines.Should().HaveCount(3);
        lines[0].Should().Be("Ready");
        lines[1].Should().Be("10 PRINT X");
        lines[2].Should().BeEmpty();
    }

    [Fact]
    public void ReadScreen_EmptyScreen_ReturnsEmptyStrings()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("read_screen",
            """{"ok":true,"lines":["","",""],"cursor_x":0,"cursor_y":0}""");

        var lines = backend.ReadScreen();
        lines.Should().AllBe("");
    }

    // ── IHighLevelBackend: ReadLine ──

    [Fact]
    public void ReadLine_SendsRowParameter()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("read_line", """{"ok":true,"row":5,"text":"HELLO"}""");

        var text = backend.ReadLine(5);
        text.Should().Be("HELLO");

        var calls = mock.GetCallsFor("read_line");
        calls[0].Args!["row"].Should().Be(5);
    }

    // ── IHighLevelBackend: GetCursor ──

    [Fact]
    public void GetCursor_ReturnsTuple()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("get_cursor", """{"ok":true,"x":10,"y":5}""");

        var (x, y) = backend.GetCursor();
        x.Should().Be(10);
        y.Should().Be(5);
    }

    // ── IHighLevelBackend: WaitForText ──

    [Fact]
    public void WaitForText_Found_DoesNotThrow()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("wait_ready", """{"ok":true,"found":true,"row":0}""");

        backend.WaitForText("Ready");
        mock.GetCallsFor("wait_ready").Should().HaveCount(1);
    }

    [Fact]
    public void WaitForText_SendsCorrectParams()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("wait_ready", """{"ok":true,"found":true,"row":0}""");

        backend.WaitForText("DONE", 3000);

        var calls = mock.GetCallsFor("wait_ready");
        calls[0].Args!["text"].Should().Be("DONE");
        calls[0].Args!["timeout_ms"].Should().Be(3000);
    }

    [Fact]
    public void WaitForText_NotFound_ThrowsTimeout()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("wait_ready", """{"ok":true,"found":false}""");

        var act = () => backend.WaitForText("NEVER", 100);
        act.Should().Throw<TimeoutException>()
            .WithMessage("*NEVER*")
            .WithMessage("*100ms*");
    }

    // ── IHighLevelBackend: ColdStart ──

    [Fact]
    public void ColdStart_SendsColdStartThenWaitsForReady()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("wait_ready", """{"ok":true,"found":true,"row":0}""");

        backend.ColdStart();

        mock.WasCalled("cold_start").Should().BeTrue();
        mock.WasCalled("wait_ready").Should().BeTrue();

        // cold_start should come before wait_ready
        var coldIdx = mock.Calls.FindIndex(c => c.Command == "cold_start");
        var waitIdx = mock.Calls.FindIndex(c => c.Command == "wait_ready");
        coldIdx.Should().BeLessThan(waitIdx);
    }

    [Fact]
    public void ColdStart_WaitUsesConfigTimeout()
    {
        var config = new NovaVmBackendConfig { TimeoutMs = 15000 };
        var mock = new MockNovaVmConnection();
        var backend = new NovaVmBackend(config, mock);
        mock.SetResponse("wait_ready", """{"ok":true,"found":true,"row":0}""");

        backend.ColdStart();

        var waitCalls = mock.GetCallsFor("wait_ready");
        waitCalls[0].Args!["timeout_ms"].Should().Be(15000);
    }

    [Fact]
    public void ColdStart_ReadyNotFound_ThrowsTimeout()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("wait_ready", """{"ok":true,"found":false}""");

        var act = () => backend.ColdStart();
        act.Should().Throw<TimeoutException>().WithMessage("*Ready*");
    }

    // ── IHighLevelBackend: Reset ──

    [Fact]
    public void Reset_CallsColdStart()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("wait_ready", """{"ok":true,"found":true,"row":0}""");

        backend.Reset();

        mock.WasCalled("cold_start").Should().BeTrue();
    }

    // ── IHighLevelBackend: Pause / Resume ──

    [Fact]
    public void Pause_SendsDbgPause()
    {
        var (backend, mock) = CreateBackend();
        backend.Pause();
        mock.WasCalled("dbg_pause").Should().BeTrue();
    }

    [Fact]
    public void Resume_SendsDbgResume()
    {
        var (backend, mock) = CreateBackend();
        backend.Resume();
        mock.WasCalled("dbg_resume").Should().BeTrue();
    }

    // ── IHighLevelBackend: RunCycles (native run_cycles command) ──

    [Fact]
    public void RunCycles_SendsRunCyclesCommand()
    {
        var (backend, mock) = CreateBackend();
        backend.RunCycles(50000);

        var calls = mock.GetCallsFor("run_cycles");
        calls.Should().HaveCount(1);
        calls[0].Args!["cycles"].Should().Be(50000);
    }

    [Fact]
    public void RunCycles_Zero_StillSendsCommand()
    {
        var (backend, mock) = CreateBackend();
        backend.RunCycles(0);

        mock.GetCallsFor("run_cycles").Should().HaveCount(1);
    }

    [Fact]
    public void RunCycles_DoesNotUseFallbackSteps()
    {
        var (backend, mock) = CreateBackend();
        backend.RunCycles(100);

        mock.WasCalled("dbg_step").Should().BeFalse();
        mock.WasCalled("dbg_pause").Should().BeFalse();
    }

    // ── IHighLevelBackend: WaitForMemory (native watch command) ──

    [Fact]
    public void WaitForMemory_Matched_Returns()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("watch", """{"ok":true,"matched":true,"address":8192,"expected":42,"actual":42}""");

        backend.WaitForMemory(0x2000, 42, 1000);

        var calls = mock.GetCallsFor("watch");
        calls.Should().HaveCount(1);
        calls[0].Args!["address"].Should().Be(0x2000);
        calls[0].Args!["value"].Should().Be(42);
        calls[0].Args!["timeout_ms"].Should().Be(1000);
    }

    [Fact]
    public void WaitForMemory_NotMatched_ThrowsTimeout()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("watch", """{"ok":true,"matched":false,"address":8192,"expected":42,"actual":0}""");

        var act = () => backend.WaitForMemory(0x2000, 42, 200);
        act.Should().Throw<TimeoutException>()
            .WithMessage("*$2000*")
            .WithMessage("*$2A*");
    }

    [Fact]
    public void WaitForMemory_DoesNotUseFallbackPeek()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("watch", """{"ok":true,"matched":true,"address":0,"expected":0,"actual":0}""");

        backend.WaitForMemory(0x0000, 0, 1000);

        mock.WasCalled("peek").Should().BeFalse();
        mock.WasCalled("watch").Should().BeTrue();
    }

    [Fact]
    public void WaitForMemory_DefaultTimeout()
    {
        var (backend, mock) = CreateBackend();
        mock.SetResponse("watch", """{"ok":true,"matched":true,"address":0,"expected":0,"actual":0}""");

        backend.WaitForMemory(0x1000, 0xFF);

        var calls = mock.GetCallsFor("watch");
        calls[0].Args!["timeout_ms"].Should().Be(5000); // default
    }

    // ── Dispose ──

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var (backend, _) = CreateBackend();
        var act = () => backend.Dispose();
        act.Should().NotThrow();
    }
}
