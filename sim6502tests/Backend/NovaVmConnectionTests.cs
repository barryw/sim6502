using System.Text.Json;
using FluentAssertions;
using sim6502.Backend;
using Xunit;

namespace sim6502tests.Backend;

public class NovaVmConnectionTests
{
    // ── BuildRequestJson ──

    [Fact]
    public void BuildRequest_SimpleCommand_HasCommandField()
    {
        var json = NovaVmConnection.BuildRequestJson("peek");
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("command").GetString().Should().Be("peek");
    }

    [Fact]
    public void BuildRequest_WithArgs_MergesIntoTopLevel()
    {
        var args = new Dictionary<string, object>
        {
            { "address", 0x1234 },
            { "value", 42 }
        };
        var json = NovaVmConnection.BuildRequestJson("poke", args);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("command").GetString().Should().Be("poke");
        root.GetProperty("address").GetInt32().Should().Be(0x1234);
        root.GetProperty("value").GetInt32().Should().Be(42);
    }

    [Fact]
    public void BuildRequest_NullArgs_OnlyHasCommand()
    {
        var json = NovaVmConnection.BuildRequestJson("cold_start", null);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("command").GetString().Should().Be("cold_start");
        root.EnumerateObject().Should().HaveCount(1);
    }

    [Fact]
    public void BuildRequest_EmptyArgs_OnlyHasCommand()
    {
        var json = NovaVmConnection.BuildRequestJson("cold_start", new Dictionary<string, object>());
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.EnumerateObject().Should().HaveCount(1);
    }

    [Fact]
    public void BuildRequest_StringArgs_SerializedCorrectly()
    {
        var args = new Dictionary<string, object>
        {
            { "text", "HELLO WORLD" },
            { "delay_ms", 2 }
        };
        var json = NovaVmConnection.BuildRequestJson("type_text", args);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("text").GetString().Should().Be("HELLO WORLD");
        root.GetProperty("delay_ms").GetInt32().Should().Be(2);
    }

    // ── ParseResponse ──

    [Fact]
    public void ParseResponse_Success_ReturnsRootElement()
    {
        var response = """{"ok":true,"address":4096,"value":42}""";
        var result = NovaVmConnection.ParseResponse(response, "peek");

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("value").GetInt32().Should().Be(42);
    }

    [Fact]
    public void ParseResponse_SuccessNoData_ReturnsOk()
    {
        var response = """{"ok":true}""";
        var result = NovaVmConnection.ParseResponse(response, "poke");

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ParseResponse_Error_ThrowsWithCommandName()
    {
        var response = """{"ok":false,"error":"Missing 'address'"}""";

        var act = () => NovaVmConnection.ParseResponse(response, "peek");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*peek*")
            .WithMessage("*Missing 'address'*");
    }

    [Fact]
    public void ParseResponse_ErrorNoMessage_ThrowsUnknownError()
    {
        var response = """{"ok":false}""";

        var act = () => NovaVmConnection.ParseResponse(response, "bad_cmd");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown error*");
    }

    [Fact]
    public void ParseResponse_ScreenLines_ParsesArray()
    {
        var response = """{"ok":true,"lines":["Ready","",""],"cursor_x":0,"cursor_y":1}""";
        var result = NovaVmConnection.ParseResponse(response, "read_screen");

        var lines = result.GetProperty("lines");
        lines.GetArrayLength().Should().BeGreaterThan(0);
        lines[0].GetString().Should().Be("Ready");
        result.GetProperty("cursor_x").GetInt32().Should().Be(0);
        result.GetProperty("cursor_y").GetInt32().Should().Be(1);
    }

    [Fact]
    public void ParseResponse_WaitReadyFound_HasFoundTrue()
    {
        var response = """{"ok":true,"found":true,"row":1}""";
        var result = NovaVmConnection.ParseResponse(response, "wait_ready");

        result.GetProperty("found").GetBoolean().Should().BeTrue();
        result.GetProperty("row").GetInt32().Should().Be(1);
    }

    [Fact]
    public void ParseResponse_WaitReadyTimeout_HasFoundFalse()
    {
        var response = """{"ok":true,"found":false}""";
        var result = NovaVmConnection.ParseResponse(response, "wait_ready");

        result.GetProperty("found").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void ParseResponse_DbgState_ParsesAllRegistersAndFlags()
    {
        var response = """{"ok":true,"a":42,"x":0,"y":0,"sp":255,"pc":40960,"nf":0,"vf":0,"df":0,"if":1,"zf":0,"cf":0,"paused":false}""";
        var result = NovaVmConnection.ParseResponse(response, "dbg_state");

        result.GetProperty("a").GetInt32().Should().Be(42);
        result.GetProperty("sp").GetInt32().Should().Be(255);
        result.GetProperty("pc").GetInt32().Should().Be(40960);
        result.GetProperty("cf").GetInt32().Should().Be(0);
    }

    // ── Send (not connected) ──

    [Fact]
    public void Send_NotConnected_Throws()
    {
        var conn = new NovaVmConnection();
        var act = () => conn.Send("peek");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Not connected*");
    }

    // ── IsConnected ──

    [Fact]
    public void IsConnected_BeforeConnect_ReturnsFalse()
    {
        var conn = new NovaVmConnection();
        conn.IsConnected.Should().BeFalse();
    }
}
