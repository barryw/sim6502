// sim6502-lsp/Handlers/TextDocumentHandler.cs
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using sim6502_lsp.Server;

namespace sim6502_lsp.Handlers;

public class TextDocumentHandler : TextDocumentSyncHandlerBase
{
    private readonly DocumentManager _documentManager;
    private readonly DiagnosticsProvider _diagnosticsProvider;
    private readonly ILanguageServerFacade _server;

    public TextDocumentHandler(
        DocumentManager documentManager,
        DiagnosticsProvider diagnosticsProvider,
        ILanguageServerFacade server)
    {
        _documentManager = documentManager;
        _diagnosticsProvider = diagnosticsProvider;
        _server = server;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, "sim6502");
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.6502"),
            Change = TextDocumentSyncKind.Full,
            Save = new SaveOptions { IncludeText = true }
        };
    }

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var content = request.TextDocument.Text;

        _documentManager.OpenDocument(uri, content);
        PublishDiagnostics(uri, content);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var content = request.ContentChanges.First().Text;

        _documentManager.UpdateDocument(uri, content);
        PublishDiagnostics(uri, content);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        _documentManager.CloseDocument(uri);

        // Clear diagnostics for closed file
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = request.TextDocument.Uri,
            Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>()
        });

        return Unit.Task;
    }

    private void PublishDiagnostics(Uri uri, string content)
    {
        var diagnostics = _diagnosticsProvider.GetDiagnostics(content);

        var lspDiagnostics = diagnostics.Select(d => new OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic
        {
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                new Position(d.StartLine - 1, d.StartColumn),
                new Position(d.EndLine - 1, d.EndColumn)
            ),
            Severity = d.Severity switch
            {
                Server.DiagnosticSeverity.Error => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                Server.DiagnosticSeverity.Warning => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
                Server.DiagnosticSeverity.Information => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Information,
                Server.DiagnosticSeverity.Hint => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Hint,
                _ => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error
            },
            Message = d.Message,
            Source = "sim6502"
        }).ToArray();

        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.From(uri),
            Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(lspDiagnostics)
        });
    }
}
