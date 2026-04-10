using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NLog;

namespace sim6502.Backend;

public class NovaVmConnection : INovaVmConnection
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    private readonly string _host;
    private readonly int _port;
    private readonly int _timeoutMs;
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public NovaVmConnection(string host = "127.0.0.1", int port = 6502, int timeoutMs = 10000)
    {
        _host = host;
        _port = port;
        _timeoutMs = timeoutMs;
    }

    public void Connect()
    {
        _client = new TcpClient();
        _client.Connect(_host, _port);
        _client.ReceiveTimeout = _timeoutMs;
        _client.SendTimeout = _timeoutMs;

        var stream = _client.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        Logger.Info($"Connected to e6502 emulator at {_host}:{_port}");
    }

    public bool IsConnected => _client?.Connected ?? false;

    /// <summary>
    /// Build the JSON request string for a command.
    /// </summary>
    public static string BuildRequestJson(string command, Dictionary<string, object>? args = null)
    {
        var request = new Dictionary<string, object> { { "command", command } };
        if (args != null)
        {
            foreach (var kv in args)
                request[kv.Key] = kv.Value;
        }
        return JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    /// <summary>
    /// Parse a JSON response line. Returns the root element.
    /// Throws InvalidOperationException if the response indicates an error.
    /// </summary>
    public static JsonElement ParseResponse(string responseLine, string command)
    {
        var doc = JsonDocument.Parse(responseLine);
        var root = doc.RootElement;

        if (root.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
        {
            var errorMsg = root.TryGetProperty("error", out var err) ? err.GetString() : "Unknown error";
            throw new InvalidOperationException($"Emulator error on '{command}': {errorMsg}");
        }

        return root;
    }

    /// <summary>
    /// Send a JSON command and return the parsed response.
    /// </summary>
    public JsonElement Send(string command, Dictionary<string, object>? args = null)
    {
        if (_writer == null || _reader == null)
            throw new InvalidOperationException("Not connected to emulator");

        var json = BuildRequestJson(command, args);
        Logger.Trace($"TX: {json}");

        _writer.WriteLine(json);

        var responseLine = _reader.ReadLine()
            ?? throw new InvalidOperationException("Connection closed by emulator");

        Logger.Trace($"RX: {responseLine}");

        return ParseResponse(responseLine, command);
    }

    /// <summary>
    /// Ping the emulator by sending a simple peek command.
    /// </summary>
    public bool Ping()
    {
        try
        {
            var result = Send("peek", new Dictionary<string, object> { { "address", 0 } });
            return result.TryGetProperty("ok", out var ok) && ok.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _client?.Dispose();
    }
}
