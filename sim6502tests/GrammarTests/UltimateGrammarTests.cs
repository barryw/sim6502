using Antlr4.Runtime;
using FluentAssertions;
using sim6502.Errors;
using sim6502.Grammar;
using sim6502.Grammar.Generated;
using Xunit;

namespace sim6502tests.GrammarTests;

public class UltimateGrammarTests
{
    private static ErrorCollector Parse(string source)
    {
        var collector = new ErrorCollector();
        collector.SetSource(source, "test.6502");

        var lexer = new sim6502Lexer(new AntlrInputStream(source));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));

        var parser = new sim6502Parser(new CommonTokenStream(lexer)) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));

        parser.suites();
        return collector;
    }

    private static string Wrap(string body) => $@"
suites {{
  suite(""ultimate"") {{
    system(c64)
    ultimate(fs_root = ""fixtures/usb0"")
    test(""t"", ""d"") {{
{body}
    }}
  }}
}}";

    [Fact]
    public void UltimateDeclaration_Parses()
    {
        Parse(Wrap("      a = $01")).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciCall_WithNoArguments_Parses()
    {
        Parse(Wrap("      uci($01, $01)")).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciCall_WithAStringArgument_Parses()
    {
        Parse(Wrap(@"      uci($01, $11, ""/Usb0/data"")")).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciCall_WithMixedArguments_Parses()
    {
        Parse(Wrap(@"      uci($01, $02, $01, ""game.prg"")")).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciStatus_ParsesInsideAssert()
    {
        Parse(Wrap(@"      assert(uci_status(""00,OK""), ""ok"")")).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciData_ParsesInsideAComparison()
    {
        Parse(Wrap(@"      assert(uci_data(0) == $55, ""first byte"")"))
            .HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciData_ParsesInsideAnExpression()
    {
        Parse(Wrap(@"      assert(uci_data(0) + uci_data(1) == $10, ""sum"")"))
            .HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciCall_ParsesInsideASetupBlock()
    {
        var source = @"
suites {
  suite(""ultimate"") {
    system(c64)
    ultimate(fs_root = ""fixtures/usb0"")
    setup {
      uci($01, $11, ""/Usb0/data"")
    }
    test(""t"", ""d"") {
      a = $01
    }
  }
}";
        Parse(source).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UltimateDeclaration_WithoutFsRoot_IsASyntaxError()
    {
        var source = @"
suites {
  suite(""ultimate"") {
    system(c64)
    ultimate()
    test(""t"", ""d"") {
      a = $01
    }
  }
}";
        Parse(source).HasErrors.Should().BeTrue();
    }

    [Fact]
    public void UciCall_WithOnlyOneArgument_IsASyntaxError()
    {
        Parse(Wrap("      uci($01)")).HasErrors.Should().BeTrue();
    }

    [Fact]
    public void ExistingSuitesWithoutUltimate_StillParse()
    {
        var source = @"
suites {
  suite(""plain"") {
    system(c64)
    test(""t"", ""d"") {
      a = $01
      assert(a == $01, ""a"")
    }
  }
}";
        Parse(source).HasErrors.Should().BeFalse();
    }
}
