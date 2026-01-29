using sim6502.Proc;
using sim6502.Systems;
using Xunit;

namespace sim6502tests.Systems;

public class MemoryMapFactoryTests
{
    [Fact]
    public void CreateForSystem_C64_ReturnsC64MemoryMap()
    {
        var (map, procType) = MemoryMapFactory.CreateForSystem(SystemType.C64);
        Assert.IsType<C64MemoryMap>(map);
        Assert.Equal(ProcessorType.MOS6510, procType);
    }

    [Fact]
    public void CreateForSystem_Generic6502_ReturnsGenericMemoryMap()
    {
        var (map, procType) = MemoryMapFactory.CreateForSystem(SystemType.Generic6502);
        Assert.IsType<GenericMemoryMap>(map);
        Assert.Equal(ProcessorType.MOS6502, procType);
    }

    [Fact]
    public void CreateForSystem_Generic6510_ReturnsGeneric6510MemoryMap()
    {
        var (map, procType) = MemoryMapFactory.CreateForSystem(SystemType.Generic6510);
        Assert.IsType<Generic6510MemoryMap>(map);
        Assert.Equal(ProcessorType.MOS6510, procType);
    }

    [Fact]
    public void CreateForSystem_Generic65C02_ReturnsGenericMemoryMap()
    {
        var (map, procType) = MemoryMapFactory.CreateForSystem(SystemType.Generic65C02);
        Assert.IsType<GenericMemoryMap>(map);
        Assert.Equal(ProcessorType.WDC65C02, procType);
    }

    [Fact]
    public void CreateForProcessor_MapsToGenericSystem()
    {
        var (map6502, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6502);
        var (map6510, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6510);
        var (map65c02, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.WDC65C02);

        Assert.IsType<GenericMemoryMap>(map6502);
        Assert.IsType<Generic6510MemoryMap>(map6510);
        Assert.IsType<GenericMemoryMap>(map65c02);
    }
}
