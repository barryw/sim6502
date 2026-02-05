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
        ViceBackendConfig? viceConfig = null)
    {
        switch (backendType.ToLower())
        {
            case "sim":
                return new SimulatorBackend(processorType, memoryMap);

            case "vice":
                if (viceConfig == null)
                    viceConfig = new ViceBackendConfig();

                var backend = new ViceBackend(viceConfig);
                backend.Connect();
                return backend;

            default:
                throw new ArgumentException($"Unknown backend type: {backendType}. Valid options: sim, vice");
        }
    }
}
