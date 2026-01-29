using sim6502_lsp.Server;
using Xunit;

namespace sim6502_lsp_tests.Server;

public class DocumentManagerTests
{
    [Fact]
    public void OpenDocument_StoresContent()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///test.6502");
        var content = "suites { }";

        manager.OpenDocument(uri, content);

        Assert.Equal(content, manager.GetContent(uri));
    }

    [Fact]
    public void UpdateDocument_ReplacesContent()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///test.6502");

        manager.OpenDocument(uri, "old content");
        manager.UpdateDocument(uri, "new content");

        Assert.Equal("new content", manager.GetContent(uri));
    }

    [Fact]
    public void CloseDocument_RemovesContent()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///test.6502");

        manager.OpenDocument(uri, "content");
        manager.CloseDocument(uri);

        Assert.Null(manager.GetContent(uri));
    }

    [Fact]
    public void GetContent_ReturnsNullForUnknownUri()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///unknown.6502");

        Assert.Null(manager.GetContent(uri));
    }

    [Fact]
    public void GetOpenDocuments_ReturnsAllUris()
    {
        var manager = new DocumentManager();
        var uri1 = new Uri("file:///test1.6502");
        var uri2 = new Uri("file:///test2.6502");

        manager.OpenDocument(uri1, "content1");
        manager.OpenDocument(uri2, "content2");

        var uris = manager.GetOpenDocuments().ToList();
        Assert.Contains(uri1, uris);
        Assert.Contains(uri2, uris);
    }
}
