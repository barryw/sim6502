// sim6502-lsp-tests/Handlers/CompletionHandlerTests.cs
using sim6502_lsp.Handlers;
using Xunit;

namespace sim6502_lsp_tests.Handlers;

public class CompletionHandlerTests
{
    [Fact]
    public void GetCompletions_ReturnsKeywords()
    {
        var handler = new CompletionProvider();

        var completions = handler.GetCompletions("", 0, 0);

        Assert.Contains(completions, c => c.Label == "suites");
        Assert.Contains(completions, c => c.Label == "suite");
        Assert.Contains(completions, c => c.Label == "test");
        Assert.Contains(completions, c => c.Label == "assert");
    }

    [Fact]
    public void GetCompletions_ReturnsRegisters()
    {
        var handler = new CompletionProvider();

        var completions = handler.GetCompletions("", 0, 0);

        Assert.Contains(completions, c => c.Label == "a");
        Assert.Contains(completions, c => c.Label == "x");
        Assert.Contains(completions, c => c.Label == "y");
    }

    [Fact]
    public void GetCompletions_ReturnsFlags()
    {
        var handler = new CompletionProvider();

        var completions = handler.GetCompletions("", 0, 0);

        Assert.Contains(completions, c => c.Label == "c");
        Assert.Contains(completions, c => c.Label == "n");
        Assert.Contains(completions, c => c.Label == "z");
    }

    [Fact]
    public void GetCompletions_ReturnsSystemTypes()
    {
        var handler = new CompletionProvider();

        var completions = handler.GetCompletions("", 0, 0);

        Assert.Contains(completions, c => c.Label == "c64");
        Assert.Contains(completions, c => c.Label == "generic_6502");
        Assert.Contains(completions, c => c.Label == "generic_6510");
        Assert.Contains(completions, c => c.Label == "generic_65c02");
    }

    [Fact]
    public void GetCompletions_ReturnsBuiltinFunctions()
    {
        var handler = new CompletionProvider();

        var completions = handler.GetCompletions("", 0, 0);

        Assert.Contains(completions, c => c.Label == "peekbyte");
        Assert.Contains(completions, c => c.Label == "peekword");
        Assert.Contains(completions, c => c.Label == "memcmp");
        Assert.Contains(completions, c => c.Label == "memchk");
    }
}
