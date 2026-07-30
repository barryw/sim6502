using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using sim6502.Backend;
using Xunit;

namespace sim6502tests.Backend;

public class NovaVmConnectionTests
{
    /// <summary>
    /// Starts a TCP listener on an ephemeral loopback port. Callers must
    /// dispose/stop it in a finally block — this is a real socket, not a
    /// fake, but it is entirely local and never touches an external service.
    /// </summary>
    private static (TcpListener Listener, int Port) StartLoopbackListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (listener, port);
    }

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

    // ── Connect / Send / Ping / Dispose over a real loopback socket ──

    [Fact]
    public void Connect_NoListener_ThrowsSocketException()
    {
        var (listener, port) = StartLoopbackListener();
        listener.Stop(); // free the port immediately; nothing is listening now

        using var conn = new NovaVmConnection("127.0.0.1", port, 500);
        var act = () => conn.Connect();

        act.Should().Throw<SocketException>();
    }

    [Fact]
    public void Connect_ValidServer_SetsIsConnectedTrue()
    {
        var (listener, port) = StartLoopbackListener();
        try
        {
            var serverTask = Task.Run(() =>
            {
                using var client = listener.AcceptTcpClient();
            });

            using var conn = new NovaVmConnection("127.0.0.1", port, 5000);
            conn.Connect();

            conn.IsConnected.Should().BeTrue();
            serverTask.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Send_RoundTrip_SendsRequestLineAndParsesResponse()
    {
        var (listener, port) = StartLoopbackListener();
        try
        {
            string? receivedLine = null;
            var serverTask = Task.Run(() =>
            {
                using var client = listener.AcceptTcpClient();
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                receivedLine = reader.ReadLine();
                writer.WriteLine("""{"ok":true,"value":42}""");
            });

            using var conn = new NovaVmConnection("127.0.0.1", port, 5000);
            conn.Connect();

            var result = conn.Send("peek", new Dictionary<string, object> { { "address", 0x1000 } });

            serverTask.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            result.GetProperty("value").GetInt32().Should().Be(42);

            var sentDoc = JsonDocument.Parse(receivedLine!);
            sentDoc.RootElement.GetProperty("command").GetString().Should().Be("peek");
            sentDoc.RootElement.GetProperty("address").GetInt32().Should().Be(0x1000);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Send_ServerReturnsError_ThrowsInvalidOperationException()
    {
        var (listener, port) = StartLoopbackListener();
        try
        {
            var serverTask = Task.Run(() =>
            {
                using var client = listener.AcceptTcpClient();
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                reader.ReadLine();
                writer.WriteLine("""{"ok":false,"error":"bad address"}""");
            });

            using var conn = new NovaVmConnection("127.0.0.1", port, 5000);
            conn.Connect();

            var act = () => conn.Send("peek");

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*peek*")
                .WithMessage("*bad address*");
            serverTask.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Send_ServerClosesWithoutResponding_ThrowsConnectionClosed()
    {
        var (listener, port) = StartLoopbackListener();
        try
        {
            var serverTask = Task.Run(() =>
            {
                using var client = listener.AcceptTcpClient();
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                // Drain the request so the client's write always succeeds, then
                // close without responding — only the read side should fail.
                reader.ReadLine();
            });

            using var conn = new NovaVmConnection("127.0.0.1", port, 5000);
            conn.Connect();

            var act = () => conn.Send("peek");

            act.Should().Throw<InvalidOperationException>().WithMessage("*Connection closed*");
            serverTask.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Ping_ServerRespondsOk_ReturnsTrue()
    {
        var (listener, port) = StartLoopbackListener();
        try
        {
            var serverTask = Task.Run(() =>
            {
                using var client = listener.AcceptTcpClient();
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                reader.ReadLine();
                writer.WriteLine("""{"ok":true,"value":0}""");
            });

            using var conn = new NovaVmConnection("127.0.0.1", port, 5000);
            conn.Connect();

            conn.Ping().Should().BeTrue();
            serverTask.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Ping_SendThrows_ReturnsFalseInsteadOfPropagating()
    {
        var (listener, port) = StartLoopbackListener();
        try
        {
            var serverTask = Task.Run(() =>
            {
                using var client = listener.AcceptTcpClient();
                // Close without responding — Send() throws, Ping() must swallow it.
            });

            using var conn = new NovaVmConnection("127.0.0.1", port, 5000);
            conn.Connect();
            serverTask.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            conn.Ping().Should().BeFalse();
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Dispose_AfterConnect_DoesNotThrow()
    {
        var (listener, port) = StartLoopbackListener();
        try
        {
            var serverTask = Task.Run(() =>
            {
                using var client = listener.AcceptTcpClient();
            });

            var conn = new NovaVmConnection("127.0.0.1", port, 5000);
            conn.Connect();
            serverTask.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            var act = () => conn.Dispose();
            act.Should().NotThrow();
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Dispose_WithoutConnect_DoesNotThrow()
    {
        var conn = new NovaVmConnection();
        var act = () => conn.Dispose();
        act.Should().NotThrow();
    }
}
