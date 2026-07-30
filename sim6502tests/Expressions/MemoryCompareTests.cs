using FluentAssertions;
using sim6502.Expressions;
using Xunit;

namespace sim6502tests.Expressions;

public class MemoryCompareTests
{
    [Fact]
    public void MemoryCmp_MatchingRegions_Passes()
    {
        var backend = new FakeExecutionBackend();
        backend.WriteByte(0x1000, 0x11);
        backend.WriteByte(0x1001, 0x22);
        backend.WriteByte(0x2000, 0x11);
        backend.WriteByte(0x2001, 0x22);

        var cmp = new MemoryCompare(backend);
        var result = cmp.MemoryCmp(0x1000, 0x2000, 2);

        result.ComparisonPassed.Should().BeTrue();
        result.FailureMessage.Should().BeNull();
    }

    [Fact]
    public void MemoryCmp_Mismatch_FailsWithMessageAndStopsAtFirstDifference()
    {
        var backend = new FakeExecutionBackend();
        backend.WriteByte(0x1000, 0x11);
        backend.WriteByte(0x1001, 0x99); // mismatch here
        backend.WriteByte(0x2000, 0x11);
        backend.WriteByte(0x2001, 0x22);

        var cmp = new MemoryCompare(backend);
        var result = cmp.MemoryCmp(0x1000, 0x2000, 2);

        result.ComparisonPassed.Should().BeFalse();
        result.FailureMessage.Should().Be(
            "Expected values at memory locations 4097 and 8193 to match, but 4097 contains 153 and 8193 contains 34");
    }

    [Fact]
    public void MemoryCmp_ZeroCount_Passes()
    {
        var backend = new FakeExecutionBackend();
        var cmp = new MemoryCompare(backend);

        var result = cmp.MemoryCmp(0x1000, 0x2000, 0);

        result.ComparisonPassed.Should().BeTrue();
    }

    [Fact]
    public void MemoryChk_AllMatchValue_Passes()
    {
        var backend = new FakeExecutionBackend();
        backend.WriteByte(0x1000, 0xAA);
        backend.WriteByte(0x1001, 0xAA);

        var cmp = new MemoryCompare(backend);
        var result = cmp.MemoryChk(0x1000, 2, 0xAA);

        result.ComparisonPassed.Should().BeTrue();
        result.FailureMessage.Should().BeNull();
    }

    [Fact]
    public void MemoryChk_Mismatch_FailsWithMessage()
    {
        var backend = new FakeExecutionBackend();
        backend.WriteByte(0x1000, 0xAA);
        backend.WriteByte(0x1001, 0xBB);

        var cmp = new MemoryCompare(backend);
        var result = cmp.MemoryChk(0x1000, 2, 0xAA);

        result.ComparisonPassed.Should().BeFalse();
        result.FailureMessage.Should().Be(
            "Expected value at memory location 4097 to be 170, but the actual value was 187");
    }

    [Fact]
    public void MemoryVal_ByteValue_UsesReadByte()
    {
        var backend = new FakeExecutionBackend();
        backend.WriteByte(0x1000, 0x42);

        var cmp = new MemoryCompare(backend);
        var result = cmp.MemoryVal(0x1000, 0x42);

        result.ComparisonPassed.Should().BeTrue();
        result.FailureMessage.Should().BeNull();
    }

    [Fact]
    public void MemoryVal_WordValue_UsesReadWord()
    {
        var backend = new FakeExecutionBackend();
        backend.WriteWord(0x1000, 0x1234);

        var cmp = new MemoryCompare(backend);
        var result = cmp.MemoryVal(0x1000, 0x1234);

        result.ComparisonPassed.Should().BeTrue();
    }

    [Fact]
    public void MemoryVal_Mismatch_FailsWithMessageIncludingOperator()
    {
        var backend = new FakeExecutionBackend();
        backend.WriteByte(0x1000, 0x42); // actual = 66

        var cmp = new MemoryCompare(backend);
        var result = cmp.MemoryVal(0x1000, 0x10, ">"); // 16 > 66 is false

        result.ComparisonPassed.Should().BeFalse();
        result.FailureMessage.Should().Be(
            "Expected the value in memory location 4096 to be > 16, but the actual value was 66");
    }

    [Fact]
    public void MemoryVal_DefaultOperator_IsEquality()
    {
        var backend = new FakeExecutionBackend();
        backend.WriteByte(0x1000, 0x99);

        var cmp = new MemoryCompare(backend);
        var result = cmp.MemoryVal(0x1000, 0x42);

        result.ComparisonPassed.Should().BeFalse();
        result.FailureMessage.Should().Be(
            "Expected the value in memory location 4096 to be == 66, but the actual value was 153");
    }
}
