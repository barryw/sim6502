using FluentAssertions;
using sim6502.Errors;
using Xunit;

namespace sim6502tests;

public class SuggestionEngineTests
{
    [Theory]
    [InlineData("jrs", "jsr")]
    [InlineData("asert", "assert")]
    [InlineData("memfil", "memfill")]
    [InlineData("peekbyt", "peekbyte")]
    [InlineData("peekwrd", "peekword")]
    [InlineData("memdmp", "memdump")]
    [InlineData("setip", "setup")]
    public void SuggestKeyword_ReturnsSuggestion_ForTypos(string input, string expected)
    {
        var suggestion = SuggestionEngine.SuggestKeyword(input);
        suggestion.Should().Be(expected);
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("completely_different")]
    public void SuggestKeyword_ReturnsNull_WhenNoCloseMatch(string input)
    {
        var suggestion = SuggestionEngine.SuggestKeyword(input);
        suggestion.Should().BeNull();
    }

    [Fact]
    public void SuggestKeyword_ReturnsRegister_ForSingleLetter()
    {
        // "a" matches register "A" with distance 0
        var suggestion = SuggestionEngine.SuggestKeyword("a");
        suggestion.Should().Be("A");
    }

    [Fact]
    public void SuggestSymbol_ReturnsSuggestion_ForTypos()
    {
        var symbols = new[] { "FillMemory", "ClearScreen", "SetColor", "DrawSprite" };

        var suggestion = SuggestionEngine.SuggestSymbol("FillMmory", symbols);
        suggestion.Should().Be("FillMemory");

        suggestion = SuggestionEngine.SuggestSymbol("ClearScren", symbols);
        suggestion.Should().Be("ClearScreen");
    }

    [Fact]
    public void SuggestSymbol_ReturnsNull_WhenNoCloseMatch()
    {
        var symbols = new[] { "FillMemory", "ClearScreen", "SetColor" };

        var suggestion = SuggestionEngine.SuggestSymbol("CompletelyDifferent", symbols);
        suggestion.Should().BeNull();
    }

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "abc", 0)]
    [InlineData("abc", "abd", 1)]
    [InlineData("abc", "abcd", 1)]
    [InlineData("abc", "ab", 1)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("saturday", "sunday", 3)]
    public void LevenshteinDistance_CalculatesCorrectly(string source, string target, int expected)
    {
        var distance = SuggestionEngine.LevenshteinDistance(source, target);
        distance.Should().Be(expected);
    }
}

public class ErrorCollectorTests
{
    [Fact]
    public void SetSource_StoresSourceLinesAndFilePath()
    {
        var collector = new ErrorCollector();
        collector.SetSource("line1\nline2\nline3", "/test/file.txt");

        var lines = collector.SourceLines;
        lines.Should().HaveCount(3);
        lines[0].Should().Be("line1");
        lines[1].Should().Be("line2");
        lines[2].Should().Be("line3");

        collector.FilePath.Should().Be("/test/file.txt");
    }

    [Fact]
    public void AddError_AddsErrorToCollection()
    {
        var collector = new ErrorCollector();
        collector.SetSource("test source", "test.txt");

        collector.AddError(ErrorPhase.Parser, 1, 5, 3, "test error", "hint");

        collector.HasErrors.Should().BeTrue();
        collector.HasWarnings.Should().BeFalse();

        var errors = collector.Errors;
        errors.Should().HaveCount(1);
        errors[0].Phase.Should().Be(ErrorPhase.Parser);
        errors[0].Line.Should().Be(1);
        errors[0].Column.Should().Be(5);
        errors[0].Length.Should().Be(3);
        errors[0].Message.Should().Be("test error");
        errors[0].Hint.Should().Be("hint");
        errors[0].Severity.Should().Be(ErrorSeverity.Error);
    }

    [Fact]
    public void AddWarning_AddsWarningToCollection()
    {
        var collector = new ErrorCollector();
        collector.SetSource("test source", "test.txt");

        collector.AddWarning(ErrorPhase.Semantic, 2, 1, 4, "test warning");

        collector.HasWarnings.Should().BeTrue();
        collector.HasErrors.Should().BeFalse();

        var errors = collector.Errors;
        errors.Should().HaveCount(1);
        errors[0].Severity.Should().Be(ErrorSeverity.Warning);
    }

    [Fact]
    public void MultipleErrors_AreCollected()
    {
        var collector = new ErrorCollector();
        collector.SetSource("line1\nline2\nline3", "test.txt");

        collector.AddError(ErrorPhase.Lexer, 1, 1, 1, "error 1");
        collector.AddError(ErrorPhase.Parser, 2, 5, 2, "error 2");
        collector.AddWarning(ErrorPhase.Semantic, 3, 1, 3, "warning 1");

        var errors = collector.Errors;
        errors.Should().HaveCount(3);

        collector.HasErrors.Should().BeTrue();
        collector.HasWarnings.Should().BeTrue();
    }

    [Fact]
    public void Clear_RemovesAllErrors()
    {
        var collector = new ErrorCollector();
        collector.SetSource("test", "test.txt");
        collector.AddError(ErrorPhase.Parser, 1, 1, 1, "error");

        collector.Clear();

        collector.HasErrors.Should().BeFalse();
        collector.Errors.Should().BeEmpty();
    }
}

public class ErrorRendererTests
{
    [Fact]
    public void Render_ReturnsEmptyString_WhenNoErrors()
    {
        var collector = new ErrorCollector();
        collector.SetSource("test", "test.txt");

        var output = ErrorRenderer.Render(collector);

        output.Should().BeEmpty();
    }

    [Fact]
    public void Render_IncludesErrorLocation()
    {
        var collector = new ErrorCollector();
        collector.SetSource("line 1\nline with error\nline 3", "test.txt");
        collector.AddError(ErrorPhase.Parser, 2, 6, 4, "unexpected token");

        var output = ErrorRenderer.Render(collector);

        output.Should().Contain("test.txt:2:6");
        output.Should().Contain("unexpected token");
    }

    [Fact]
    public void Render_IncludesSourceContext()
    {
        var collector = new ErrorCollector();
        collector.SetSource("line 1\nline with error\nline 3", "test.txt");
        collector.AddError(ErrorPhase.Parser, 2, 6, 4, "test error");

        var output = ErrorRenderer.Render(collector);

        output.Should().Contain("line 1");
        output.Should().Contain("line with error");
        output.Should().Contain("line 3");
    }

    [Fact]
    public void Render_IncludesHintWhenProvided()
    {
        var collector = new ErrorCollector();
        collector.SetSource("test line", "test.txt");
        collector.AddError(ErrorPhase.Parser, 1, 1, 3, "error message", "Did you mean 'xyz'?");

        var output = ErrorRenderer.Render(collector);

        output.Should().Contain("Hint: Did you mean 'xyz'?");
    }

    [Fact]
    public void Render_IncludesSummary()
    {
        var collector = new ErrorCollector();
        collector.SetSource("line1\nline2", "test.txt");
        collector.AddError(ErrorPhase.Parser, 1, 1, 1, "error 1");
        collector.AddError(ErrorPhase.Parser, 2, 1, 1, "error 2");

        var output = ErrorRenderer.Render(collector);

        output.Should().Contain("Found 2 errors");
    }

    [Fact]
    public void Render_HandlesWarningsSeparately()
    {
        var collector = new ErrorCollector();
        collector.SetSource("test", "test.txt");
        collector.AddWarning(ErrorPhase.Semantic, 1, 1, 1, "warning message");

        var output = ErrorRenderer.Render(collector);

        output.Should().Contain("Warning");
    }
}

public class SimErrorTests
{
    [Fact]
    public void SimError_StoresAllProperties()
    {
        var error = new SimError(
            ErrorSeverity.Error,
            ErrorPhase.Semantic,
            "/path/to/file.txt",
            10,
            5,
            3,
            "test message",
            "test hint"
        );

        error.Severity.Should().Be(ErrorSeverity.Error);
        error.Phase.Should().Be(ErrorPhase.Semantic);
        error.FilePath.Should().Be("/path/to/file.txt");
        error.Line.Should().Be(10);
        error.Column.Should().Be(5);
        error.Length.Should().Be(3);
        error.Message.Should().Be("test message");
        error.Hint.Should().Be("test hint");
    }

    [Fact]
    public void SimError_HintCanBeNull()
    {
        var error = new SimError(
            ErrorSeverity.Warning,
            ErrorPhase.Lexer,
            "file.txt",
            1,
            1,
            1,
            "message",
            null
        );

        error.Hint.Should().BeNull();
    }
}
