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
