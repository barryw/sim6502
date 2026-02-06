namespace sim6502.Backend;

public interface IViceConnection : IDisposable
{
    McpResponse CallTool(string toolName, Dictionary<string, object>? arguments = null);
    Task<McpResponse> CallToolAsync(string toolName, Dictionary<string, object>? arguments = null);
    bool Ping();
}
