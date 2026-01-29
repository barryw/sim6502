using sim6502.Systems;
using Xunit;

namespace sim6502tests.Systems;

public class C64MemoryMapTests
{
    private C64MemoryMap CreateMapWithRoms()
    {
        var map = new C64MemoryMap();

        // Create test ROMs with recognizable patterns
        var basicRom = new byte[0x2000]; // $A000-$BFFF
        for (int i = 0; i < basicRom.Length; i++)
            basicRom[i] = 0xBA; // "BA" for BASIC

        var kernalRom = new byte[0x2000]; // $E000-$FFFF
        for (int i = 0; i < kernalRom.Length; i++)
            kernalRom[i] = 0xEA; // "EA" for kErnAl (and it's NOP!)

        map.LoadRom("basic", basicRom);
        map.LoadRom("kernal", kernalRom);

        return map;
    }

    [Fact]
    public void DefaultBanking_RomsVisible()
    {
        var map = CreateMapWithRoms();
        // Default $01 = $37: BASIC + KERNAL visible

        Assert.Equal(0xBA, map.ReadWithoutCycle(0xA000)); // BASIC ROM
        Assert.Equal(0xEA, map.ReadWithoutCycle(0xE000)); // KERNAL ROM
    }

    [Fact]
    public void Write_AlwaysGoesToRam_EvenWhenRomVisible()
    {
        var map = CreateMapWithRoms();
        // Default: ROMs visible, but writes go to RAM underneath

        map.WriteWithoutCycle(0xA000, 0x42);

        // Reading still shows ROM
        Assert.Equal(0xBA, map.ReadWithoutCycle(0xA000));

        // But RAM underneath has our value
        Assert.Equal(0x42, map.GetRam()[0xA000]);
    }

    [Fact]
    public void Banking_Port01_35_AllRamVisible()
    {
        var map = CreateMapWithRoms();

        // Write to RAM under ROM first
        map.WriteWithoutCycle(0xA000, 0x42);
        map.WriteWithoutCycle(0xE000, 0x43);

        // Bank out all ROMs: $01 = $35 (HIRAM=0, LORAM=1, CHAREN=0 -> all RAM + I/O)
        map.WriteWithoutCycle(0x01, 0x35);

        // Now should see RAM
        Assert.Equal(0x42, map.ReadWithoutCycle(0xA000));
        Assert.Equal(0x43, map.ReadWithoutCycle(0xE000));
    }

    [Fact]
    public void Banking_Port01_36_NoBasic_KernalVisible()
    {
        var map = CreateMapWithRoms();

        map.WriteWithoutCycle(0xA000, 0x42); // Write RAM under BASIC

        // $01 = $36: LORAM=0 (no BASIC), HIRAM=1 (KERNAL visible)
        map.WriteWithoutCycle(0x01, 0x36);

        Assert.Equal(0x42, map.ReadWithoutCycle(0xA000)); // RAM visible (no BASIC)
        Assert.Equal(0xEA, map.ReadWithoutCycle(0xE000)); // KERNAL still visible
    }

    [Fact]
    public void Banking_Port01_34_AllRam_NoIO()
    {
        var map = CreateMapWithRoms();

        map.WriteWithoutCycle(0xA000, 0x42);
        map.WriteWithoutCycle(0xD000, 0x44);
        map.WriteWithoutCycle(0xE000, 0x43);

        // $01 = $34: All RAM, no I/O
        map.WriteWithoutCycle(0x01, 0x34);

        Assert.Equal(0x42, map.ReadWithoutCycle(0xA000));
        Assert.Equal(0x44, map.ReadWithoutCycle(0xD000)); // RAM, not I/O
        Assert.Equal(0x43, map.ReadWithoutCycle(0xE000));
    }

    [Fact]
    public void Address00_DataDirection()
    {
        var map = new C64MemoryMap();
        map.WriteWithoutCycle(0x00, 0x2F);
        Assert.Equal(0x2F, map.ReadWithoutCycle(0x00));
    }

    [Fact]
    public void Address01_DataPort()
    {
        var map = new C64MemoryMap();
        map.WriteWithoutCycle(0x01, 0x37);
        Assert.Equal(0x37, map.ReadWithoutCycle(0x01));
    }

    [Fact]
    public void Reset_SetsBankingToDefault()
    {
        var map = CreateMapWithRoms();
        map.WriteWithoutCycle(0x01, 0x34); // All RAM
        map.Reset();

        // Need to reload ROMs after reset
        var basicRom = new byte[0x2000];
        for (int i = 0; i < basicRom.Length; i++) basicRom[i] = 0xBA;
        map.LoadRom("basic", basicRom);

        // Default banking restored - ROM visible again
        Assert.Equal(0xBA, map.ReadWithoutCycle(0xA000));
    }

    [Fact]
    public void LowRam_AlwaysAccessible()
    {
        var map = new C64MemoryMap();

        // Low RAM ($0002-$9FFF) always accessible regardless of banking
        map.WriteWithoutCycle(0x0800, 0x42);
        Assert.Equal(0x42, map.ReadWithoutCycle(0x0800));

        // Change banking
        map.WriteWithoutCycle(0x01, 0x34);
        Assert.Equal(0x42, map.ReadWithoutCycle(0x0800));
    }

    [Fact]
    public void Write_ToIoRegion_AlsoWritesToRam()
    {
        var map = new C64MemoryMap();
        // Default $01 = $37: I/O visible at $D000-$DFFF

        // Write to I/O region - should also write to RAM underneath
        map.WriteWithoutCycle(0xD000, 0x42);

        // Reading shows I/O register value
        Assert.Equal(0x42, map.ReadWithoutCycle(0xD000));

        // But RAM underneath ALSO has the value - verify by banking to all RAM
        map.WriteWithoutCycle(0x01, 0x34); // Bank to all RAM, no I/O
        Assert.Equal(0x42, map.ReadWithoutCycle(0xD000)); // Should read RAM value

        // Also verify via GetRam() - this is the CRITICAL C64 behavior
        Assert.Equal(0x42, map.GetRam()[0xD000]);
    }
}
