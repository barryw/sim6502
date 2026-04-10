using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using FluentAssertions;
using sim6502.Backend;
using sim6502.Errors;
using sim6502.Grammar;
using sim6502.Grammar.Generated;
using Xunit;

namespace sim6502tests.Backend;

public class NovaVmListenerTests
{
    /// <summary>
    /// Parse and walk a test suite file using the NovaVM backend with a mock connection.
    /// Returns the listener and mock for inspection.
    /// </summary>
    private static (SimBaseListener listener, MockNovaVmConnection mock) RunSuite(string source)
    {
        var mock = new MockNovaVmConnection();
        // Default responses for common commands
        mock.SetDefaultResponse("""{"ok":true}""");

        var config = new NovaVmBackendConfig();
        var backend = new NovaVmBackend(config, mock);

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

        var sbl = new SimBaseListener
        {
            BackendType = "novavm",
            NovaVmConfig = config,
            Errors = collector
        };

        // Inject pre-built backend directly
        sbl.Backend = backend;

        var walker = new ParseTreeWalker();
        walker.Walk(sbl, tree);

        return (sbl, mock);
    }

    /// <summary>
    /// Helper: configure mock for screen reads used by screen_contains/screen_line.
    /// </summary>
    private static void SetupScreenResponse(MockNovaVmConnection mock, params string[] lines)
    {
        var linesJson = string.Join(",", lines.Select(l => $"\"{l}\""));
        mock.SetResponse("read_screen", $$"""{"ok":true,"lines":[{{linesJson}}],"cursor_x":0,"cursor_y":0}""");
    }

    private static void SetupWaitReady(MockNovaVmConnection mock, bool found = true)
    {
        mock.SetResponse("wait_ready",
            found ? """{"ok":true,"found":true,"row":0}""" : """{"ok":true,"found":false}""");
    }

    /// <summary>
    /// Set up mock responses for a test that uses run().
    /// Includes cold_start wait_ready (auto-called by EnterTestFunction) and
    /// read_screen responses for run() polling.
    /// </summary>
    private static void SetupRunScreenPolling(MockNovaVmConnection mock)
    {
        SetupWaitReady(mock);                           // cold_start() in EnterTestFunction
        SetupScreenResponse(mock, "", "", "");           // pre-RUN: no Ready
        SetupScreenResponse(mock, "Ready", "", "");      // post-RUN: Ready appears
    }

    /// <summary>
    /// Set up get_cursor responses for basic() cursor polling.
    /// Each basic() call needs 2 responses: before (y=N) and after (y=N+1).
    /// </summary>
    private static void SetupCursorPolling(MockNovaVmConnection mock, int numBasicLines)
    {
        for (int i = 0; i < numBasicLines; i++)
        {
            mock.SetResponse("get_cursor",
                $"{{\"ok\":true,\"x\":0,\"y\":{i}}}");      // before: y=i
            mock.SetResponse("get_cursor",
                $"{{\"ok\":true,\"x\":0,\"y\":{i + 1}}}");  // after: y=i+1
        }
    }

    // ── Grammar parsing ──

    [Fact]
    public void Grammar_NovaVmCommands_ParseWithoutErrors()
    {
        var source = """
        suites {
          suite("Parse Test") {
            test("all-commands", "all NovaVM commands") {
              basic("10 PRINT X")
              run()
              run(wait = "DONE")
              wait_ready()
              wait_ready(timeout = 3000)
              wait_text("LOADING")
              wait_text("LOADING", timeout = 5000)
              send_key("ENTER")
              cold_start()
              pause()
              resume()
              pause(cycles_count = 100)
              pause(screen = "READY")
              pause(watch = $A010, value = $00)
            }
          }
        }
        """;

        // Just need to parse — we set up enough mocks for it to walk without crashing
        var mock = new MockNovaVmConnection();
        mock.SetDefaultResponse("""{"ok":true}""");
        // wait_ready responses for run(), wait_ready(), cold_start()
        for (int i = 0; i < 10; i++)
            mock.SetResponse("wait_ready", """{"ok":true,"found":true,"row":0}""");

        var config = new NovaVmBackendConfig();
        var backend = new NovaVmBackend(config, mock);

        var collector = new ErrorCollector();
        collector.SetSource(source, "test");
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
            $"Parse errors: {(collector.HasErrors ? ErrorRenderer.Render(collector) : "")}");
    }

    [Fact]
    public void Grammar_ScreenAssertions_ParseWithoutErrors()
    {
        var source = """
        suites {
          suite("Screen Parse") {
            test("screen-funcs", "screen functions in assertions") {
              basic("10 PRINT 42")
              run()
              assert(screen_contains("42"), "found it")
              assert(screen_line(0, "Ready"), "first line")
            }
          }
        }
        """;

        var collector = new ErrorCollector();
        collector.SetSource(source, "test");
        var inputStream = new AntlrInputStream(source);
        var lexer = new sim6502Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));
        var tokens = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokens) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));
        parser.suites();

        collector.HasErrors.Should().BeFalse(
            $"Parse errors: {(collector.HasErrors ? ErrorRenderer.Render(collector) : "")}");
    }

    // ── basic() ──

    [Fact]
    public void Basic_SendsTextThenEnter()
    {
        var source = """
        suites {
          suite("Basic Test") {
            test("basic-cmd", "sends text") {
              basic("10 PRINT X")
              ; Need a run() to satisfy _didJsr
              run()
            }
          }
        }
        """;

        var (sbl, mock) = RunWithWaitReady(source);

        var typeCalls = mock.GetCallsFor("type_text");
        typeCalls.Should().HaveCountGreaterThanOrEqualTo(1);
        // First type_text should be the BASIC line
        typeCalls[0].Args!["text"].Should().Be("10 PRINT X");

        // Should send ENTER after the BASIC line
        var keyCalls = mock.GetCallsFor("send_key");
        keyCalls.Should().Contain(c => (string)c.Args!["key"] == "ENTER");
    }

    // ── run() ──

    [Fact]
    public void Run_SendsRunAndPollsScreen()
    {
        var source = """
        suites {
          suite("Run Test") {
            test("run-cmd", "sends RUN") {
              run()
            }
          }
        }
        """;

        var (sbl, mock) = RunWithWaitReady(source);

        // Should type "RUN"
        var typeCalls = mock.GetCallsFor("type_text");
        typeCalls.Should().Contain(c => (string)c.Args!["text"] == "RUN");

        // Should send ENTER
        mock.WasCalled("send_key").Should().BeTrue();

        // Should poll screen to detect new Ready (not use wait_ready)
        mock.WasCalled("read_screen").Should().BeTrue();
    }

    [Fact]
    public void Run_WithWaitParam_PollsForCustomText()
    {
        var source = """
        suites {
          suite("Run Wait Test") {
            test("run-wait", "waits for custom text") {
              run(wait = "DONE")
            }
          }
        }
        """;

        // Need screen polling for "DONE" instead of "Ready"
        var mock = new MockNovaVmConnection();
        mock.SetDefaultResponse("""{"ok":true,"found":true,"row":0}""");
        SetupWaitReady(mock);                        // cold_start() in EnterTestFunction
        SetupScreenResponse(mock, "", "", "");        // pre-RUN: no DONE
        SetupScreenResponse(mock, "DONE", "", "");    // post-RUN: DONE appears

        var config = new NovaVmBackendConfig();
        var backend = new NovaVmBackend(config, mock);
        var collector = new sim6502.Errors.ErrorCollector();
        var inputStream = new AntlrInputStream(source);
        collector.SetSource(source, "test");
        var lexer = new sim6502Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));
        var tokens = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokens) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));
        var tree = parser.suites();

        var sbl = new SimBaseListener
        {
            BackendType = "novavm",
            NovaVmConfig = config,
            Errors = collector,
            Backend = backend
        };
        new ParseTreeWalker().Walk(sbl, tree);

        // Should poll read_screen looking for "DONE"
        mock.WasCalled("read_screen").Should().BeTrue();
    }

    [Fact]
    public void Run_SetsDidJsr()
    {
        var source = """
        suites {
          suite("Run JSR Test") {
            test("run-jsr", "run sets didJsr") {
              run()
              assert(peekbyte($0000) == peekbyte($0000), "dummy assertion")
            }
          }
        }
        """;

        var mock = new MockNovaVmConnection();
        mock.SetDefaultResponse("""{"ok":true}""");
        SetupRunScreenPolling(mock);
        mock.SetResponse("peek", """{"ok":true,"value":0}""");
        mock.SetResponse("peek", """{"ok":true,"value":0}""");

        var config = new NovaVmBackendConfig();
        var backend = new NovaVmBackend(config, mock);

        var collector = new ErrorCollector();
        var inputStream = new AntlrInputStream(source);
        collector.SetSource(source, "test");
        var lexer = new sim6502Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));
        var tokens = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokens) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));
        var tree = parser.suites();

        var sbl = new SimBaseListener
        {
            BackendType = "novavm",
            NovaVmConfig = config,
            Errors = collector,
            Backend = backend
        };

        new ParseTreeWalker().Walk(sbl, tree);

        // Test should pass (not fail with "No JSR encountered")
        sbl.TotalSuitesFailed.Should().Be(0, "test suite should pass");
    }

    // ── wait_ready() / wait_text() ──

    [Fact]
    public void WaitReady_SendsWaitReadyCommand()
    {
        var source = """
        suites {
          suite("WaitReady Test") {
            test("wr", "wait ready") {
              run()
              wait_ready()
            }
          }
        }
        """;

        var (sbl, mock) = RunWithWaitReady(source, waitReadyCount: 1);

        var waitCalls = mock.GetCallsFor("wait_ready");
        // 2 calls: 1 from auto cold_start + 1 from wait_ready() DSL command
        waitCalls.Should().HaveCount(2);
    }

    [Fact]
    public void WaitText_SendsCustomText()
    {
        var source = """
        suites {
          suite("WaitText Test") {
            test("wt", "wait text") {
              run()
              wait_text("LOADING")
            }
          }
        }
        """;

        var (sbl, mock) = RunWithWaitReady(source, waitReadyCount: 2);

        var waitCalls = mock.GetCallsFor("wait_ready");
        waitCalls.Should().Contain(c => (string)c.Args!["text"] == "LOADING");
    }

    [Fact]
    public void WaitText_WithTimeout_PassesTimeout()
    {
        var source = """
        suites {
          suite("WaitText Timeout") {
            test("wt-to", "wait with timeout") {
              run()
              wait_text("LOADING", timeout = 8000)
            }
          }
        }
        """;

        var (sbl, mock) = RunWithWaitReady(source, waitReadyCount: 2);

        var waitCalls = mock.GetCallsFor("wait_ready");
        waitCalls.Should().Contain(c =>
            (string)c.Args!["text"] == "LOADING" &&
            (int)c.Args!["timeout_ms"] == 8000);
    }

    // ── send_key() ──

    [Fact]
    public void SendKey_SendsKeyCommand()
    {
        var source = """
        suites {
          suite("SendKey Test") {
            test("sk", "send key") {
              run()
              send_key("CTRL-C")
            }
          }
        }
        """;

        var (sbl, mock) = RunWithWaitReady(source);

        mock.GetCallsFor("send_key").Should().Contain(c => (string)c.Args!["key"] == "CTRL-C");
    }

    // ── cold_start() ──

    [Fact]
    public void ColdStart_SendsColdStartCommand()
    {
        var source = """
        suites {
          suite("ColdStart Test") {
            test("cs", "cold start") {
              run()
              cold_start()
            }
          }
        }
        """;

        var (sbl, mock) = RunWithWaitReady(source, waitReadyCount: 2);

        mock.WasCalled("cold_start").Should().BeTrue();
    }

    // ── pause() / resume() ──

    [Fact]
    public void Pause_NoArgs_SendsPause()
    {
        var source = """
        suites {
          suite("Pause Test") {
            test("p", "pause") {
              run()
              pause()
            }
          }
        }
        """;

        var (sbl, mock) = RunWithWaitReady(source);

        mock.WasCalled("dbg_pause").Should().BeTrue();
    }

    [Fact]
    public void Resume_SendsResume()
    {
        var source = """
        suites {
          suite("Resume Test") {
            test("r", "resume") {
              run()
              pause()
              resume()
            }
          }
        }
        """;

        var (sbl, mock) = RunWithWaitReady(source);

        mock.WasCalled("dbg_resume").Should().BeTrue();
    }

    [Fact]
    public void Pause_WithCycles_CallsRunCycles()
    {
        var source = """
        suites {
          suite("PauseCycles Test") {
            test("pc", "pause cycles") {
              run()
              pause(cycles_count = 3)
            }
          }
        }
        """;

        var (sbl, mock) = RunWithWaitReady(source);

        // RunCycles now uses native run_cycles command
        var calls = mock.GetCallsFor("run_cycles");
        calls.Should().HaveCount(1);
        calls[0].Args!["cycles"].Should().Be(3);
    }

    [Fact]
    public void Pause_WithScreen_WaitsThenPauses()
    {
        var source = """
        suites {
          suite("PauseScreen Test") {
            test("ps", "pause screen") {
              run()
              pause(screen = "LOADED")
            }
          }
        }
        """;

        var (sbl, mock) = RunWithWaitReady(source, waitReadyCount: 2);

        // Should wait for "LOADED" text
        mock.GetCallsFor("wait_ready").Should().Contain(c => (string)c.Args!["text"] == "LOADED");
        // Then pause
        mock.WasCalled("dbg_pause").Should().BeTrue();
    }

    [Fact]
    public void Pause_WithWatch_PollsMemoryThenPauses()
    {
        var source = """
        suites {
          suite("PauseWatch Test") {
            test("pw", "pause watch") {
              run()
              pause(watch = $A010, value = $00)
            }
          }
        }
        """;

        var mock = new MockNovaVmConnection();
        mock.SetDefaultResponse("""{"ok":true}""");
        SetupRunScreenPolling(mock);
        // WaitForMemory now uses native watch command
        mock.SetResponse("watch", """{"ok":true,"matched":true,"address":40976,"expected":0,"actual":0}""");

        var config = new NovaVmBackendConfig();
        var backend = new NovaVmBackend(config, mock);
        var collector = new ErrorCollector();
        var inputStream = new AntlrInputStream(source);
        collector.SetSource(source, "test");
        var lexer = new sim6502Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));
        var tokens = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokens) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));
        var tree = parser.suites();

        var sbl = new SimBaseListener
        {
            BackendType = "novavm",
            NovaVmConfig = config,
            Errors = collector,
            Backend = backend
        };
        new ParseTreeWalker().Walk(sbl, tree);

        mock.WasCalled("watch").Should().BeTrue();
    }

    // ── screen_contains() ──

    [Fact]
    public void ScreenContains_Found_PassesAssertion()
    {
        var source = """
        suites {
          suite("ScreenContains Test") {
            test("sc", "screen contains") {
              run()
              assert(screen_contains("HELLO"), "found hello")
            }
          }
        }
        """;

        var mock = new MockNovaVmConnection();
        mock.SetDefaultResponse("""{"ok":true}""");
        SetupRunScreenPolling(mock);
        // For screen_contains assertion
        SetupScreenResponse(mock, "HELLO WORLD", "Ready", "");

        var config = new NovaVmBackendConfig();
        var backend = new NovaVmBackend(config, mock);
        var collector = new ErrorCollector();
        var inputStream = new AntlrInputStream(source);
        collector.SetSource(source, "test");
        var lexer = new sim6502Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));
        var tokens = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokens) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));
        var tree = parser.suites();

        var sbl = new SimBaseListener
        {
            BackendType = "novavm",
            NovaVmConfig = config,
            Errors = collector,
            Backend = backend
        };
        new ParseTreeWalker().Walk(sbl, tree);

        sbl.TotalSuitesFailed.Should().Be(0, "test suite should pass");
    }

    [Fact]
    public void ScreenContains_NotFound_FailsAssertion()
    {
        var source = """
        suites {
          suite("ScreenContains Fail Test") {
            test("sc-fail", "screen not contains") {
              run()
              assert(screen_contains("MISSING"), "should fail")
            }
          }
        }
        """;

        var mock = new MockNovaVmConnection();
        mock.SetDefaultResponse("""{"ok":true}""");
        SetupRunScreenPolling(mock);
        // For screen_contains assertion
        SetupScreenResponse(mock, "Ready", "", "");

        var config = new NovaVmBackendConfig();
        var backend = new NovaVmBackend(config, mock);
        var collector = new ErrorCollector();
        var inputStream = new AntlrInputStream(source);
        collector.SetSource(source, "test");
        var lexer = new sim6502Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));
        var tokens = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokens) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));
        var tree = parser.suites();

        var sbl = new SimBaseListener
        {
            BackendType = "novavm",
            NovaVmConfig = config,
            Errors = collector,
            Backend = backend
        };
        new ParseTreeWalker().Walk(sbl, tree);

        sbl.TotalSuitesFailed.Should().Be(1, "test suite should fail");
    }

    // ── screen_line() ──

    [Fact]
    public void ScreenLine_MatchFound_PassesAssertion()
    {
        var source = """
        suites {
          suite("ScreenLine Test") {
            test("sl", "screen line") {
              run()
              assert(screen_line(0, "Ready"), "first line is Ready")
            }
          }
        }
        """;

        var mock = new MockNovaVmConnection();
        mock.SetDefaultResponse("""{"ok":true}""");
        SetupRunScreenPolling(mock);
        mock.SetResponse("read_line", """{"ok":true,"row":0,"text":"Ready"}""");

        var config = new NovaVmBackendConfig();
        var backend = new NovaVmBackend(config, mock);
        var collector = new ErrorCollector();
        var inputStream = new AntlrInputStream(source);
        collector.SetSource(source, "test");
        var lexer = new sim6502Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));
        var tokens = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokens) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));
        var tree = parser.suites();

        var sbl = new SimBaseListener
        {
            BackendType = "novavm",
            NovaVmConfig = config,
            Errors = collector,
            Backend = backend
        };
        new ParseTreeWalker().Walk(sbl, tree);

        sbl.TotalSuitesFailed.Should().Be(0, "test suite should pass");
    }

    [Fact]
    public void ScreenLine_NoMatch_FailsAssertion()
    {
        var source = """
        suites {
          suite("ScreenLine Fail Test") {
            test("sl-fail", "screen line no match") {
              run()
              assert(screen_line(0, "MISSING"), "should fail")
            }
          }
        }
        """;

        var mock = new MockNovaVmConnection();
        mock.SetDefaultResponse("""{"ok":true}""");
        SetupRunScreenPolling(mock);
        mock.SetResponse("read_line", """{"ok":true,"row":0,"text":"Ready"}""");

        var config = new NovaVmBackendConfig();
        var backend = new NovaVmBackend(config, mock);
        var collector = new ErrorCollector();
        var inputStream = new AntlrInputStream(source);
        collector.SetSource(source, "test");
        var lexer = new sim6502Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));
        var tokens = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokens) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));
        var tree = parser.suites();

        var sbl = new SimBaseListener
        {
            BackendType = "novavm",
            NovaVmConfig = config,
            Errors = collector,
            Backend = backend
        };
        new ParseTreeWalker().Walk(sbl, tree);

        sbl.TotalSuitesFailed.Should().Be(1, "test suite should fail");
    }

    // ── Wrong backend error ──

    [Fact]
    public void BasicOnSimBackend_Throws()
    {
        var source = """
        suites {
          suite("Wrong Backend") {
            test("wb", "basic on sim backend") {
              basic("10 PRINT X")
            }
          }
        }
        """;

        var collector = new ErrorCollector();
        var inputStream = new AntlrInputStream(source);
        collector.SetSource(source, "test");
        var lexer = new sim6502Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));
        var tokens = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokens) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));
        var tree = parser.suites();

        // Use default sim backend
        var sbl = new SimBaseListener
        {
            BackendType = "sim",
            Errors = collector
        };

        var act = () => new ParseTreeWalker().Walk(sbl, tree);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*requires a high-level backend*")
            .WithMessage("*novavm*");
    }

    // ── Helper to run with pre-configured wait_ready responses ──

    private static (SimBaseListener sbl, MockNovaVmConnection mock) RunWithWaitReady(
        string source, string waitText = "Ready", int waitReadyCount = 1)
    {
        var mock = new MockNovaVmConnection();
        mock.SetDefaultResponse("""{"ok":true,"found":true,"row":0}""");
        // cold_start() auto-called by EnterTestFunction needs wait_ready
        SetupWaitReady(mock);
        // basic() calls WaitForText for each line + additional wait_ready for DSL commands
        for (int i = 0; i < waitReadyCount + 10; i++)
            SetupWaitReady(mock);
        // run() reads screen before RUN (0 Ready), then polls until new Ready appears
        SetupScreenResponse(mock, "", "", "");
        SetupScreenResponse(mock, "Ready", "", "");

        var config = new NovaVmBackendConfig();
        var backend = new NovaVmBackend(config, mock);

        var collector = new ErrorCollector();
        var inputStream = new AntlrInputStream(source);
        collector.SetSource(source, "test");
        var lexer = new sim6502Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));
        var tokens = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokens) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));
        var tree = parser.suites();

        var sbl = new SimBaseListener
        {
            BackendType = "novavm",
            NovaVmConfig = config,
            Errors = collector,
            Backend = backend
        };

        new ParseTreeWalker().Walk(sbl, tree);
        return (sbl, mock);
    }
}
