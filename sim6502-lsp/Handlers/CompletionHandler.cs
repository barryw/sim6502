// sim6502-lsp/Handlers/CompletionHandler.cs
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using sim6502_lsp.Server;

namespace sim6502_lsp.Handlers;

public class CompletionHandler : ICompletionHandler
{
    private readonly DocumentManager _documentManager;
    private readonly CompletionProvider _completionProvider;

    public CompletionHandler(DocumentManager documentManager, CompletionProvider completionProvider)
    {
        _documentManager = documentManager;
        _completionProvider = completionProvider;
    }

    public CompletionRegistrationOptions GetRegistrationOptions(
        CompletionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.6502"),
            TriggerCharacters = new Container<string>(".", "[", "(", "="),
            ResolveProvider = false
        };
    }

    public Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var content = _documentManager.GetContent(uri) ?? "";
        var line = (int)request.Position.Line;
        var character = (int)request.Position.Character;

        var items = _completionProvider.GetCompletions(content, line, character);

        var completionItems = items.Select(item => new OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem
        {
            Label = item.Label,
            Kind = item.Kind switch
            {
                CompletionItemKind.Keyword => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
                CompletionItemKind.Variable => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Variable,
                CompletionItemKind.Function => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Function,
                CompletionItemKind.Constant => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Constant,
                CompletionItemKind.Enum => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Enum,
                _ => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Text
            },
            Detail = item.Detail,
            Documentation = item.Documentation != null
                ? new StringOrMarkupContent(item.Documentation)
                : null
        }).ToArray();

        return Task.FromResult(new CompletionList(completionItems));
    }
}
