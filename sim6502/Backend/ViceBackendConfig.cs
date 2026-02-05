namespace sim6502.Backend;

public class ViceBackendConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6510;
    public int TimeoutMs { get; set; } = 5000;
    public bool WarpMode { get; set; } = true;
}
