using NLog;
using sim6502.Proc;
using sim6502.Systems;

namespace sim6502.Backend;

public static class BackendFactory
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    public static IExecutionBackend Create(
        string backendType,
        ProcessorType processorType,
        IMemoryMap memoryMap,
        ViceBackendConfig? viceConfig = null,
        NovaVmBackendConfig? novaVmConfig = null)
    {
        switch (backendType.ToLower())
        {
            case "sim":
                return new SimulatorBackend(processorType, memoryMap);

            case "vice":
                if (viceConfig == null)
                    viceConfig = new ViceBackendConfig();

                var viceBackend = new ViceBackend(viceConfig);
                viceBackend.Connect();
                return viceBackend;

            case "novavm":
                if (novaVmConfig == null)
                    novaVmConfig = new NovaVmBackendConfig();

                var novaVmBackend = new NovaVmBackend(novaVmConfig);
                novaVmBackend.Connect();
                return novaVmBackend;

            case "verilator":
                // Same protocol as novavm, different default port (6503)
                if (novaVmConfig == null)
                    novaVmConfig = new NovaVmBackendConfig { Port = 6503 };
                else if (novaVmConfig.Port == 6502)
                    novaVmConfig.Port = 6503; // override default novavm port

                var verilatorBackend = new NovaVmBackend(novaVmConfig);
                verilatorBackend.Connect();
                return verilatorBackend;

            default:
                throw new ArgumentException($"Unknown backend type: {backendType}. Valid options: sim, vice, novavm, verilator");
        }
    }
}
