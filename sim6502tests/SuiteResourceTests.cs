using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using FluentAssertions;
using sim6502.Errors;
using sim6502.Grammar;
using sim6502.Grammar.Generated;
using Xunit;

namespace sim6502tests;

/// <summary>
/// How a suite finds the files it names, and what happens when it names one that is not there.
/// </summary>
public class SuiteResourceTests
{
    /// <summary>
    /// A suite that references a symbol without loading a symbol file gets an error, not a
    /// crash. <c>Symbols</c> used to start null, so the first symbol reference threw a
    /// NullReferenceException out of the parse walk and took the whole run with it — the
    /// user saw a stack trace instead of "undefined symbol".
    /// </summary>
    [Fact]
    public void ReferencingASymbolWithNoSymbolFile_ReportsAnErrorRatherThanThrowing()
    {
        var collector = Walk("""
            suites {
              suite("s") {
                test("t", "d") {
                  [SomeSymbol] = $01
                }
              }
            }
            """);

        collector.HasErrors.Should().BeTrue();
        collector.Errors.Should().Contain(e => e.Message.Contains("SomeSymbol"));
    }

    /// <summary>
    /// A relative path resolves against the suite file, so a suite and the program it loads
    /// can sit in one directory and be run from anywhere.
    /// </summary>
    [Fact]
    public void RelativePaths_ResolveAgainstTheSuiteFile()
    {
        var directory = Directory.CreateTempSubdirectory("sim6502-resources");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "prog.sym"), ".label Answer=$002a\n");

            var listener = new SimBaseListener { SuiteDirectory = directory.FullName };
            Walk("""
                suites {
                  suite("s") {
                    symbols("prog.sym")
                  }
                }
                """, listener);

            listener.Symbols.SymbolExists("Answer").Should().BeTrue(
                "the symbol file sits beside the suite and should have been found");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The same for <c>load(...)</c>, which is checked for existence at the moment it is
    /// parsed. Resolving only where the resource was later constructed left that check
    /// looking at the unresolved name, so a suite run from a different working directory
    /// reported "file not found" for a program sitting right beside it — which is exactly
    /// what happened inside the Docker image, where the working directory is not /code.
    /// </summary>
    [Fact]
    public void RelativeLoadPaths_ResolveAgainstTheSuiteFile()
    {
        var directory = Directory.CreateTempSubdirectory("sim6502-load");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "prog.prg"), [0x00, 0x10, 0xEA, 0x60]);

            var listener = new SimBaseListener { SuiteDirectory = directory.FullName };
            var collector = Walk("""
                suites {
                  suite("s") {
                    load("prog.prg", strip_header = true)
                  }
                }
                """, listener);

            collector.HasErrors.Should().BeFalse(
                "the program sits beside the suite and should have been found: " +
                string.Join("; ", collector.Errors.Select(e => e.Message)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Without a suite directory, a relative path still resolves against the working
    /// directory — which is what every existing invocation relies on.
    /// </summary>
    [Fact]
    public void RelativePaths_StillResolveAgainstTheWorkingDirectory()
    {
        var listener = new SimBaseListener();
        Walk("""
            suites {
              suite("s") {
                symbols("TestPrograms/include_me_full.sym")
              }
            }
            """, listener);

        listener.Symbols.SymbolCount.Should().BeGreaterThan(0);
    }

    private static ErrorCollector Walk(string source, SimBaseListener? listener = null)
    {
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

        listener ??= new SimBaseListener();
        listener.Errors = collector;
        new ParseTreeWalker().Walk(listener, tree);
        return collector;
    }
}
