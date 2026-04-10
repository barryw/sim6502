namespace sim6502.Backend;

public class NovaVmBackendConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6502;
    public int TimeoutMs { get; set; } = 10000;
}
