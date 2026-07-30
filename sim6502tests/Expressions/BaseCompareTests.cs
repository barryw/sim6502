using FluentAssertions;
using sim6502.Expressions;
using Xunit;

namespace sim6502tests.Expressions;

public class BaseCompareTests
{
    [Theory]
    [InlineData(5, 5, "==", true)]
    [InlineData(5, 6, "==", false)]
    [InlineData(5, 5, "eq", true)]
    [InlineData(5, 6, "eq", false)]
    [InlineData(5, 6, "!=", true)]
    [InlineData(5, 5, "!=", false)]
    [InlineData(5, 6, "<>", true)]
    [InlineData(5, 5, "<>", false)]
    [InlineData(5, 6, "ne", true)]
    [InlineData(5, 5, "ne", false)]
    [InlineData(6, 5, ">", true)]
    [InlineData(5, 5, ">", false)]
    [InlineData(5, 5, ">=", true)]
    [InlineData(4, 5, ">=", false)]
    [InlineData(4, 5, "<", true)]
    [InlineData(5, 5, "<", false)]
    [InlineData(5, 5, "<=", true)]
    [InlineData(6, 5, "<=", false)]
    [InlineData(4, 5, "lt", true)]
    [InlineData(5, 5, "lt", false)]
    [InlineData(6, 5, "gt", true)]
    [InlineData(5, 5, "gt", false)]
    public void CompareValues_EvaluatesEachOperator(int expected, int actual, string op, bool shouldPass)
    {
        var result = BaseCompare.CompareValues(expected, actual, op);

        result.ComparisonPassed.Should().Be(shouldPass);
        if (!shouldPass)
        {
            result.FailureMessage.Should().Be($"Expected {expected} {op} {actual}");
        }
        else
        {
            result.FailureMessage.Should().BeNull();
        }
    }

    [Fact]
    public void CompareValues_UnknownOperator_FailsClosed()
    {
        var result = BaseCompare.CompareValues(5, 5, "wat");

        result.ComparisonPassed.Should().BeFalse();
        result.FailureMessage.Should().Be("Expected 5 wat 5");
    }
}
