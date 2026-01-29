using System.Collections.Concurrent;

namespace sim6502_lsp.Server;

public enum SymbolSource
{
    Dsl,        // Defined in .6502 file
    SymFile,    // From .sym file
    Assembly    // Located in .asm source
}

public record SymbolInfo(
    string Name,
    int Address,
    Uri SourceUri,
    int Line,
    int Column,
    SymbolSource Source,
    string? AssemblySourcePath = null,
    int? AssemblySourceLine = null
);

public class SymbolIndex
{
    private readonly ConcurrentDictionary<string, SymbolInfo> _symbols = new();
    private readonly ConcurrentDictionary<Uri, HashSet<string>> _documentSymbols = new();

    public void AddSymbol(SymbolInfo symbol)
    {
        _symbols[symbol.Name] = symbol;

        _documentSymbols.AddOrUpdate(
            symbol.SourceUri,
            _ => new HashSet<string> { symbol.Name },
            (_, set) => { set.Add(symbol.Name); return set; }
        );
    }

    public SymbolInfo? GetSymbol(string name)
    {
        return _symbols.TryGetValue(name, out var symbol) ? symbol : null;
    }

    public IEnumerable<SymbolInfo> GetSymbolsForDocument(Uri uri)
    {
        if (!_documentSymbols.TryGetValue(uri, out var names))
            return Enumerable.Empty<SymbolInfo>();

        return names
            .Select(n => _symbols.TryGetValue(n, out var s) ? s : null)
            .Where(s => s != null)!;
    }

    public void ClearDocument(Uri uri)
    {
        if (!_documentSymbols.TryRemove(uri, out var names))
            return;

        foreach (var name in names)
        {
            _symbols.TryRemove(name, out _);
        }
    }

    public IEnumerable<SymbolInfo> GetAllSymbols()
    {
        return _symbols.Values;
    }

    public IEnumerable<SymbolInfo> SearchSymbols(string prefix)
    {
        return _symbols.Values
            .Where(s => s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
