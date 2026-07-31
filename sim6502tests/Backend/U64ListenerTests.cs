using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using FluentAssertions;
using sim6502.Backend;
using sim6502.Errors;
using sim6502.Grammar;
using sim6502.Grammar.Generated;
using Xunit;

namespace sim6502tests.Backend;

/// <summary>
/// Pins the CLI-to-backend wiring for the u64 backend: SimBaseListener.U64Config
/// must reach BackendFactory.Create as its 7th argument from EnterSuite.
/// Sim6502CliTests covers CLI-option-to-config mapping (BuildBackendConfigs) and
/// BackendFactoryTests covers config-to-backend construction, but neither one
/// covers the listener actually threading U64Config through EnterSuite into the
/// factory call — that hop had no test. Deleting either the
/// `U64Config = backendConfigs.U64` assignment in Sim6502CLI.RunTests or the 7th
/// argument in SimBaseListener.EnterSuite's BackendFactory.Create call left every
/// existing test passing.
///
/// The suite body below never calls anything that touches Backend: load() with a
/// file that doesn't exist returns out of ExitLoadFunction right after the
/// File.Exists check, before it would ever reach Backend, but it still satisfies
/// the grammar's requirement that a suite body contain at least one of
/// testFunction|symbolsFunction|loadFunction|romDeclaration|setupBlock. That
/// keeps this test free of network I/O even against a real (non-mocked)
/// U64Backend — confirmed separately that its constructor (via
/// U64RestConnection) only builds an HttpClient and a base URL string, issuing
/// no request.
/// </summary>
public class U64ListenerTests
{
    private const string Source = """
        suites {
          suite("u64 wiring") {
            system(c64)
            load("does-not-exist.prg")
          }
        }
        """;

    private static IParseTree Parse()
    {
        var collector = new ErrorCollector();
        collector.SetSource(Source, "test-input");

        var inputStream = new AntlrInputStream(Source);
        var lexer = new sim6502Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));

        var tokens = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokens) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));

        var tree = parser.suites();
        collector.HasErrors.Should().BeFalse(
            $"Grammar parse errors: {(collector.HasErrors ? ErrorRenderer.Render(collector) : "")}");

        return tree;
    }

    [Fact]
    public void EnterSuite_U64WithConfig_ConstructsU64Backend()
    {
        var tree = Parse();
        var sbl = new SimBaseListener
        {
            BackendType = "u64",
            // TEST-NET-1 (RFC 5737): guaranteed non-routable, so nothing could
            // accidentally connect even if this path did attempt I/O. It
            // doesn't (see class remarks), but this is a second guard for free.
            U64Config = new U64BackendConfig { Host = "192.0.2.1" }
        };

        new ParseTreeWalker().Walk(sbl, tree);

        sbl.Backend.Should().BeOfType<U64Backend>();
    }

    [Fact]
    public void EnterSuite_U64WithoutConfig_ThrowsNamingTheFix()
    {
        var tree = Parse();
        var sbl = new SimBaseListener
        {
            BackendType = "u64",
            U64Config = null
        };

        var act = () => new ParseTreeWalker().Walk(sbl, tree);

        act.Should().Throw<ArgumentException>().WithMessage("*--u64-host*");
    }
}
