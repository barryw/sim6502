using System.Text.RegularExpressions;

namespace sim6502_lsp.Parsing;

public record ParsedSymbol(string Name, int Address);

public partial class KickAssemblerSymbolParser
{
    // Matches: .label name=$xxxx or .label name=decimal
    [GeneratedRegex(@"^\s*\.label\s+(\w+)\s*=\s*\$?([0-9a-fA-F]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPattern();

    // Matches: .const NAME=$xxxx or .const NAME=decimal
    [GeneratedRegex(@"^\s*\.const\s+(\w+)\s*=\s*\$?([0-9a-fA-F]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ConstPattern();

    public ParsedSymbol? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        line = line.Trim();

        if (line.StartsWith("//") || line.StartsWith(";"))
            return null;

        var match = LabelPattern().Match(line);
        if (match.Success)
        {
            return CreateSymbol(match);
        }

        match = ConstPattern().Match(line);
        if (match.Success)
        {
            return CreateSymbol(match);
        }

        return null;
    }

    private ParsedSymbol? CreateSymbol(Match match)
    {
        var name = match.Groups[1].Value;
        var addressStr = match.Groups[2].Value;

        // Determine if hex or decimal
        int address;
        if (match.Value.Contains('$'))
        {
            address = Convert.ToInt32(addressStr, 16);
        }
        else
        {
            // Could be hex without $ or decimal
            address = addressStr.All(c => char.IsDigit(c))
                ? int.Parse(addressStr)
                : Convert.ToInt32(addressStr, 16);
        }

        return new ParsedSymbol(name, address);
    }

    public IEnumerable<ParsedSymbol> ParseContent(string content)
    {
        var lines = content.Split('\n', StringSplitOptions.None);
        foreach (var line in lines)
        {
            var symbol = ParseLine(line);
            if (symbol != null)
                yield return symbol;
        }
    }

    public IEnumerable<ParsedSymbol> ParseFile(string filePath)
    {
        if (!File.Exists(filePath))
            yield break;

        var content = File.ReadAllText(filePath);
        foreach (var symbol in ParseContent(content))
            yield return symbol;
    }
}
