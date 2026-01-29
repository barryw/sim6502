# sim6502 Language Server Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build an LSP server for the sim6502 testing DSL, providing diagnostics, completion, hover, and go-to-definition in VS Code and other editors.

**Architecture:** Standalone .NET console app using OmniSharp LSP libraries, reusing the existing ANTLR parser from sim6502. VS Code extension is a thin TypeScript launcher.

**Tech Stack:** C#/.NET 10, OmniSharp.Extensions.LanguageServer, ANTLR4, TypeScript (VS Code extension)

**Related:** `docs/plans/2026-01-28-language-server-design.md`

---

## Phase 1: Project Setup

### Task 1: Create sim6502-lsp Project

**Files:**
- Create: `sim6502-lsp/sim6502-lsp.csproj`
- Create: `sim6502-lsp/Program.cs`
- Modify: `sim6502.sln` (add project)

**Step 1: Create the project directory**

```bash
mkdir -p sim6502-lsp
```

**Step 2: Create the project file**

```xml
<!-- sim6502-lsp/sim6502-lsp.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>sim6502_lsp</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="OmniSharp.Extensions.LanguageServer" Version="0.19.9" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="9.0.0" />
    <PackageReference Include="NLog" Version="5.4.0" />
    <PackageReference Include="NLog.Extensions.Logging" Version="5.4.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../sim6502/sim6502.csproj" />
  </ItemGroup>

</Project>
```

**Step 3: Create minimal Program.cs**

```csharp
// sim6502-lsp/Program.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;

namespace sim6502_lsp;

class Program
{
    static async Task Main(string[] args)
    {
        var server = await LanguageServer.From(options =>
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(x => x
                    .AddNLog()
                    .SetMinimumLevel(LogLevel.Debug))
                .WithServices(ConfigureServices)
        );

        await server.WaitForExit;
    }

    static void ConfigureServices(IServiceCollection services)
    {
        // Services will be added here as we build features
    }
}
```

**Step 4: Add project to solution**

Run: `dotnet sln sim6502.sln add sim6502-lsp/sim6502-lsp.csproj`
Expected: Project added successfully

**Step 5: Restore and build**

Run: `dotnet build sim6502-lsp/sim6502-lsp.csproj`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add sim6502-lsp/ sim6502.sln
git commit -m "feat(lsp): create sim6502-lsp project with OmniSharp dependencies"
```

---

### Task 2: Create sim6502-lsp-tests Project

**Files:**
- Create: `sim6502-lsp-tests/sim6502-lsp-tests.csproj`
- Create: `sim6502-lsp-tests/SmokeTests.cs`

**Step 1: Create test project**

```bash
mkdir -p sim6502-lsp-tests
```

**Step 2: Create test project file**

```xml
<!-- sim6502-lsp-tests/sim6502-lsp-tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>sim6502_lsp_tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../sim6502-lsp/sim6502-lsp.csproj" />
  </ItemGroup>

</Project>
```

**Step 3: Create smoke test**

```csharp
// sim6502-lsp-tests/SmokeTests.cs
using Xunit;

namespace sim6502_lsp_tests;

public class SmokeTests
{
    [Fact]
    public void ProjectReferencesWork()
    {
        // Verify we can reference types from sim6502-lsp
        Assert.True(true);
    }
}
```

**Step 4: Add to solution and run tests**

Run: `dotnet sln sim6502.sln add sim6502-lsp-tests/sim6502-lsp-tests.csproj`
Run: `dotnet test sim6502-lsp-tests -v n`
Expected: 1 test passed

**Step 5: Commit**

```bash
git add sim6502-lsp-tests/ sim6502.sln
git commit -m "test(lsp): create sim6502-lsp-tests project"
```

---

## Phase 2: Document Manager

### Task 3: Create Document Manager with Tests

**Files:**
- Create: `sim6502-lsp/Server/DocumentManager.cs`
- Create: `sim6502-lsp-tests/Server/DocumentManagerTests.cs`

**Step 1: Write failing tests**

```csharp
// sim6502-lsp-tests/Server/DocumentManagerTests.cs
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
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test sim6502-lsp-tests --filter "FullyQualifiedName~DocumentManagerTests" -v n`
Expected: FAIL - DocumentManager class does not exist

**Step 3: Implement DocumentManager**

```csharp
// sim6502-lsp/Server/DocumentManager.cs
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
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test sim6502-lsp-tests --filter "FullyQualifiedName~DocumentManagerTests" -v n`
Expected: All 5 tests PASS

**Step 5: Commit**

```bash
git add sim6502-lsp/Server/DocumentManager.cs sim6502-lsp-tests/Server/DocumentManagerTests.cs
git commit -m "feat(lsp): implement DocumentManager for tracking open files"
```

---

## Phase 3: Diagnostics

### Task 4: Create DiagnosticsProvider with Tests

**Files:**
- Create: `sim6502-lsp/Server/DiagnosticsProvider.cs`
- Create: `sim6502-lsp-tests/Server/DiagnosticsProviderTests.cs`

**Step 1: Write failing tests**

```csharp
// sim6502-lsp-tests/Server/DiagnosticsProviderTests.cs
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
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test sim6502-lsp-tests --filter "FullyQualifiedName~DiagnosticsProviderTests" -v n`
Expected: FAIL - DiagnosticsProvider class does not exist

**Step 3: Implement DiagnosticsProvider**

```csharp
// sim6502-lsp/Server/DiagnosticsProvider.cs
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
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test sim6502-lsp-tests --filter "FullyQualifiedName~DiagnosticsProviderTests" -v n`
Expected: All 4 tests PASS

**Step 5: Commit**

```bash
git add sim6502-lsp/Server/DiagnosticsProvider.cs sim6502-lsp-tests/Server/DiagnosticsProviderTests.cs
git commit -m "feat(lsp): implement DiagnosticsProvider using ANTLR parser"
```

---

### Task 5: Create TextDocumentHandler

**Files:**
- Create: `sim6502-lsp/Handlers/TextDocumentHandler.cs`
- Modify: `sim6502-lsp/Program.cs`

**Step 1: Implement TextDocumentHandler**

```csharp
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

    private readonly DocumentSelector _documentSelector = new(
        new DocumentFilter { Pattern = "**/*.6502" }
    );

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
            DocumentSelector = _documentSelector,
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
                DiagnosticSeverity.Error => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                DiagnosticSeverity.Warning => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
                DiagnosticSeverity.Information => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Information,
                DiagnosticSeverity.Hint => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Hint,
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
```

**Step 2: Update Program.cs to register services**

```csharp
// sim6502-lsp/Program.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using sim6502_lsp.Handlers;
using sim6502_lsp.Server;

namespace sim6502_lsp;

class Program
{
    static async Task Main(string[] args)
    {
        var server = await LanguageServer.From(options =>
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(x => x
                    .AddNLog()
                    .SetMinimumLevel(LogLevel.Debug))
                .WithServices(ConfigureServices)
                .WithHandler<TextDocumentHandler>()
        );

        await server.WaitForExit;
    }

    static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<DocumentManager>();
        services.AddSingleton<DiagnosticsProvider>();
    }
}
```

**Step 3: Build to verify compilation**

Run: `dotnet build sim6502-lsp/sim6502-lsp.csproj`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add sim6502-lsp/Handlers/TextDocumentHandler.cs sim6502-lsp/Program.cs
git commit -m "feat(lsp): implement TextDocumentHandler with diagnostics publishing"
```

---

## Phase 4: Completion

### Task 6: Create CompletionHandler with Tests

**Files:**
- Create: `sim6502-lsp/Handlers/CompletionHandler.cs`
- Create: `sim6502-lsp-tests/Handlers/CompletionHandlerTests.cs`
- Modify: `sim6502-lsp/Program.cs`

**Step 1: Write failing tests**

```csharp
// sim6502-lsp-tests/Handlers/CompletionHandlerTests.cs
using sim6502_lsp.Handlers;
using sim6502_lsp.Server;
using Xunit;

namespace sim6502_lsp_tests.Handlers;

public class CompletionHandlerTests
{
    [Fact]
    public void GetCompletions_ReturnsKeywords()
    {
        var documentManager = new DocumentManager();
        var handler = new CompletionProvider();

        var completions = handler.GetCompletions("", 0, 0);

        Assert.Contains(completions, c => c.Label == "suites");
        Assert.Contains(completions, c => c.Label == "suite");
        Assert.Contains(completions, c => c.Label == "test");
        Assert.Contains(completions, c => c.Label == "assert");
    }

    [Fact]
    public void GetCompletions_ReturnsRegisters()
    {
        var handler = new CompletionProvider();

        var completions = handler.GetCompletions("", 0, 0);

        Assert.Contains(completions, c => c.Label == "a");
        Assert.Contains(completions, c => c.Label == "x");
        Assert.Contains(completions, c => c.Label == "y");
    }

    [Fact]
    public void GetCompletions_ReturnsFlags()
    {
        var handler = new CompletionProvider();

        var completions = handler.GetCompletions("", 0, 0);

        Assert.Contains(completions, c => c.Label == "c");
        Assert.Contains(completions, c => c.Label == "n");
        Assert.Contains(completions, c => c.Label == "z");
    }

    [Fact]
    public void GetCompletions_ReturnsSystemTypes()
    {
        var handler = new CompletionProvider();

        var completions = handler.GetCompletions("", 0, 0);

        Assert.Contains(completions, c => c.Label == "c64");
        Assert.Contains(completions, c => c.Label == "generic_6502");
        Assert.Contains(completions, c => c.Label == "generic_6510");
        Assert.Contains(completions, c => c.Label == "generic_65c02");
    }

    [Fact]
    public void GetCompletions_ReturnsBuiltinFunctions()
    {
        var handler = new CompletionProvider();

        var completions = handler.GetCompletions("", 0, 0);

        Assert.Contains(completions, c => c.Label == "peekbyte");
        Assert.Contains(completions, c => c.Label == "peekword");
        Assert.Contains(completions, c => c.Label == "memcmp");
        Assert.Contains(completions, c => c.Label == "memchk");
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test sim6502-lsp-tests --filter "FullyQualifiedName~CompletionHandlerTests" -v n`
Expected: FAIL - CompletionProvider class does not exist

**Step 3: Implement CompletionProvider**

```csharp
// sim6502-lsp/Handlers/CompletionProvider.cs
namespace sim6502_lsp.Handlers;

public record CompletionItem(
    string Label,
    CompletionItemKind Kind,
    string? Detail = null,
    string? Documentation = null
);

public enum CompletionItemKind
{
    Keyword = 14,
    Variable = 6,
    Function = 3,
    Constant = 21,
    Enum = 13
}

public class CompletionProvider
{
    private static readonly List<CompletionItem> Keywords = new()
    {
        new("suites", CompletionItemKind.Keyword, "Top-level container for test suites"),
        new("suite", CompletionItemKind.Keyword, "Define a test suite", "suite(\"name\") { ... }"),
        new("test", CompletionItemKind.Keyword, "Define a test case", "test(\"name\", \"description\") { ... }"),
        new("setup", CompletionItemKind.Keyword, "Setup block run before each test"),
        new("assert", CompletionItemKind.Function, "Assert a condition", "assert(condition, \"message\")"),
        new("jsr", CompletionItemKind.Function, "Jump to subroutine", "jsr(address, stop_on_rts = true, fail_on_brk = false)"),
        new("load", CompletionItemKind.Function, "Load a binary file", "load(\"file.prg\")"),
        new("symbols", CompletionItemKind.Function, "Load symbol file", "symbols(\"file.sym\")"),
        new("system", CompletionItemKind.Keyword, "Set system type", "system(c64)"),
        new("processor", CompletionItemKind.Keyword, "Set processor type (deprecated)", "processor(6502)"),
        new("rom", CompletionItemKind.Function, "Load ROM file", "rom(\"name\", \"file.rom\")"),
    };

    private static readonly List<CompletionItem> Registers = new()
    {
        new("a", CompletionItemKind.Variable, "Accumulator register"),
        new("x", CompletionItemKind.Variable, "X index register"),
        new("y", CompletionItemKind.Variable, "Y index register"),
    };

    private static readonly List<CompletionItem> Flags = new()
    {
        new("c", CompletionItemKind.Variable, "Carry flag"),
        new("n", CompletionItemKind.Variable, "Negative flag"),
        new("z", CompletionItemKind.Variable, "Zero flag"),
        new("d", CompletionItemKind.Variable, "Decimal flag"),
        new("v", CompletionItemKind.Variable, "Overflow flag"),
    };

    private static readonly List<CompletionItem> SystemTypes = new()
    {
        new("c64", CompletionItemKind.Enum, "Commodore 64 with memory banking"),
        new("generic_6502", CompletionItemKind.Enum, "Generic 6502 with flat 64KB RAM"),
        new("generic_6510", CompletionItemKind.Enum, "Generic 6510 with $00/$01 I/O port"),
        new("generic_65c02", CompletionItemKind.Enum, "Generic 65C02 with extended opcodes"),
    };

    private static readonly List<CompletionItem> Functions = new()
    {
        new("peekbyte", CompletionItemKind.Function, "Read byte from memory", "peekbyte(address)"),
        new("peekword", CompletionItemKind.Function, "Read word from memory", "peekword(address)"),
        new("memcmp", CompletionItemKind.Function, "Compare memory regions", "memcmp(src, dest, size)"),
        new("memchk", CompletionItemKind.Function, "Check memory for value", "memchk(address, size, value)"),
        new("memfill", CompletionItemKind.Function, "Fill memory region", "memfill(address, size, value)"),
        new("memdump", CompletionItemKind.Function, "Dump memory region", "memdump(address, size)"),
    };

    private static readonly List<CompletionItem> TestOptions = new()
    {
        new("skip", CompletionItemKind.Keyword, "Skip this test"),
        new("trace", CompletionItemKind.Keyword, "Enable instruction tracing"),
        new("timeout", CompletionItemKind.Keyword, "Set timeout in cycles"),
        new("tags", CompletionItemKind.Keyword, "Add tags to test"),
    };

    private static readonly List<CompletionItem> JsrOptions = new()
    {
        new("stop_on_rts", CompletionItemKind.Keyword, "Stop when RTS encountered"),
        new("stop_on_address", CompletionItemKind.Keyword, "Stop at specific address"),
        new("fail_on_brk", CompletionItemKind.Keyword, "Fail if BRK encountered"),
    };

    public List<CompletionItem> GetCompletions(string content, int line, int character)
    {
        // For now, return all completions
        // TODO: Context-aware filtering based on cursor position
        var all = new List<CompletionItem>();
        all.AddRange(Keywords);
        all.AddRange(Registers);
        all.AddRange(Flags);
        all.AddRange(SystemTypes);
        all.AddRange(Functions);
        all.AddRange(TestOptions);
        all.AddRange(JsrOptions);
        return all;
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test sim6502-lsp-tests --filter "FullyQualifiedName~CompletionHandlerTests" -v n`
Expected: All 5 tests PASS

**Step 5: Create LSP CompletionHandler**

```csharp
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
            DocumentSelector = new DocumentSelector(
                new DocumentFilter { Pattern = "**/*.6502" }
            ),
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
```

**Step 6: Update Program.cs**

Add to `WithHandler` chain:
```csharp
.WithHandler<CompletionHandler>()
```

Add to `ConfigureServices`:
```csharp
services.AddSingleton<CompletionProvider>();
```

**Step 7: Build to verify compilation**

Run: `dotnet build sim6502-lsp/sim6502-lsp.csproj`
Expected: Build succeeded

**Step 8: Commit**

```bash
git add sim6502-lsp/Handlers/CompletionProvider.cs sim6502-lsp/Handlers/CompletionHandler.cs sim6502-lsp-tests/Handlers/CompletionHandlerTests.cs sim6502-lsp/Program.cs
git commit -m "feat(lsp): implement code completion for keywords, registers, and functions"
```

---

## Phase 5: Symbol Index

### Task 7: Create SymbolIndex with Tests

**Files:**
- Create: `sim6502-lsp/Server/SymbolIndex.cs`
- Create: `sim6502-lsp-tests/Server/SymbolIndexTests.cs`

**Step 1: Write failing tests**

```csharp
// sim6502-lsp-tests/Server/SymbolIndexTests.cs
using sim6502_lsp.Server;
using Xunit;

namespace sim6502_lsp_tests.Server;

public class SymbolIndexTests
{
    [Fact]
    public void AddSymbol_CanBeRetrieved()
    {
        var index = new SymbolIndex();
        var uri = new Uri("file:///test.6502");

        index.AddSymbol(new SymbolInfo("myvar", 0x1000, uri, 5, 0, SymbolSource.Dsl));

        var symbol = index.GetSymbol("myvar");
        Assert.NotNull(symbol);
        Assert.Equal(0x1000, symbol.Address);
    }

    [Fact]
    public void GetSymbol_ReturnsNullForUnknown()
    {
        var index = new SymbolIndex();

        Assert.Null(index.GetSymbol("unknown"));
    }

    [Fact]
    public void GetSymbolsForDocument_ReturnsOnlyThatDocument()
    {
        var index = new SymbolIndex();
        var uri1 = new Uri("file:///test1.6502");
        var uri2 = new Uri("file:///test2.6502");

        index.AddSymbol(new SymbolInfo("sym1", 0x1000, uri1, 1, 0, SymbolSource.Dsl));
        index.AddSymbol(new SymbolInfo("sym2", 0x2000, uri2, 1, 0, SymbolSource.Dsl));

        var symbols = index.GetSymbolsForDocument(uri1).ToList();
        Assert.Single(symbols);
        Assert.Equal("sym1", symbols[0].Name);
    }

    [Fact]
    public void ClearDocument_RemovesOnlyThatDocumentsSymbols()
    {
        var index = new SymbolIndex();
        var uri1 = new Uri("file:///test1.6502");
        var uri2 = new Uri("file:///test2.6502");

        index.AddSymbol(new SymbolInfo("sym1", 0x1000, uri1, 1, 0, SymbolSource.Dsl));
        index.AddSymbol(new SymbolInfo("sym2", 0x2000, uri2, 1, 0, SymbolSource.Dsl));

        index.ClearDocument(uri1);

        Assert.Null(index.GetSymbol("sym1"));
        Assert.NotNull(index.GetSymbol("sym2"));
    }

    [Fact]
    public void GetAllSymbols_ReturnsAll()
    {
        var index = new SymbolIndex();
        var uri = new Uri("file:///test.6502");

        index.AddSymbol(new SymbolInfo("a", 0x1000, uri, 1, 0, SymbolSource.Dsl));
        index.AddSymbol(new SymbolInfo("b", 0x2000, uri, 2, 0, SymbolSource.SymFile));

        var all = index.GetAllSymbols().ToList();
        Assert.Equal(2, all.Count);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test sim6502-lsp-tests --filter "FullyQualifiedName~SymbolIndexTests" -v n`
Expected: FAIL - SymbolIndex class does not exist

**Step 3: Implement SymbolIndex**

```csharp
// sim6502-lsp/Server/SymbolIndex.cs
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
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test sim6502-lsp-tests --filter "FullyQualifiedName~SymbolIndexTests" -v n`
Expected: All 5 tests PASS

**Step 5: Commit**

```bash
git add sim6502-lsp/Server/SymbolIndex.cs sim6502-lsp-tests/Server/SymbolIndexTests.cs
git commit -m "feat(lsp): implement SymbolIndex for tracking symbols"
```

---

### Task 8: Create KickAssembler Symbol Parser

**Files:**
- Create: `sim6502-lsp/Parsing/KickAssemblerSymbolParser.cs`
- Create: `sim6502-lsp-tests/Parsing/KickAssemblerSymbolParserTests.cs`

**Step 1: Write failing tests**

```csharp
// sim6502-lsp-tests/Parsing/KickAssemblerSymbolParserTests.cs
using sim6502_lsp.Parsing;
using Xunit;

namespace sim6502_lsp_tests.Parsing;

public class KickAssemblerSymbolParserTests
{
    [Fact]
    public void ParseLine_LabelFormat_ReturnsSymbol()
    {
        var parser = new KickAssemblerSymbolParser();

        var symbol = parser.ParseLine(".label screenRam=$0400");

        Assert.NotNull(symbol);
        Assert.Equal("screenRam", symbol.Name);
        Assert.Equal(0x0400, symbol.Address);
    }

    [Fact]
    public void ParseLine_ConstFormat_ReturnsSymbol()
    {
        var parser = new KickAssemblerSymbolParser();

        var symbol = parser.ParseLine(".const BORDER_COLOR=$d020");

        Assert.NotNull(symbol);
        Assert.Equal("BORDER_COLOR", symbol.Name);
        Assert.Equal(0xD020, symbol.Address);
    }

    [Fact]
    public void ParseLine_Comment_ReturnsNull()
    {
        var parser = new KickAssemblerSymbolParser();

        var symbol = parser.ParseLine("// this is a comment");

        Assert.Null(symbol);
    }

    [Fact]
    public void ParseLine_EmptyLine_ReturnsNull()
    {
        var parser = new KickAssemblerSymbolParser();

        Assert.Null(parser.ParseLine(""));
        Assert.Null(parser.ParseLine("   "));
    }

    [Fact]
    public void ParseFile_MultipleSymbols_ReturnsAll()
    {
        var parser = new KickAssemblerSymbolParser();
        var content = @".label main=$0810
.label loop=$0820
.const SCREEN=$0400";

        var symbols = parser.ParseContent(content).ToList();

        Assert.Equal(3, symbols.Count);
        Assert.Contains(symbols, s => s.Name == "main" && s.Address == 0x0810);
        Assert.Contains(symbols, s => s.Name == "loop" && s.Address == 0x0820);
        Assert.Contains(symbols, s => s.Name == "SCREEN" && s.Address == 0x0400);
    }

    [Fact]
    public void ParseLine_DecimalAddress_ReturnsSymbol()
    {
        var parser = new KickAssemblerSymbolParser();

        var symbol = parser.ParseLine(".label start=2048");

        Assert.NotNull(symbol);
        Assert.Equal("start", symbol.Name);
        Assert.Equal(2048, symbol.Address);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test sim6502-lsp-tests --filter "FullyQualifiedName~KickAssemblerSymbolParserTests" -v n`
Expected: FAIL - KickAssemblerSymbolParser class does not exist

**Step 3: Implement KickAssemblerSymbolParser**

```csharp
// sim6502-lsp/Parsing/KickAssemblerSymbolParser.cs
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
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test sim6502-lsp-tests --filter "FullyQualifiedName~KickAssemblerSymbolParserTests" -v n`
Expected: All 6 tests PASS

**Step 5: Commit**

```bash
git add sim6502-lsp/Parsing/KickAssemblerSymbolParser.cs sim6502-lsp-tests/Parsing/KickAssemblerSymbolParserTests.cs
git commit -m "feat(lsp): implement KickAssembler symbol file parser"
```

---

## Phase 6: Hover and Go-to-Definition

### Task 9: Create HoverHandler

**Files:**
- Create: `sim6502-lsp/Handlers/HoverHandler.cs`
- Modify: `sim6502-lsp/Program.cs`

**Step 1: Implement HoverHandler**

```csharp
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
            DocumentSelector = new DocumentSelector(
                new DocumentFilter { Pattern = "**/*.6502" }
            )
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
```

**Step 2: Update Program.cs**

Add: `.WithHandler<HoverHandler>()`
Add to services: `services.AddSingleton<SymbolIndex>();`

**Step 3: Build to verify compilation**

Run: `dotnet build sim6502-lsp/sim6502-lsp.csproj`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add sim6502-lsp/Handlers/HoverHandler.cs sim6502-lsp/Program.cs
git commit -m "feat(lsp): implement HoverHandler for keywords and symbols"
```

---

### Task 10: Create DefinitionHandler

**Files:**
- Create: `sim6502-lsp/Handlers/DefinitionHandler.cs`
- Modify: `sim6502-lsp/Program.cs`

**Step 1: Implement DefinitionHandler**

```csharp
// sim6502-lsp/Handlers/DefinitionHandler.cs
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
            DocumentSelector = new DocumentSelector(
                new DocumentFilter { Pattern = "**/*.6502" }
            )
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

        // Check if it's a symbol reference (inside brackets)
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

            // Otherwise go to the symbol definition
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
```

**Step 2: Update Program.cs**

Add: `.WithHandler<DefinitionHandler>()`

**Step 3: Build to verify compilation**

Run: `dotnet build sim6502-lsp/sim6502-lsp.csproj`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add sim6502-lsp/Handlers/DefinitionHandler.cs sim6502-lsp/Program.cs
git commit -m "feat(lsp): implement DefinitionHandler for go-to-definition"
```

---

## Phase 7: VS Code Extension

### Task 11: Create VS Code Extension Scaffold

**Files:**
- Create: `sim6502-vscode/package.json`
- Create: `sim6502-vscode/src/extension.ts`
- Create: `sim6502-vscode/tsconfig.json`
- Create: `sim6502-vscode/language-configuration.json`
- Create: `sim6502-vscode/syntaxes/sim6502.tmLanguage.json`

**Step 1: Create package.json**

```json
{
  "name": "sim6502-vscode",
  "displayName": "sim6502 Language Support",
  "description": "Language support for sim6502 test files",
  "version": "0.1.0",
  "publisher": "barryw",
  "engines": {
    "vscode": "^1.85.0"
  },
  "categories": ["Programming Languages"],
  "activationEvents": [
    "onLanguage:sim6502"
  ],
  "main": "./out/extension.js",
  "contributes": {
    "languages": [{
      "id": "sim6502",
      "aliases": ["sim6502", "6502 Test"],
      "extensions": [".6502"],
      "configuration": "./language-configuration.json"
    }],
    "grammars": [{
      "language": "sim6502",
      "scopeName": "source.sim6502",
      "path": "./syntaxes/sim6502.tmLanguage.json"
    }],
    "configuration": {
      "type": "object",
      "title": "sim6502",
      "properties": {
        "sim6502.lspPath": {
          "type": "string",
          "default": "",
          "description": "Path to sim6502-lsp executable (uses bundled if empty)"
        },
        "sim6502.trace.server": {
          "type": "string",
          "enum": ["off", "messages", "verbose"],
          "default": "off",
          "description": "Traces the communication between VS Code and the sim6502 language server"
        }
      }
    }
  },
  "scripts": {
    "vscode:prepublish": "npm run compile",
    "compile": "tsc -p ./",
    "watch": "tsc -watch -p ./"
  },
  "dependencies": {
    "vscode-languageclient": "^9.0.1"
  },
  "devDependencies": {
    "@types/node": "^20.10.0",
    "@types/vscode": "^1.85.0",
    "typescript": "^5.3.0"
  }
}
```

**Step 2: Create tsconfig.json**

```json
{
  "compilerOptions": {
    "module": "commonjs",
    "target": "ES2022",
    "outDir": "out",
    "lib": ["ES2022"],
    "sourceMap": true,
    "rootDir": "src",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true
  },
  "exclude": ["node_modules", ".vscode-test"]
}
```

**Step 3: Create extension.ts**

```typescript
// sim6502-vscode/src/extension.ts
import * as path from 'path';
import * as vscode from 'vscode';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient;

export function activate(context: vscode.ExtensionContext) {
    const config = vscode.workspace.getConfiguration('sim6502');
    let serverPath = config.get<string>('lspPath');

    if (!serverPath) {
        // Default: assume dotnet run from project directory
        // In production, this would be a published executable
        serverPath = 'dotnet';
    }

    const serverOptions: ServerOptions = {
        run: {
            command: serverPath,
            args: serverPath === 'dotnet'
                ? ['run', '--project', findLspProject(context)]
                : [],
            transport: TransportKind.stdio
        },
        debug: {
            command: serverPath,
            args: serverPath === 'dotnet'
                ? ['run', '--project', findLspProject(context)]
                : [],
            transport: TransportKind.stdio
        }
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'sim6502' }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/*.6502')
        }
    };

    client = new LanguageClient(
        'sim6502',
        'sim6502 Language Server',
        serverOptions,
        clientOptions
    );

    client.start();
}

function findLspProject(context: vscode.ExtensionContext): string {
    // Try to find the LSP project relative to workspace
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    if (workspaceFolder) {
        return path.join(workspaceFolder.uri.fsPath, 'sim6502-lsp', 'sim6502-lsp.csproj');
    }
    // Fallback
    return 'sim6502-lsp/sim6502-lsp.csproj';
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}
```

**Step 4: Create language-configuration.json**

```json
{
  "comments": {
    "lineComment": ";"
  },
  "brackets": [
    ["{", "}"],
    ["[", "]"],
    ["(", ")"]
  ],
  "autoClosingPairs": [
    { "open": "{", "close": "}" },
    { "open": "[", "close": "]" },
    { "open": "(", "close": ")" },
    { "open": "\"", "close": "\"" }
  ],
  "surroundingPairs": [
    { "open": "{", "close": "}" },
    { "open": "[", "close": "]" },
    { "open": "(", "close": ")" },
    { "open": "\"", "close": "\"" }
  ]
}
```

**Step 5: Create TextMate grammar**

```json
{
  "name": "sim6502",
  "scopeName": "source.sim6502",
  "patterns": [
    { "include": "#comments" },
    { "include": "#strings" },
    { "include": "#keywords" },
    { "include": "#functions" },
    { "include": "#registers" },
    { "include": "#numbers" },
    { "include": "#symbols" }
  ],
  "repository": {
    "comments": {
      "patterns": [{
        "name": "comment.line.semicolon.sim6502",
        "match": ";.*$"
      }]
    },
    "strings": {
      "patterns": [{
        "name": "string.quoted.double.sim6502",
        "begin": "\"",
        "end": "\""
      }]
    },
    "keywords": {
      "patterns": [{
        "name": "keyword.control.sim6502",
        "match": "\\b(suites|suite|test|setup|assert|jsr|load|symbols|system|processor|rom)\\b"
      }, {
        "name": "keyword.other.sim6502",
        "match": "\\b(stop_on_rts|stop_on_address|fail_on_brk|skip|trace|timeout|tags|address|strip_header)\\b"
      }, {
        "name": "constant.language.sim6502",
        "match": "\\b(true|false|c64|generic_6502|generic_6510|generic_65c02|6502|6510|65c02)\\b"
      }]
    },
    "functions": {
      "patterns": [{
        "name": "support.function.sim6502",
        "match": "\\b(peekbyte|peekword|memcmp|memchk|memfill|memdump)\\b"
      }]
    },
    "registers": {
      "patterns": [{
        "name": "variable.language.register.sim6502",
        "match": "\\b[aAxXyY]\\b"
      }, {
        "name": "variable.language.flag.sim6502",
        "match": "\\b[cCnNzZdDvV]\\b"
      }]
    },
    "numbers": {
      "patterns": [{
        "name": "constant.numeric.hex.sim6502",
        "match": "\\$[0-9a-fA-F]+"
      }, {
        "name": "constant.numeric.binary.sim6502",
        "match": "%[01]+"
      }, {
        "name": "constant.numeric.decimal.sim6502",
        "match": "\\b[0-9]+\\b"
      }]
    },
    "symbols": {
      "patterns": [{
        "name": "variable.other.symbol.sim6502",
        "match": "\\[[a-zA-Z_][a-zA-Z0-9_.]*\\]"
      }]
    }
  }
}
```

**Step 6: Initialize npm and install dependencies**

Run: `cd sim6502-vscode && npm install`

**Step 7: Compile TypeScript**

Run: `cd sim6502-vscode && npm run compile`
Expected: No errors, `out/extension.js` created

**Step 8: Commit**

```bash
git add sim6502-vscode/
git commit -m "feat(vscode): create VS Code extension with syntax highlighting"
```

---

## Phase 8: Integration and Testing

### Task 12: Run Full Test Suite

**Step 1: Run all LSP tests**

Run: `dotnet test sim6502-lsp-tests -v n`
Expected: All tests PASS

**Step 2: Run all sim6502 tests (no regressions)**

Run: `dotnet test sim6502tests -v n`
Expected: All tests PASS

**Step 3: Manual test in VS Code**

1. Open VS Code in the sim6502 directory
2. Press F5 to launch Extension Development Host
3. Open a `.6502` file
4. Verify:
   - Syntax highlighting works
   - Errors appear as red squiggles
   - Completion popup shows keywords
   - Hover shows documentation

**Step 4: Commit any fixes**

```bash
git add -A
git commit -m "test: verify full integration"
```

---

### Task 13: Final Documentation

**Files:**
- Update: `README.md`

**Step 1: Add LSP section to README**

Add a section documenting:
- How to install the VS Code extension
- Available features
- Configuration options

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add language server documentation to README"
```

---

## Summary

This plan implements the language server in 13 tasks across 8 phases:

1. **Phase 1 (Tasks 1-2)**: Project setup
2. **Phase 2 (Task 3)**: Document manager
3. **Phase 3 (Tasks 4-5)**: Diagnostics
4. **Phase 4 (Task 6)**: Completion
5. **Phase 5 (Tasks 7-8)**: Symbol index
6. **Phase 6 (Tasks 9-10)**: Hover and go-to-definition
7. **Phase 7 (Task 11)**: VS Code extension
8. **Phase 8 (Tasks 12-13)**: Integration and documentation

Total new files: ~20
Total new tests: ~25+
