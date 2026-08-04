using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using FluentAssertions;
using sim6502.Errors;
using sim6502.Grammar;
using sim6502.Grammar.Generated;
using sim6502.Utilities;
using Xunit;

namespace sim6502tests;

/// <summary>
/// Arithmetic in a suite means what it says.
/// </summary>
/// <remarks>
/// ANTLR gives left-recursive alternatives descending precedence in the order the grammar
/// lists them, and that list used to run backwards: bitwise OR bound tightest and division
/// loosest, so <c>2 + 3 * 4</c> parsed as <c>(2 + 3) * 4</c> and evaluated to 20. It was
/// not academic — <c>example/tests.6502</c> computed a timer's address as
/// <c>base + 0 + index * 8</c>, which became <c>base * 8</c> and read from $19E0 instead
/// of $033C, so the assertions read zeroes and the test had presumably never passed.
/// </remarks>
public class ExpressionPrecedenceTests
{
    [Theory]
    [InlineData("2 + 3 * 4", 14)]          // not 20
    [InlineData("10 - 4 / 2", 8)]          // not 3
    [InlineData("3 * 4 + 2", 14)]
    [InlineData("2 + 3 + 4", 9)]
    [InlineData("(2 + 3) * 4", 20)]        // parentheses still win
    [InlineData("$10 + $00 + $01 * 8", 24)]      // the shape example/tests.6502 uses
    public void Arithmetic_FollowsConventionalPrecedence(string expression, int expected)
    {
        Evaluate(expression).Should().Be(expected);
    }

    [Theory]
    [InlineData("1 + 2 & 3", 3)]           // additive binds tighter than AND
    [InlineData("1 | 6 & 3", 3)]           // AND binds tighter than OR
    [InlineData("1 ^ 3 & 1", 0)]           // AND binds tighter than XOR
    public void Bitwise_BindsLooserThanArithmetic(string expression, int expected)
    {
        Evaluate(expression).Should().Be(expected);
    }

    /// <summary>
    /// Walks a one-statement suite and returns what the expression evaluated to.
    /// </summary>
    /// <remarks>
    /// The result comes back through the accumulator, so cases must stay within a byte.
    /// </remarks>
    private static int Evaluate(string expression)
    {
        var source = $$"""
            suites {
              suite("s") {
                test("t", "d") {
                  a = {{expression}}
                }
              }
            }
            """;

        var collector = new ErrorCollector();
        collector.SetSource(source, "inline");

        var lexer = new sim6502Lexer(new AntlrInputStream(source));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));

        var parser = new sim6502Parser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));
        parser.BuildParseTree = true;
        var tree = parser.suites();

        collector.HasErrors.Should().BeFalse($"'{expression}' must parse");

        var sbl = new SimBaseListener();
        new ParseTreeWalker().Walk(sbl, tree);

        // The register assignment is the only statement, so the accumulator holds the result.
        return sbl.Backend.GetRegister("a");
    }
}
