using System;
using FluentAssertions;
using sim6502.Systems;
using Xunit;

namespace sim6502tests.Systems;

/// <summary>
/// Covers the cycle-counted Read/Write entry points, LoadRom no-op, and LoadProgram
/// (including its overflow guard) on Generic6510MemoryMap - the paths not already
/// exercised by Generic6510MemoryMapTests, which only uses the *WithoutCycle helpers.
/// </summary>
public class Generic6510MemoryMapCycleTests
{
    [Fact]
    public void Read_IncrementsCycleCountAndReturnsValue()
    {
        var map = new Generic6510MemoryMap();
        map.WriteWithoutCycle(0x1000, 0x55);
        var cycleCount = 0;
        map.IncrementCycleCount = () => cycleCount++;

        var value = map.Read(0x1000);

        value.Should().Be(0x55);
        cycleCount.Should().Be(1);
    }

    [Fact]
    public void Write_IncrementsCycleCountAndStoresValue()
    {
        var map = new Generic6510MemoryMap();
        var cycleCount = 0;
        map.IncrementCycleCount = () => cycleCount++;

        map.Write(0x1000, 0x77);

        cycleCount.Should().Be(1);
        map.ReadWithoutCycle(0x1000).Should().Be(0x77);
    }

    [Fact]
    public void LoadRom_IsIgnored()
    {
        var map = new Generic6510MemoryMap();

        var act = () => map.LoadRom("kernal", new byte[] { 0x01, 0x02 });

        act.Should().NotThrow();
    }

    [Fact]
    public void LoadProgram_WritesBytesAtAddress()
    {
        var map = new Generic6510MemoryMap();
        var program = new byte[] { 0xA9, 0x42, 0x60 };

        map.LoadProgram(0x0600, program);

        map.ReadWithoutCycle(0x0600).Should().Be(0xA9);
        map.ReadWithoutCycle(0x0601).Should().Be(0x42);
        map.ReadWithoutCycle(0x0602).Should().Be(0x60);
    }

    [Fact]
    public void LoadProgram_ExceedingAddressSpace_Throws()
    {
        var map = new Generic6510MemoryMap();
        var program = new byte[10];

        var act = () => map.LoadProgram(0xFFFF, program);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*exceeds 64KB address space*");
    }
}
