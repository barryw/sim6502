using System.Text.Json;
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
