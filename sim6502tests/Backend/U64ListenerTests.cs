using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using FluentAssertions;
using sim6502.Backend;
using sim6502.Errors;
using sim6502.Grammar;
using sim6502.Grammar.Generated;
using sim6502.Systems.Ultimate;
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

    // The suite above never calls anything that touches Backend, which is
    // exactly why it missed the real defect: SimBaseListener.ResetTest() calls
    // Backend.ResetCycleCount() unconditionally before every test() block, and
    // U64Backend.ResetCycleCount() used to throw NotSupportedException -- so
    // --backend u64 died on the first test() in any suite, before any UCI
    // traffic. Walk a suite that actually contains a test() body to exercise
    // EnterTestFunction -> ResetTest -> Backend.ResetCycleCount() end to end.
    private const string TestSource = """
        suites {
          suite("u64 wiring") {
            system(c64)

            test("dos-identify", "the DOS target reports its version") {
              uci($01, $01)
              assert(uci_status("00,OK"), "IDENTIFY succeeded")
            }
          }
        }
        """;

    private static IParseTree ParseTestSuite()
    {
        var collector = new ErrorCollector();
        collector.SetSource(TestSource, "test-input");

        var inputStream = new AntlrInputStream(TestSource);
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
    public void EnterTestFunction_U64Backend_RunsATestBlockToCompletion()
    {
        var tree = ParseTestSuite();
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "u64listener-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        try
        {
            using var fs = new UltimateFileSystem(fixtureRoot);
            var dos = new UltimateDosTarget(fs, "ULTIMATE-II DOS V1.2");
            var connection = new FakeU64Connection(0, (1, dos));

            // SimBaseListener.EnterSuite only constructs a Backend when one
            // isn't already set, so injecting one here (as U64BackendTests
            // does) keeps this test off the network entirely --
            // FakeU64Connection is an in-memory IU64Connection.
            using var backend = new U64Backend(new U64BackendConfig { Host = "192.0.2.1" }, connection);
            var sbl = new SimBaseListener
            {
                BackendType = "u64",
                Backend = backend
            };

            new ParseTreeWalker().Walk(sbl, tree);

            // ExitSuite (which fires before Walk returns, since this file has
            // one suite) rolls the per-suite TotalTestsPassed/TotalTestsFailed
            // into TotalSuitesPassed/TotalSuitesFailed and resets the
            // per-suite counters to zero -- so those, not TotalTestsPassed,
            // are what survive to be asserted on here. TotalSuitesPassed == 1
            // with TotalSuitesFailed == 0 only happens when the suite's one
            // test() block ran and passed, i.e. EnterTestFunction ->
            // ResetTest -> Backend.ResetCycleCount() did not throw and the
            // uci()/assert() pair inside actually executed and matched.
            sbl.TotalSuitesPassed.Should().Be(1);
            sbl.TotalSuitesFailed.Should().Be(0);
        }
        finally
        {
            Directory.Delete(fixtureRoot, true);
        }
    }
}
