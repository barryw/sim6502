using FluentAssertions;
using sim6502.Grammar;
using Xunit;

namespace sim6502tests;

/// <summary>
/// Pins the BASIC error-detection rules. Was added 2026-04-26 after the
/// earlier `EndsWith(" Error")` matcher produced false positives that sent
/// the test author on a multi-hour goose chase debugging hardware that
/// wasn't actually broken. Each "false-positive screen" case below is a
/// real-world pattern that DID NOT contain an actual BASIC error but the
/// old matcher flagged anyway. Each "real failure" case is a screen that
/// DOES contain an EhBASIC program-failure message and MUST be flagged.
/// </summary>
public class BasicErrorScannerTests
{
    [Fact]
    public void FindProgramError_ReturnsNull_ForNullScreen()
    {
        BasicErrorScanner.FindProgramError(null).Should().BeNull();
    }

    [Fact]
    public void FindProgramError_ReturnsNull_ForEmptyScreen()
    {
        BasicErrorScanner.FindProgramError(new string[0]).Should().BeNull();
    }

    [Fact]
    public void FindProgramError_ReturnsNull_ForCleanReadyPrompt()
    {
        var screen = new[]
        {
            "EhBASIC NOVA 1.0",
            "",
            "Ready",
            "10 PRINT 42",
            "RUN",
            "42",
            "Ready",
            ""
        };
        BasicErrorScanner.FindProgramError(screen).Should().BeNull();
    }

    [Theory]
    [InlineData("Syntax Error in line 10")]
    [InlineData("Type Mismatch Error in line 20")]
    [InlineData("Division by Zero Error in line 99")]
    [InlineData("Out of Memory Error in line 1")]
    [InlineData("Undefined Statement Error in line 65535")]
    public void FindProgramError_DetectsRealBasicErrors(string errorLine)
    {
        var screen = new[]
        {
            "10 PRINT 1/0",
            "RUN",
            errorLine,
            "Ready"
        };
        BasicErrorScanner.FindProgramError(screen).Should().Be(errorLine);
    }

    [Fact]
    public void FindProgramError_ReturnsFirstMatch_WhenMultipleErrorLines()
    {
        var screen = new[]
        {
            "Syntax Error in line 10",
            "Type Mismatch Error in line 20"
        };
        BasicErrorScanner.FindProgramError(screen).Should().Be("Syntax Error in line 10");
    }

    [Fact]
    public void FindProgramError_TrimsTrailingWhitespace()
    {
        var screen = new[] { "Syntax Error in line 10                              " };
        BasicErrorScanner.FindProgramError(screen).Should().Be("Syntax Error in line 10");
    }

    // ---- regression cases — these are the patterns that the earlier
    //      `EndsWith(" Error")` matcher misfired on. They MUST stay null. ----

    [Theory]
    [InlineData("This is some User Error")]      // generic English ending in " Error"
    [InlineData("Boot Error")]                    // banner-style status
    [InlineData("Soft Error")]                    // hardware status
    [InlineData("? Type Mismatch  Error")]        // immediate-mode error WITHOUT line number
    [InlineData("Last Error")]                    // diagnostic header
    public void FindProgramError_IgnoresSuffixOnlyMatches(string spuriousLine)
    {
        var screen = new[]
        {
            spuriousLine,
            "Ready"
        };
        BasicErrorScanner.FindProgramError(screen).Should().BeNull(
            "the suffix-only matcher used to misfire on this — narrowed to " +
            "'Error in line N' on 2026-04-26 to eliminate false positives");
    }

    [Fact]
    public void FindProgramError_IgnoresNullLines()
    {
        var screen = new string[] { null, "Ready", null };
        BasicErrorScanner.FindProgramError(screen).Should().BeNull();
    }

    [Fact]
    public void FindProgramError_RequiresExactSpaceErrorSpaceInLineSpace()
    {
        // No space between "Error" and "in" — should not match.
        var screen = new[] { "Syntax Errorinline 10" };
        BasicErrorScanner.FindProgramError(screen).Should().BeNull();
    }

    [Fact]
    public void FindProgramError_DetectsErrorOnSameLineAsOtherText()
    {
        // EhBASIC may emit the error after BASIC's prompt or other text on
        // the same row depending on cursor position — the matcher just looks
        // for the substring " Error in line ".
        var screen = new[] { "Ready Syntax Error in line 5" };
        BasicErrorScanner.FindProgramError(screen)
            .Should().Be("Ready Syntax Error in line 5");
    }
}
