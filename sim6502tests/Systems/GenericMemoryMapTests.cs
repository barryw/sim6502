using sim6502.Systems;
using Xunit;

namespace sim6502tests.Systems;

public class GenericMemoryMapTests
{
    [Fact]
    public void Read_ReturnsWrittenValue()
    {
        var map = new GenericMemoryMap();
        map.WriteWithoutCycle(0x1000, 0x42);
        Assert.Equal(0x42, map.ReadWithoutCycle(0x1000));
    }

    [Fact]
    public void Write_StoresValueInRam()
    {
        var map = new GenericMemoryMap();
        map.WriteWithoutCycle(0x2000, 0xAB);
        var ram = map.GetRam();
        Assert.Equal(0xAB, ram[0x2000]);
    }

    [Fact]
    public void LoadProgram_LoadsDataAtAddress()
    {
        var map = new GenericMemoryMap();
        var program = new byte[] { 0xA9, 0x42, 0x60 }; // LDA #$42, RTS
        map.LoadProgram(0x0600, program);

        Assert.Equal(0xA9, map.ReadWithoutCycle(0x0600));
        Assert.Equal(0x42, map.ReadWithoutCycle(0x0601));
        Assert.Equal(0x60, map.ReadWithoutCycle(0x0602));
    }

    [Fact]
    public void Reset_ClearsMemory()
    {
        var map = new GenericMemoryMap();
        map.WriteWithoutCycle(0x1000, 0xFF);
        map.Reset();
        Assert.Equal(0x00, map.ReadWithoutCycle(0x1000));
    }

    [Fact]
    public void LoadRom_IsIgnoredForGenericSystem()
    {
        var map = new GenericMemoryMap();
        var rom = new byte[] { 0x01, 0x02, 0x03 };
        // Should not throw - just ignored for generic systems
        map.LoadRom("test", rom);
    }

    [Fact]
    public void Read_IncrementsCycleCount()
    {
        var map = new GenericMemoryMap();
        var cycleCount = 0;
        map.IncrementCycleCount = () => cycleCount++;

        map.Read(0x1000);
        Assert.Equal(1, cycleCount);
    }

    [Fact]
    public void Write_IncrementsCycleCount()
    {
        var map = new GenericMemoryMap();
        var cycleCount = 0;
        map.IncrementCycleCount = () => cycleCount++;

        map.Write(0x1000, 0x42);
        Assert.Equal(1, cycleCount);
    }
}
