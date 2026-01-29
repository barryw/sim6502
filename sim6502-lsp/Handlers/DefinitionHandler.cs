// sim6502-lsp/Handlers/DefinitionHandler.cs
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using sim6502_lsp.Server;

namespace sim6502_lsp.Handlers;

public class DefinitionHandler : IDefinitionHandler
{
    private readonly DocumentManager _documentManager;
    private readonly SymbolIndex _symbolIndex;

    public DefinitionHandler(DocumentManager documentManager, SymbolIndex symbolIndex)
    {
        _documentManager = documentManager;
        _symbolIndex = symbolIndex;
    }

    public DefinitionRegistrationOptions GetRegistrationOptions(
        DefinitionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DefinitionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.6502")
        };
    }

    public Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var content = _documentManager.GetContent(uri);
        if (content == null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var line = (int)request.Position.Line;
        var character = (int)request.Position.Character;

        var word = GetWordAtPosition(content, line, character);
        if (string.IsNullOrEmpty(word))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        // Check if it's a symbol reference
        var symbol = _symbolIndex.GetSymbol(word);
        if (symbol != null)
        {
            // If we have assembly source location, go there
            if (symbol.AssemblySourcePath != null && symbol.AssemblySourceLine.HasValue)
            {
                return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(
                    new Location
                    {
                        Uri = DocumentUri.FromFileSystemPath(symbol.AssemblySourcePath),
                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                            new Position(symbol.AssemblySourceLine.Value - 1, 0),
                            new Position(symbol.AssemblySourceLine.Value - 1, 0)
                        )
                    }
                ));
            }

            // Otherwise go to the symbol definition in the source file
            return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(
                new Location
                {
                    Uri = DocumentUri.From(symbol.SourceUri),
                    Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                        new Position(symbol.Line - 1, symbol.Column),
                        new Position(symbol.Line - 1, symbol.Column + symbol.Name.Length)
                    )
                }
            ));
        }

        return Task.FromResult<LocationOrLocationLinks?>(null);
    }

    private string? GetWordAtPosition(string content, int line, int character)
    {
        var lines = content.Split('\n');
        if (line >= lines.Length)
            return null;

        var lineText = lines[line];
        if (character >= lineText.Length)
            return null;

        var start = character;
        while (start > 0 && IsWordChar(lineText[start - 1]))
            start--;

        var end = character;
        while (end < lineText.Length && IsWordChar(lineText[end]))
            end++;

        if (start == end)
            return null;

        return lineText[start..end];
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
