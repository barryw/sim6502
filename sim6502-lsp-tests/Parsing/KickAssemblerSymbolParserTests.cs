using sim6502_lsp.Parsing;
using Xunit;

namespace sim6502_lsp_tests.Parsing;

public class KickAssemblerSymbolParserTests
{
    [Fact]
    public void ParseLine_LabelFormat_ReturnsSymbol()
    {
        var parser = new KickAssemblerSymbolParser();

        var symbol = parser.ParseLine(".label screenRam=$0400");

        Assert.NotNull(symbol);
        Assert.Equal("screenRam", symbol.Name);
        Assert.Equal(0x0400, symbol.Address);
    }

    [Fact]
    public void ParseLine_ConstFormat_ReturnsSymbol()
    {
        var parser = new KickAssemblerSymbolParser();

        var symbol = parser.ParseLine(".const BORDER_COLOR=$d020");

        Assert.NotNull(symbol);
        Assert.Equal("BORDER_COLOR", symbol.Name);
        Assert.Equal(0xD020, symbol.Address);
    }

    [Fact]
    public void ParseLine_Comment_ReturnsNull()
    {
        var parser = new KickAssemblerSymbolParser();

        var symbol = parser.ParseLine("// this is a comment");

        Assert.Null(symbol);
    }

    [Fact]
    public void ParseLine_EmptyLine_ReturnsNull()
    {
        var parser = new KickAssemblerSymbolParser();

        Assert.Null(parser.ParseLine(""));
        Assert.Null(parser.ParseLine("   "));
    }

    [Fact]
    public void ParseFile_MultipleSymbols_ReturnsAll()
    {
        var parser = new KickAssemblerSymbolParser();
        var content = @".label main=$0810
.label loop=$0820
.const SCREEN=$0400";

        var symbols = parser.ParseContent(content).ToList();

        Assert.Equal(3, symbols.Count);
        Assert.Contains(symbols, s => s.Name == "main" && s.Address == 0x0810);
        Assert.Contains(symbols, s => s.Name == "loop" && s.Address == 0x0820);
        Assert.Contains(symbols, s => s.Name == "SCREEN" && s.Address == 0x0400);
    }

    [Fact]
    public void ParseLine_DecimalAddress_ReturnsSymbol()
    {
        var parser = new KickAssemblerSymbolParser();

        var symbol = parser.ParseLine(".label start=2048");

        Assert.NotNull(symbol);
        Assert.Equal("start", symbol.Name);
        Assert.Equal(2048, symbol.Address);
    }
}
