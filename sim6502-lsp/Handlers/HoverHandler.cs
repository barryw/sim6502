// sim6502-lsp/Handlers/HoverHandler.cs
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using sim6502_lsp.Server;

namespace sim6502_lsp.Handlers;

public class HoverHandler : IHoverHandler
{
    private readonly DocumentManager _documentManager;
    private readonly SymbolIndex _symbolIndex;

    public HoverHandler(DocumentManager documentManager, SymbolIndex symbolIndex)
    {
        _documentManager = documentManager;
        _symbolIndex = symbolIndex;
    }

    public HoverRegistrationOptions GetRegistrationOptions(
        HoverCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.6502")
        };
    }

    public Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var content = _documentManager.GetContent(uri);
        if (content == null)
            return Task.FromResult<Hover?>(null);

        var line = (int)request.Position.Line;
        var character = (int)request.Position.Character;

        var word = GetWordAtPosition(content, line, character);
        if (string.IsNullOrEmpty(word))
            return Task.FromResult<Hover?>(null);

        // Check if it's a symbol
        var symbol = _symbolIndex.GetSymbol(word);
        if (symbol != null)
        {
            var markdown = $"**{symbol.Name}**\n\n" +
                          $"Address: `${symbol.Address:X4}` ({symbol.Address})\n\n" +
                          $"Source: {symbol.Source}";

            if (symbol.AssemblySourcePath != null)
                markdown += $"\n\nDefined in: `{symbol.AssemblySourcePath}:{symbol.AssemblySourceLine}`";

            return Task.FromResult<Hover?>(new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(
                    new MarkupContent { Kind = MarkupKind.Markdown, Value = markdown }
                )
            });
        }

        // Check if it's a keyword
        var keywordHover = GetKeywordHover(word);
        if (keywordHover != null)
        {
            return Task.FromResult<Hover?>(new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(
                    new MarkupContent { Kind = MarkupKind.Markdown, Value = keywordHover }
                )
            });
        }

        return Task.FromResult<Hover?>(null);
    }

    private string? GetWordAtPosition(string content, int line, int character)
    {
        var lines = content.Split('\n');
        if (line >= lines.Length)
            return null;

        var lineText = lines[line];
        if (character >= lineText.Length)
            return null;

        // Find word boundaries
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

    private string? GetKeywordHover(string word)
    {
        return word.ToLower() switch
        {
            "suites" => "**suites**\n\nTop-level container for all test suites.\n\n```\nsuites {\n  suite(...) { }\n}\n```",
            "suite" => "**suite**\n\nDefines a test suite with a name.\n\n```\nsuite(\"Suite Name\") {\n  system(c64)\n  test(...) { }\n}\n```",
            "test" => "**test**\n\nDefines a test case.\n\n```\ntest(\"name\", \"description\") {\n  // setup and assertions\n}\n```",
            "assert" => "**assert**\n\nAssert a condition with a message.\n\n```\nassert(a == $42, \"A should be $42\")\nassert(peekbyte($0400) == $20, \"Screen char\")\n```",
            "jsr" => "**jsr**\n\nCall a subroutine and wait for return.\n\n```\njsr($0810, stop_on_rts = true, fail_on_brk = false)\njsr([main], stop_on_address = $0900, fail_on_brk = true)\n```",
            "system" => "**system**\n\nSet the system type for the suite.\n\n```\nsystem(c64)           // C64 with banking\nsystem(generic_6502)  // Flat 64KB\nsystem(generic_6510)  // Flat 64KB + I/O port\nsystem(generic_65c02) // 65C02 opcodes\n```",
            "load" => "**load**\n\nLoad a binary file into memory.\n\n```\nload(\"program.prg\")\nload(\"data.bin\", address = $c000)\n```",
            "symbols" => "**symbols**\n\nLoad a symbol file for symbolic references.\n\n```\nsymbols(\"program.sym\")\n```",
            "peekbyte" => "**peekbyte**\n\nRead a byte from memory.\n\n```\nassert(peekbyte($0400) == $20, \"Space char\")\n```",
            "peekword" => "**peekword**\n\nRead a 16-bit word from memory (little-endian).\n\n```\nassert(peekword($00fb) == $c000, \"Vector\")\n```",
            "a" => "**A** (Accumulator)\n\n8-bit accumulator register used for arithmetic and logic operations.",
            "x" => "**X** (Index Register)\n\n8-bit index register for addressing and counting.",
            "y" => "**Y** (Index Register)\n\n8-bit index register for addressing and counting.",
            "c" => "**C** (Carry Flag)\n\nSet when arithmetic produces a carry/borrow.",
            "n" => "**N** (Negative Flag)\n\nSet when result has bit 7 set.",
            "z" => "**Z** (Zero Flag)\n\nSet when result is zero.",
            "d" => "**D** (Decimal Flag)\n\nEnables BCD mode for ADC/SBC.",
            "v" => "**V** (Overflow Flag)\n\nSet when signed arithmetic overflows.",
            _ => null
        };
    }
}
