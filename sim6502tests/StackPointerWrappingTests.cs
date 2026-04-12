using FluentAssertions;
using sim6502.Proc;
using Xunit;

namespace sim6502tests;

public class StackPointerWrappingTests
{
    [Fact]
    public void StackPointer_WrapsCorrectly_WhenDecrementedBelowZero()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.StackPointer = 0x00;
        proc.StackPointer--;

        proc.StackPointer.Should().Be(0xFF,
            "SP should wrap from 0x00 to 0xFF on decrement");
    }

    [Fact]
    public void StackPointer_WrapsCorrectly_WhenIncrementedAboveFF()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.StackPointer = 0xFF;
        proc.StackPointer++;

        proc.StackPointer.Should().Be(0x00,
            "SP should wrap from 0xFF to 0x00 on increment");
    }

    [Fact]
    public void StackPointer_MasksLargeValues()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.StackPointer = 0x1FF;
        proc.StackPointer.Should().Be(0xFF);

        proc.StackPointer = 0x300;
        proc.StackPointer.Should().Be(0x00);

        proc.StackPointer = 0x44F6;
        proc.StackPointer.Should().Be(0xF6,
            "large values should be masked to low byte");
    }

    [Fact]
    public void StackPointer_MasksNegativeValues()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.StackPointer = -1;
        proc.StackPointer.Should().Be(0xFF);

        proc.StackPointer = -256;
        proc.StackPointer.Should().Be(0x00);
    }

    [Fact]
    public void Stack_DoesNotCorruptMemory_DuringLongLoop()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        // Place sentinel values in memory outside the stack region ($0100-$01FF)
        var sentinelAddr = 0x44F6;
        byte sentinelValue = 0xA5; // LDA zp opcode — the value from issue #5
        proc.WriteMemoryValueWithoutIncrement(sentinelAddr, sentinelValue);

        // Simulate 2000+ push/pull cycles (more than InitZobristTables' 1536)
        proc.StackPointer = 0xFF;
        for (var i = 0; i < 2000; i++)
        {
            // PHA: write to stack, decrement SP
            proc.WriteMemoryValueWithoutIncrement(proc.StackPointer + 0x100, (byte)(i & 0xFF));
            proc.StackPointer--;

            // PLA: increment SP, read from stack
            proc.StackPointer++;
            proc.ReadMemoryValueWithoutCycle(proc.StackPointer + 0x100);
        }

        proc.ReadMemoryValueWithoutCycle(sentinelAddr).Should().Be(sentinelValue,
            "stack operations must not corrupt memory outside the $0100-$01FF stack region");
    }

    [Fact]
    public void JSR_RTS_DeepNesting_DoesNotCorruptMemory()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        // Place sentinel in memory that could be hit by corrupted SP
        proc.WriteMemoryValueWithoutIncrement(0x44F6, 0xA5);

        // Simulate deeply nested JSR stack usage:
        // Each JSR pushes 2 bytes, so 128 nested JSRs = 256 stack bytes = full wrap
        proc.StackPointer = 0xFF;
        for (var i = 0; i < 130; i++)
        {
            // Simulate JSR: push return address high then low byte
            proc.WriteMemoryValueWithoutIncrement(proc.StackPointer + 0x100, (byte)((0x0200 >> 8) & 0xFF));
            proc.StackPointer--;
            proc.WriteMemoryValueWithoutIncrement(proc.StackPointer + 0x100, (byte)(0x0200 & 0xFF));
            proc.StackPointer--;
        }

        // SP should have wrapped properly and stayed in range
        proc.StackPointer.Should().BeInRange(0x00, 0xFF);

        // Memory outside stack must be untouched
        proc.ReadMemoryValueWithoutCycle(0x44F6).Should().Be(0xA5,
            "deeply nested JSRs must not corrupt memory outside the stack region");
    }
}
