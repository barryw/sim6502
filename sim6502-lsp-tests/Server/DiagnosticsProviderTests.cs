using sim6502_lsp.Server;
using Xunit;

namespace sim6502_lsp_tests.Server;

public class DiagnosticsProviderTests
{
    [Fact]
    public void ValidSyntax_ReturnsNoDiagnostics()
    {
        var provider = new DiagnosticsProvider();
        var content = @"suites {
  suite(""Test Suite"") {
    system(generic_6502)
    test(""test-1"", ""Description"") {
      a = $42
      $0300 = $60
      jsr($0300, stop_on_rts = true, fail_on_brk = false)
      assert(a == $42, ""A should be $42"")
    }
  }
}";
        var diagnostics = provider.GetDiagnostics(content);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MissingBrace_ReturnsSyntaxError()
    {
        var provider = new DiagnosticsProvider();
        var content = "suites {";  // Missing closing brace

        var diagnostics = provider.GetDiagnostics(content);

        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void InvalidKeyword_ReturnsSyntaxError()
    {
        var provider = new DiagnosticsProvider();
        var content = "invalid_keyword { }";

        var diagnostics = provider.GetDiagnostics(content);

        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void DeprecatedProcessor_ReturnsWarning()
    {
        var provider = new DiagnosticsProvider();
        var content = @"suites {
  suite(""Test"") {
    processor(6502)
    test(""t"", ""d"") {
      a = $00
      $0300 = $60
      jsr($0300, stop_on_rts = true, fail_on_brk = false)
      assert(a == $00, ""ok"")
    }
  }
}";
        var diagnostics = provider.GetDiagnostics(content);

        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("deprecated"));
    }
}
