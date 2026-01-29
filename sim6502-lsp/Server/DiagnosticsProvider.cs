using Antlr4.Runtime;
using sim6502.Grammar.Generated;

namespace sim6502_lsp.Server;

public enum DiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

public record Diagnostic(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string Message,
    DiagnosticSeverity Severity
);

public class DiagnosticsProvider
{
    public List<Diagnostic> GetDiagnostics(string content)
    {
        var diagnostics = new List<Diagnostic>();
        var errorListener = new DiagnosticErrorListener(diagnostics);

        var inputStream = new AntlrInputStream(content);
        var lexer = new sim6502Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);

        var tokenStream = new CommonTokenStream(lexer);
        var parser = new sim6502Parser(tokenStream);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        var tree = parser.suites();

        // Check for deprecated processor() usage
        CheckForDeprecatedSyntax(tree, diagnostics);

        return diagnostics;
    }

    private void CheckForDeprecatedSyntax(sim6502Parser.SuitesContext tree, List<Diagnostic> diagnostics)
    {
        var visitor = new DeprecationVisitor(diagnostics);
        visitor.Visit(tree);
    }

    private class DiagnosticErrorListener : IAntlrErrorListener<int>, IAntlrErrorListener<IToken>
    {
        private readonly List<Diagnostic> _diagnostics;

        public DiagnosticErrorListener(List<Diagnostic> diagnostics)
        {
            _diagnostics = diagnostics;
        }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            _diagnostics.Add(new Diagnostic(
                line, charPositionInLine,
                line, charPositionInLine + 1,
                msg,
                DiagnosticSeverity.Error
            ));
        }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            var length = offendingSymbol?.Text?.Length ?? 1;
            _diagnostics.Add(new Diagnostic(
                line, charPositionInLine,
                line, charPositionInLine + length,
                msg,
                DiagnosticSeverity.Error
            ));
        }
    }

    private class DeprecationVisitor : sim6502BaseVisitor<object?>
    {
        private readonly List<Diagnostic> _diagnostics;

        public DeprecationVisitor(List<Diagnostic> diagnostics)
        {
            _diagnostics = diagnostics;
        }

        public override object? VisitProcessorDeclaration(sim6502Parser.ProcessorDeclarationContext context)
        {
            _diagnostics.Add(new Diagnostic(
                context.Start.Line,
                context.Start.Column,
                context.Stop.Line,
                context.Stop.Column + context.Stop.Text.Length,
                "processor() is deprecated - use system() instead",
                DiagnosticSeverity.Warning
            ));
            return base.VisitProcessorDeclaration(context);
        }
    }
}
