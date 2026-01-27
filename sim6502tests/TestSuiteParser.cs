using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using FluentAssertions;
using sim6502.Grammar;
using sim6502.Grammar.Generated;
using sim6502.Utilities;
using Xunit;

namespace sim6502tests;

public class TestSuiteParser
{
    private static sim6502Parser.SuitesContext GetContext(string test)
    {
        var afs = new AntlrFileStream(test);
        var lexer = new sim6502Lexer(afs);
        var tokens = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener());
        parser.BuildParseTree = true;
        return parser.suites();
    }

    [Fact]
    public void TestSuite1()
    {
        var symbols = new Dictionary<string, int>
        {
            { "MySymbol", 0xa000 },
            { "Loc1", 0xc000 },
            { "Loc2", 0x80 }
        };

        var symbolFile = new SymbolFile(symbols);

        var tree = GetContext("GrammarTests/test-1.txt");

        var walker = new ParseTreeWalker();
        var sbl = new SimBaseListener();

        sbl.Symbols = symbolFile;

        walker.Walk(sbl, tree);

        sbl.Proc.ReadMemoryValueWithoutCycle(0x80).Should().Be(0xd0);
        sbl.Proc.ReadMemoryWordWithoutCycle(0xc000).Should().Be(0xabcd);
        sbl.Proc.ReadMemoryWordWithoutCycle(0xc002).Should().Be(0xdcba);
        sbl.Proc.ReadMemoryValueWithoutCycle(0x81).Should().Be(0x0d);
    }

    [Fact]
    public void TestSuite2()
    {
        var symbols = new Dictionary<string, int> { { "Val1", 0x11 }, { "Val2", 0x22 }, { "Val3", 0xff } };
        var symbolFile = new SymbolFile(symbols);

        var tree = GetContext("GrammarTests/test-2.txt");

        var walker = new ParseTreeWalker();
        var sbl = new SimBaseListener { Symbols = symbolFile };

        walker.Walk(sbl, tree);

        sbl.Proc.XRegister.Should().Be(0x11);
        sbl.Proc.Accumulator.Should().Be(0x22);
        sbl.Proc.YRegister.Should().Be(0xff);
    }

    [Fact]
    public void TestSuite3()
    {
        var symbols = new Dictionary<string, int>
        {
            { "Val1", 0x11 },
            { "Val2", 0x22 },
            { "Val3", 0xff },
            { "Loc1", 0xd020 },
            { "Loc2", 0xd021 },
            { "Loc3", 0xd022 }
        };

        var symbolFile = new SymbolFile(symbols);

        var tree = GetContext("GrammarTests/test-3.txt");

        var walker = new ParseTreeWalker();
        var sbl = new SimBaseListener { Symbols = symbolFile };

        walker.Walk(sbl, tree);

        sbl.Proc.ReadMemoryValueWithoutCycle(0xd020).Should().Be(0x11);
        sbl.Proc.ReadMemoryValueWithoutCycle(0xd021).Should().Be(0x22);
        sbl.Proc.ReadMemoryValueWithoutCycle(0xd022).Should().Be(0xff);
    }

    [Fact]
    public void TestSuite4()
    {
        var symbols = new Dictionary<string, int> { { "FALSE", 0x00 } };

        var symbolFile = new SymbolFile(symbols);
        var tree = GetContext("GrammarTests/test-4.txt");

        var walker = new ParseTreeWalker();
        var sbl = new SimBaseListener { Symbols = symbolFile };

        walker.Walk(sbl, tree);

        sbl.Proc.CarryFlag.Should().BeTrue();
        sbl.Proc.NegativeFlag.Should().BeFalse();
        sbl.Proc.ZeroFlag.Should().BeTrue();
        sbl.Proc.OverflowFlag.Should().BeFalse();
        sbl.Proc.DecimalFlag.Should().BeFalse();
    }

    [Fact]
    public void TestSuite5()
    {
        var tree = GetContext("GrammarTests/test-5.txt");

        var walker = new ParseTreeWalker();
        var sbl = new SimBaseListener();

        walker.Walk(sbl, tree);
    }

    [Fact]
    public void TestSuite6()
    {
        var tree = GetContext("GrammarTests/test-6.txt");

        var walker = new ParseTreeWalker();
        var sbl = new SimBaseListener();

        walker.Walk(sbl, tree);
    }

    [Fact]
    public void TestSuite7()
    {
        var tree = GetContext("GrammarTests/test-7.txt");

        var walker = new ParseTreeWalker();
        var sbl = new SimBaseListener();

        walker.Walk(sbl, tree);
    }

    [Fact]
    public void TestSuite8()
    {
        var symbols = new Dictionary<string, int>
        {
            { "Loc1", 0xd020 }
        };

        var symbolFile = new SymbolFile(symbols);

        var tree = GetContext("GrammarTests/test-8.txt");

        var walker = new ParseTreeWalker();
        var sbl = new SimBaseListener { Symbols = symbolFile };

        walker.Walk(sbl, tree);

        sbl.Proc.ReadMemoryWordWithoutCycle(0xd020).Should().Be(0xabcd);
        sbl.Proc.ReadMemoryValueWithoutCycle(0xd022).Should().Be(0xd0);
    }

    [Fact]
    public void TestSuite9()
    {
        var symbols = new Dictionary<string, int>
        {
            { "Loc1", 0xd020 }
        };

        var symbolFile = new SymbolFile(symbols);

        var tree = GetContext("GrammarTests/test-9.txt");

        var walker = new ParseTreeWalker();
        var sbl = new SimBaseListener { Symbols = symbolFile };

        walker.Walk(sbl, tree);
    }

    [Fact]
    public void TestSuite10()
    {
        var symbols = new Dictionary<string, int>
        {
            { "Loc1", 0xd020 }
        };

        var symbolFile = new SymbolFile(symbols);

        var tree = GetContext("GrammarTests/test-10.txt");

        var walker = new ParseTreeWalker();
        var sbl = new SimBaseListener { Symbols = symbolFile };

        walker.Walk(sbl, tree);
    }

    [Fact]
    public void TestSuite11_StopOnAddressWithSymbols()
    {
        // This test validates that stop_on_address works with symbol references
        // The grammar should accept both numeric and symbol forms for stop_on_address
        var tree = GetContext("GrammarTests/test-11.txt");

        var walker = new ParseTreeWalker();
        var sbl = new SimBaseListener();

        walker.Walk(sbl, tree);
    }

    [Fact]
    public void TestSuite12_GrammarFixes()
    {
        // This test validates grammar fixes:
        // - Operator precedence (mul/div before add/sub, arithmetic before bitwise)
        // - Mixed statements in test blocks
        // - Register and flag assignments
        // - Expression assignments
        // - Nested symbol references
        var tree = GetContext("GrammarTests/test-12.txt");

        var walker = new ParseTreeWalker();
        var sbl = new SimBaseListener();

        walker.Walk(sbl, tree);
    }
}
