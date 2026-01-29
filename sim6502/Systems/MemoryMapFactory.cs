using sim6502.Proc;

namespace sim6502.Systems;

/// <summary>
/// Factory for creating appropriate memory maps based on system or processor type.
/// </summary>
public static class MemoryMapFactory
{
    /// <summary>
    /// Create a memory map and determine processor type for the given system.
    /// </summary>
    public static (IMemoryMap map, ProcessorType processorType) CreateForSystem(SystemType systemType)
    {
        return systemType switch
        {
            SystemType.C64 => (new C64MemoryMap(), ProcessorType.MOS6510),
            SystemType.Generic6502 => (new GenericMemoryMap(), ProcessorType.MOS6502),
            SystemType.Generic6510 => (new Generic6510MemoryMap(), ProcessorType.MOS6510),
            SystemType.Generic65C02 => (new GenericMemoryMap(), ProcessorType.WDC65C02),
            _ => (new GenericMemoryMap(), ProcessorType.MOS6502)
        };
    }

    /// <summary>
    /// Create a memory map for backward compatibility with processor() declaration.
    /// Maps to generic systems.
    /// </summary>
    public static (IMemoryMap map, ProcessorType processorType) CreateForProcessor(ProcessorType processorType)
    {
        return processorType switch
        {
            ProcessorType.MOS6502 => (new GenericMemoryMap(), ProcessorType.MOS6502),
            ProcessorType.MOS6510 => (new Generic6510MemoryMap(), ProcessorType.MOS6510),
            ProcessorType.WDC65C02 => (new GenericMemoryMap(), ProcessorType.WDC65C02),
            _ => (new GenericMemoryMap(), ProcessorType.MOS6502)
        };
    }
}
