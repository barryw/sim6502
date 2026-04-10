using System.Text.Json;

namespace sim6502.Backend;

public interface INovaVmConnection : IDisposable
{
    void Connect();
    bool IsConnected { get; }
    JsonElement Send(string command, Dictionary<string, object>? args = null);
    bool Ping();
}
