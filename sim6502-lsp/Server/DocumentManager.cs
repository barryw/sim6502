using System.Collections.Concurrent;

namespace sim6502_lsp.Server;

public class DocumentManager
{
    private readonly ConcurrentDictionary<Uri, string> _documents = new();

    public void OpenDocument(Uri uri, string content)
    {
        _documents[uri] = content;
    }

    public void UpdateDocument(Uri uri, string content)
    {
        _documents[uri] = content;
    }

    public void CloseDocument(Uri uri)
    {
        _documents.TryRemove(uri, out _);
    }

    public string? GetContent(Uri uri)
    {
        return _documents.TryGetValue(uri, out var content) ? content : null;
    }

    public IEnumerable<Uri> GetOpenDocuments()
    {
        return _documents.Keys;
    }
}
