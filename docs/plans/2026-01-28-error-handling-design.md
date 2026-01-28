# Error Handling Design

## Overview

Comprehensive error handling for the sim6502 test DSL parser, providing rich contextual error messages with "Did you mean?" suggestions.

## Goals

- Collect **all errors** in a single run (don't fail fast)
- Show **rich contextual output** with surrounding source lines
- Provide **"Did you mean?"** suggestions for typos in keywords and symbols
- Cover all phases: lexer, parser, semantic validation, runtime

## Error Model

### SimError Class

```csharp
public class SimError
{
    public ErrorSeverity Severity { get; }      // Error, Warning
    public ErrorPhase Phase { get; }            // Lexer, Parser, Semantic, Runtime
    public string FilePath { get; }             // Source file
    public int Line { get; }                    // 1-based line number
    public int Column { get; }                  // 0-based column
    public int Length { get; }                  // Length of offending token/span
    public string Message { get; }              // "unknown function 'jrs'"
    public string? Hint { get; }                // "Did you mean 'jsr'?"
}

public enum ErrorSeverity { Warning, Error }
public enum ErrorPhase { Lexer, Parser, Semantic, Runtime }
```

### ErrorCollector Class

```csharp
public class ErrorCollector
{
    private readonly List<SimError> _errors = new();
    private string[] _sourceLines;
    private string _filePath;

    public void SetSource(string content, string filePath);
    public void AddError(ErrorPhase phase, int line, int col, int length, string message, string? hint);
    public void AddWarning(ErrorPhase phase, int line, int col, int length, string message, string? hint);
    public bool HasErrors { get; }
    public bool HasWarnings { get; }
    public IReadOnlyList<SimError> Errors { get; }
    public string[] SourceLines { get; }
}
```

## Error Rendering

### Output Format

```
Error at test.txt:5:12 - unknown function 'jrs'
    4 |       $fd = $00
    5 |       jrs([FillMemory], stop_on_rts = true)
      |       ^^^
    6 |       assert(A == $00)
  Hint: Did you mean 'jsr'?
```

### Rendering Rules

- Line numbers right-aligned and padded
- Error line highlighted with `^^^` pointer spanning token length
- 1 context line above and below
- Hints indented and prefixed
- Blank line between errors
- Summary footer: "Found 3 errors and 1 warning in test.txt"

## Suggestion Engine

Uses Levenshtein distance to find similar matches:

```csharp
public static class SuggestionEngine
{
    // Built-in keywords
    private static readonly string[] Keywords = {
        "jsr", "assert", "memfill", "memdump", "peekbyte", "peekword",
        "memorycmp", "memorychk", "load", "symbols", "test", "suite",
        "setup", "suites", "stop_on_rts", "stop_on_address", "fail_on_brk"
    };

    private static readonly string[] Registers = { "A", "X", "Y", "SP", "PC" };
    private static readonly string[] Flags = { "C", "Z", "I", "D", "B", "V", "N" };

    public static string? SuggestKeyword(string input);
    public static string? SuggestSymbol(string input, IDictionary<string, int> symbols);
}
```

### Thresholds

- Keywords: max distance 2 (they're short)
- Symbols: max distance 3 (user-defined, can be longer)
- Only suggest if exactly one close match found

## Integration

### ANTLR Error Listener

```csharp
public class SimErrorListener : BaseErrorListener, IAntlrErrorListener<int>
{
    private readonly ErrorCollector _collector;

    public override void SyntaxError(...);  // Parser errors
    void IAntlrErrorListener<int>.SyntaxError(...);  // Lexer errors
}
```

### SimBaseListener Changes

- Accept `ErrorCollector` in constructor
- Report semantic errors during tree walk
- Continue walking after errors to find more

### CLI Flow

```csharp
private static int RunTests(Options opts)
{
    var collector = new ErrorCollector();
    collector.SetSource(File.ReadAllText(opts.SuiteFile), opts.SuiteFile);

    // Setup lexer/parser with error listeners
    // Parse tree
    // If parse errors: report and exit
    // Walk tree for semantic validation
    // If semantic errors: report and exit
    // Return test results
}
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success - all tests passed |
| 1 | Test failure - tests ran but some failed |
| 2 | Parse error - syntax errors in test file |
| 3 | Semantic error - undefined symbols, invalid values |

## Semantic Validations

| Check | Example | Error Message |
|-------|---------|---------------|
| Undefined symbol | `[FillMem]` | "undefined symbol 'FillMem'" |
| Invalid address | `$FFFFF = $00` | "address $FFFFF out of range (valid: $0000-$FFFF)" |
| Invalid byte value | `$c000 = $1FF` | "value $1FF too large for byte (max $FF)" |
| File not found | `load("missing.prg")` | "file not found: 'missing.prg'" |
| Empty file | `load("empty.prg")` | "file is empty: 'empty.prg'" |
| Invalid register | `Q = $00` | "unknown register 'Q'" |
| Invalid flag | `G = true` | "unknown flag 'G'" |
| Duplicate test name | Two `test("foo")` | "duplicate test name 'foo' in suite" |
| Missing jsr in test | `test` with no `jsr` | Warning: "test has no jsr call" |

## File Structure

```
sim6502/
├── Errors/
│   ├── SimError.cs           # Error model
│   ├── ErrorCollector.cs     # Collects errors from all phases
│   ├── ErrorRenderer.cs      # Rich output formatting
│   └── SuggestionEngine.cs   # "Did you mean?" logic
├── Grammar/
│   ├── SimErrorListener.cs   # Rewrite to use collector
│   └── SimBaseListener.cs    # Add validation, inject collector
└── Sim6502CLI.cs             # Wire up collector, exit codes
```

## Test Coverage

```csharp
// SuggestionEngineTests.cs
[Fact] void SuggestKeyword_Typo_FindsMatch()
[Fact] void SuggestKeyword_TooFar_ReturnsNull()
[Fact] void SuggestSymbol_Close_FindsMatch()

// ErrorRendererTests.cs
[Fact] void Render_SingleError_ShowsContext()
[Fact] void Render_MultipleErrors_SortsByLine()
[Fact] void Render_ShowsHintWhenPresent()

// ErrorCollectorTests.cs
[Fact] void HasErrors_WhenEmpty_ReturnsFalse()
[Fact] void HasErrors_AfterAddError_ReturnsTrue()

// Integration tests with bad .txt files
[Fact] void Parse_SyntaxError_ReportsWithContext()
[Fact] void Parse_UndefinedSymbol_SuggestsClose()
```
