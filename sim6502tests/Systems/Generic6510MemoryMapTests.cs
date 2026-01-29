using sim6502.Systems;
using Xunit;

namespace sim6502tests.Systems;

public class Generic6510MemoryMapTests
{
    [Fact]
    public void Address00_ReturnsDataDirection()
    {
        var map = new Generic6510MemoryMap();
        map.WriteWithoutCycle(0x00, 0x2F);
        Assert.Equal(0x2F, map.ReadWithoutCycle(0x00));
    }

    [Fact]
    public void Address01_ReturnsDataPort()
    {
        var map = new Generic6510MemoryMap();
        map.WriteWithoutCycle(0x01, 0x37);
        Assert.Equal(0x37, map.ReadWithoutCycle(0x01));
    }

    [Fact]
    public void Address02AndUp_UsesRam()
    {
        var map = new Generic6510MemoryMap();
        map.WriteWithoutCycle(0x02, 0xAB);
        Assert.Equal(0xAB, map.ReadWithoutCycle(0x02));
        Assert.Equal(0xAB, map.GetRam()[0x02]);
    }

    [Fact]
    public void Reset_ClearsIORegisters()
    {
        var map = new Generic6510MemoryMap();
        map.WriteWithoutCycle(0x00, 0xFF);
        map.WriteWithoutCycle(0x01, 0xFF);
        map.Reset();
        Assert.Equal(0x00, map.ReadWithoutCycle(0x00));
        Assert.Equal(0x00, map.ReadWithoutCycle(0x01));
    }
}
