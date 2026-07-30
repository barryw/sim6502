using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using FluentAssertions;
using sim6502.Backend;
using sim6502.Errors;
using sim6502.Grammar;
using sim6502.Grammar.Generated;
using sim6502.Systems;
using Xunit;

namespace sim6502tests.Backend;

/// <summary>
/// Regression coverage for the suite-lifetime bug in SimBaseListener: ExitSuite
/// disposed the backend but never cleared it, so EnterSuite's `if (Backend ==
/// null)` guard saw a non-null (but disposed) backend on the second suite in a
/// file and reused it instead of building a fresh one. Harmless for the other
/// backends, whose Dispose() is a no-op or reconnects, but u64sim's Dispose()
/// deletes the Ultimate working-copy temp directory, so the second suite's
/// filesystem operations fail with "83,NO SUCH DIRECTORY".
///
/// This test drives the listener the same way the CLI does — with no backend
/// injected — so EnterSuite must construct one for each of the two suites.
/// </summary>
public class U64SimListenerTests : IDisposable
{
    private readonly string _fixture;

    public U64SimListenerTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64sim-listener-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_fixture, "data"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    private static SimBaseListener RunSuites(string source)
    {
        var collector = new ErrorCollector();
        collector.SetSource(source, "test-input");

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
            $"Grammar parse errors: {(collector.HasErrors ? ErrorRenderer.Render(collector) : "")}");

        // No Backend/U64SimConfig injected: the listener must build its own
        // backend per suite via EnterSuite, exactly like the real CLI does.
        var sbl = new SimBaseListener
        {
            BackendType = "u64sim",
            Errors = collector
        };

        new ParseTreeWalker().Walk(sbl, tree);
        return sbl;
    }

    [Fact]
    public void SecondSuiteInTheSameFile_GetsAFreshBackend_NotADisposedOne()
    {
        var source = """
        suites {
          suite("first") {
            system(c64)
            ultimate(fs_root = "__FSROOT__")
            test("a", "identify then chdir") {
              uci($01, $01)
              assert(uci_status("00,OK"), "identify ok")
              uci($01, $11, "/Usb0/data")
              assert(uci_status("00,OK"), "chdir ok")
            }
          }
          suite("second") {
            system(c64)
            ultimate(fs_root = "__FSROOT__")
            test("b", "chdir again in a second suite") {
              uci($01, $11, "/Usb0/data")
              assert(uci_status("00,OK"), "chdir ok in suite 2")
            }
          }
        }
        """.Replace("__FSROOT__", _fixture);

        var sbl = RunSuites(source);

        sbl.TotalSuitesPassed.Should().Be(2, "both suites should get a working backend");
        sbl.TotalSuitesFailed.Should().Be(0);
    }

    [Fact]
    public void U64SimBackend_SatisfiesTheUltimateSeam()
    {
        // uci() must work against any Ultimate-capable backend, not just u64sim.
        var config = new U64SimBackendConfig { FsRoot = _fixture };
        using var backend = new U64SimBackend(config, new C64MemoryMap());

        IUltimateBackend seam = backend;
        var (status, data) = seam.IssueUciCommand(new byte[] { 0x01, 0x01 });

        status.Should().Be("00,OK");
        data.Should().StartWith(new byte[] { 0x55, 0x4c });  // "UL"
    }
}
