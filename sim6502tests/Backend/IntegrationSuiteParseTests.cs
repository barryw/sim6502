using Antlr4.Runtime;
using FluentAssertions;
using sim6502.Errors;
using sim6502.Grammar;
using sim6502.Grammar.Generated;
using Xunit;

namespace sim6502tests.Backend;

/// <summary>
/// Validates that all e6502 integration test suite files parse without grammar errors.
/// These files require a running emulator to execute, but we verify they're syntactically valid.
/// </summary>
public class IntegrationSuiteParseTests
{
    private const string IntegrationDir = "../../../../e6502/tests/integration";

    private static void AssertParses(string filename)
    {
        var path = Path.Combine(IntegrationDir, filename);
        if (!File.Exists(path))
        {
            // Try relative to the sim6502 repo root
            path = Path.Combine("../../../..", "e6502", "tests", "integration", filename);
        }

        // If file doesn't exist in CI, skip gracefully
        if (!File.Exists(path))
        {
            // Use absolute path as last resort
            path = $"/Users/barry/Git/e6502/tests/integration/{filename}";
        }

        File.Exists(path).Should().BeTrue($"Integration test file '{filename}' should exist at {path}");

        var source = File.ReadAllText(path);
        var collector = new ErrorCollector();
        collector.SetSource(source, filename);

        var inputStream = new AntlrInputStream(source);
        var lexer = new sim6502Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));

        var tokens = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokens) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));

        var tree = parser.suites();

        collector.HasErrors.Should().BeFalse(
            $"Parse errors in {filename}: {(collector.HasErrors ? ErrorRenderer.Render(collector) : "")}");

        tree.suite().Should().NotBeEmpty($"{filename} should contain at least one suite");
    }

    [Fact] public void VgcSuite_ParsesWithoutErrors() => AssertParses("vgc.6502");
    [Fact] public void BlitterSuite_ParsesWithoutErrors() => AssertParses("blitter.6502");
    [Fact] public void CopperSuite_ParsesWithoutErrors() => AssertParses("copper.6502");
    [Fact] public void XramSuite_ParsesWithoutErrors() => AssertParses("xram.6502");
    [Fact] public void TilesSuite_ParsesWithoutErrors() => AssertParses("tiles.6502");
    [Fact] public void TextSuite_ParsesWithoutErrors() => AssertParses("text.6502");
    [Fact] public void SpritesSuite_ParsesWithoutErrors() => AssertParses("sprites.6502");
    [Fact] public void MathSuite_ParsesWithoutErrors() => AssertParses("math.6502");
    [Fact] public void DmaSuite_ParsesWithoutErrors() => AssertParses("dma.6502");
    [Fact] public void FileIoSuite_ParsesWithoutErrors() => AssertParses("fileio.6502");
}
