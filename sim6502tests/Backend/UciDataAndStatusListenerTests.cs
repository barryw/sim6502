using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using FluentAssertions;
using sim6502.Errors;
using sim6502.Grammar;
using sim6502.Grammar.Generated;
using Xunit;

namespace sim6502tests.Backend;

/// <summary>
/// Listener-level coverage for uci_data(n) and the uci_status(...) *value* path
/// (ExitUciStatusFunctionValue) — e.g. `assert(uci_status("00,OK") == true, ...)`
/// used inside an expression, as opposed to the direct `assert(uci_status(...), ...)`
/// comparison form already covered by <see cref="U64SimListenerTests"/>.
///
/// Drives the real u64sim backend (system(c64) + ultimate(fs_root = ...)) through
/// the parser/listener pipeline the same way the CLI does. u64sim is a
/// self-contained simulated device — there's no external process/connection to
/// mock, so (unlike NovaVmListenerTests) these tests need only a throwaway
/// filesystem root, not a mock connection. That root is a fresh temp directory
/// per test (same approach as U64SimListenerTests), which is simpler and more
/// robust than resolving the checked-in Fixtures/usb0 relative to whatever the
/// test runner's working directory happens to be.
/// </summary>
public class UciDataAndStatusListenerTests : IDisposable
{
    private readonly string _fixture;

    public UciDataAndStatusListenerTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "uci-data-listener-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixture);
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    /// <summary>
    /// Wraps a single test's statements in the minimal suite/test boilerplate
    /// and walks it with a real (unmocked) u64sim backend, exactly like the CLI.
    /// </summary>
    private SimBaseListener RunSuite(string testBody)
    {
        var source = ("""
            suites {
              suite("uci coverage") {
                system(c64)
                ultimate(fs_root = "__FSROOT__")
                test("t", "uci_data/uci_status coverage") {
            __TESTBODY__
                }
              }
            }
            """).Replace("__FSROOT__", _fixture).Replace("__TESTBODY__", testBody);

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

        // No Backend/U64SimConfig injected: EnterSuite builds a real U64SimBackend
        // from the ultimate(fs_root = ...) declaration, same as the real CLI.
        var sbl = new SimBaseListener
        {
            BackendType = "u64sim",
            Errors = collector
        };

        new ParseTreeWalker().Walk(sbl, tree);
        return sbl;
    }

    // ── uci_data(n) — case 1: correct byte from a known response ──

    [Fact]
    public void UciData_ReturnsCorrectResponseBytes()
    {
        // The DOS IDENTIFY reply (target $01, command $01) is the ASCII bytes of
        // U64SimBackendConfig.DosVersion, "ULTIMATE-II DOS V1.2": 'U' ($55) then
        // 'L' ($4c). Same expectation as example/ultimate.suite's dos-identify test.
        var sbl = RunSuite("""
                  uci($01, $01)
                  assert(uci_data(0) == $55, "first byte is 'U'")
                  assert(uci_data(1) == $4c, "second byte is 'L'")
            """);

        sbl.TotalSuitesFailed.Should().Be(0, "both uci_data() reads should match the IDENTIFY reply");
        sbl.TotalSuitesPassed.Should().Be(1);
    }

    // ── uci_data(n) — case 2: index beyond the response length ──

    [Fact]
    public void UciData_IndexOutOfRange_FailsAssertionInsteadOfThrowing()
    {
        // "ULTIMATE-II DOS V1.2" is 20 bytes, so index 20 is one past the end.
        // ReadUciData() must fail the assertion (naming the actual response
        // length in its message) rather than let an IndexOutOfRangeException
        // escape or silently return 0.
        Action act = () =>
        {
            var sbl = RunSuite("""
                      uci($01, $01)
                      assert(uci_data(20) == $00, "reading past the end of the response must fail")
                """);

            sbl.TotalSuitesFailed.Should().Be(1, "index 20 is out of range for a 20-byte response");
        };

        act.Should().NotThrow();
    }

    // ── uci_data(n) — case 3: called before any uci() in the test ──

    [Fact]
    public void UciData_CalledBeforeAnyUci_FailsAssertionInsteadOfThrowing()
    {
        // No uci() call at all: _lastUciData is still the empty array reset by
        // EnterTestFunction, and _uciCalled is false. ReadUciData() must fail the
        // assertion with a clear "before any uci()" message rather than silently
        // returning 0 (which would let a bogus `== $00` comparison pass) or
        // throwing.
        Action act = () =>
        {
            var sbl = RunSuite("""
                      assert(uci_data(0) == $00, "uci_data() before uci() must fail, not silently read an empty response")
                """);

            sbl.TotalSuitesFailed.Should().Be(1, "uci_data() ran before any uci() command in this test");
        };

        act.Should().NotThrow();
    }

    // ── uci_status(...) as an expression value — case 4 ──

    [Fact]
    public void UciStatus_UsedAsExpressionValue_ComparesCorrectly()
    {
        // uci_status("00,OK") used as a value inside `== true` goes through
        // boolFunction -> uciStatusFunctionValue -> ExitUciStatusFunctionValue,
        // not the direct assert(uci_status(...), ...) comparison form
        // (ExitUciStatusCheck) already covered by U64SimListenerTests.
        var sbl = RunSuite("""
                  uci($01, $01)
                  assert(uci_status("00,OK") == true, "status value path reports OK")
            """);

        sbl.TotalSuitesFailed.Should().Be(0);
        sbl.TotalSuitesPassed.Should().Be(1);
    }

    // ── uci_status(...) value path called before any uci() — case 5 ──

    [Fact]
    public void UciStatus_ValuePathCalledBeforeAnyUci_FailsAssertionInsteadOfThrowing()
    {
        Action act = () =>
        {
            var sbl = RunSuite("""
                      assert(uci_status("00,OK") == true, "uci_status() before uci() must fail")
                """);

            sbl.TotalSuitesFailed.Should().Be(1, "uci_status() ran before any uci() command in this test");
        };

        act.Should().NotThrow();
    }
}
