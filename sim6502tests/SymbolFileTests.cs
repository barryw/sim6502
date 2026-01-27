using FluentAssertions;
using sim6502.Utilities;
using Xunit;

namespace sim6502tests;

public class SymbolFileTests
{
    [Fact]
    public void TestAddSymbols()
    {
        var symfile = ".label MyLabel=$0801\n.label YourLabel=$c000\n  .label OurLabel=49152";
        var sf = new SymbolFile(symfile);

        sf.SymbolToAddress("MyLabel").Should().Be(2049);
        sf.SymbolToAddress("YourLabel").Should().Be(49152);
        sf.SymbolToAddress("OurLabel").Should().Be(49152);
    }

    [Fact]
    public void TestNamespaces()
    {
        var symfile =
            ".label NonNamespacedLabel=$400\n.namespace kernal {\n  .label NamespacedLabel=$ffff\n}\n.label AnotherNonNamespacedLabel=$0800";
        var sf = new SymbolFile(symfile);

        sf.SymbolToAddress("NonNamespacedLabel").Should().Be(1024);
        sf.SymbolToAddress("kernal.NamespacedLabel").Should().Be(65535);
        sf.SymbolToAddress("AnotherNonNamespacedLabel").Should().Be(2048);
    }

    [Fact]
    public void TestLookupByAddress()
    {
        var symfile =
            ".label NonNamespacedLabel=$400\n.namespace kernal {\n  .label NamespacedLabel=$ffff\n}\n.label AnotherNonNamespacedLabel=$0800";
        var sf = new SymbolFile(symfile);

        sf.AddressToSymbol(1024).Should().Be("NonNamespacedLabel");
        sf.AddressToSymbol(65535).Should().Be("kernal.NamespacedLabel");
        sf.AddressToSymbol(2048).Should().Be("AnotherNonNamespacedLabel");

        sf.AddressToSymbol(1025).Should().Be("$401");
        sf.AddressToSymbol(1025, false).Should().Be("1025");
    }
}
