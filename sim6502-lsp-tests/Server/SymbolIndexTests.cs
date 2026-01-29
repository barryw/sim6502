using sim6502_lsp.Server;
using Xunit;

namespace sim6502_lsp_tests.Server;

public class SymbolIndexTests
{
    [Fact]
    public void AddSymbol_CanBeRetrieved()
    {
        var index = new SymbolIndex();
        var uri = new Uri("file:///test.6502");

        index.AddSymbol(new SymbolInfo("myvar", 0x1000, uri, 5, 0, SymbolSource.Dsl));

        var symbol = index.GetSymbol("myvar");
        Assert.NotNull(symbol);
        Assert.Equal(0x1000, symbol.Address);
    }

    [Fact]
    public void GetSymbol_ReturnsNullForUnknown()
    {
        var index = new SymbolIndex();

        Assert.Null(index.GetSymbol("unknown"));
    }

    [Fact]
    public void GetSymbolsForDocument_ReturnsOnlyThatDocument()
    {
        var index = new SymbolIndex();
        var uri1 = new Uri("file:///test1.6502");
        var uri2 = new Uri("file:///test2.6502");

        index.AddSymbol(new SymbolInfo("sym1", 0x1000, uri1, 1, 0, SymbolSource.Dsl));
        index.AddSymbol(new SymbolInfo("sym2", 0x2000, uri2, 1, 0, SymbolSource.Dsl));

        var symbols = index.GetSymbolsForDocument(uri1).ToList();
        Assert.Single(symbols);
        Assert.Equal("sym1", symbols[0].Name);
    }

    [Fact]
    public void ClearDocument_RemovesOnlyThatDocumentsSymbols()
    {
        var index = new SymbolIndex();
        var uri1 = new Uri("file:///test1.6502");
        var uri2 = new Uri("file:///test2.6502");

        index.AddSymbol(new SymbolInfo("sym1", 0x1000, uri1, 1, 0, SymbolSource.Dsl));
        index.AddSymbol(new SymbolInfo("sym2", 0x2000, uri2, 1, 0, SymbolSource.Dsl));

        index.ClearDocument(uri1);

        Assert.Null(index.GetSymbol("sym1"));
        Assert.NotNull(index.GetSymbol("sym2"));
    }

    [Fact]
    public void GetAllSymbols_ReturnsAll()
    {
        var index = new SymbolIndex();
        var uri = new Uri("file:///test.6502");

        index.AddSymbol(new SymbolInfo("a", 0x1000, uri, 1, 0, SymbolSource.Dsl));
        index.AddSymbol(new SymbolInfo("b", 0x2000, uri, 2, 0, SymbolSource.SymFile));

        var all = index.GetAllSymbols().ToList();
        Assert.Equal(2, all.Count);
    }
}
