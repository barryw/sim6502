# DSL Enhancements Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add memfill, memdump, setup blocks, test options, and CLI filtering to the sim6502 test framework.

**Architecture:** Extend ANTLR grammar with new tokens/rules, implement handlers in SimBaseListener, add CLI options using CommandLine library. TDD approach - grammar tests first, then listener tests.

**Tech Stack:** C# 10, ANTLR4, xUnit, CommandLine library

---

## Phase 1: memfill and memdump Functions

### Task 1: Add Grammar Tokens for memfill/memdump

**Files:**
- Modify: `sim6502/Grammar/sim6502.g4`

**Step 1: Add new tokens**

Add after line 320 (after `MemoryChk`):

```antlr
MemFill:        'memfill';
MemDump:        'memdump';
```

**Step 2: Add parser rules**

Add after `memoryChkFunction` rule (around line 155):

```antlr
memFillFunction
    : MemFill LParen expression Comma expression Comma expression RParen
    ;

memDumpFunction
    : MemDump LParen expression Comma expression RParen
    ;
```

**Step 3: Add to testContents rule**

Modify `testContents` (line 135) to include new functions:

```antlr
testContents
    : assertFunction
    | assignment
    | jsrFunction
    | memFillFunction
    | memDumpFunction
    ;
```

**Step 4: Regenerate parser**

Run: `dotnet build sim6502/sim6502.csproj`

This triggers ANTLR to regenerate the parser files.

**Step 5: Commit**

```bash
git add sim6502/Grammar/sim6502.g4
git commit -m "feat(grammar): add memfill and memdump tokens and rules"
```

---

### Task 2: Add Grammar Test for memfill

**Files:**
- Create: `sim6502tests/GrammarTests/test-13.txt`
- Modify: `sim6502tests/sim6502tests.csproj` (add file to copy)

**Step 1: Create test file**

```
suites {
  suite("Test Suite 13 - memfill/memdump") {
    symbols("TestPrograms/include_me_full.sym")
    load("TestPrograms/include_me_full.prg", strip_header = true)

    test("memfill-basic", "memfill fills memory with value") {
      memfill($3000, 16, $aa)

      assert($3000 == $aa, "First byte should be $aa")
      assert($3001 == $aa, "Second byte should be $aa")
      assert($300f == $aa, "Last byte should be $aa")
      assert($3010 <> $aa, "Byte after should be unchanged")
    }

    test("memfill-symbol", "memfill works with symbols") {
      memfill([Loc1], 4, $55)

      assert([Loc1] == $55, "Symbol address should be filled")
    }

    test("memdump-basic", "memdump executes without error") {
      $4000 = $41
      $4001 = $42
      $4002 = $43
      memdump($4000, 8)

      ; memdump is for debugging - just verify it doesn't crash
      assert($4000 == $41, "Memory unchanged after memdump")
    }
  }
}
```

**Step 2: Add to csproj**

Add to `sim6502tests/sim6502tests.csproj` in the `<ItemGroup>` with other test files:

```xml
<None Update="GrammarTests\test-13.txt">
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
</None>
```

**Step 3: Run test to verify it fails**

Run: `dotnet test --filter "GrammarTests" -v normal`

Expected: FAIL (memfill/memdump not implemented in listener yet)

**Step 4: Commit**

```bash
git add sim6502tests/GrammarTests/test-13.txt sim6502tests/sim6502tests.csproj
git commit -m "test(grammar): add memfill/memdump grammar test (red)"
```

---

### Task 3: Implement memfill in Listener

**Files:**
- Modify: `sim6502/Grammar/SimBaseListener.cs`

**Step 1: Add ExitMemFillFunction method**

Add to SimBaseListener class:

```csharp
public override void ExitMemFillFunction(sim6502Parser.MemFillFunctionContext ctx)
{
    var address = _intValues.Get(ctx.expression(0));
    var count = _intValues.Get(ctx.expression(1));
    var value = _intValues.Get(ctx.expression(2));

    Logger.Debug($"memfill(${address:X4}, {count}, ${value:X2})");

    for (var i = 0; i < count; i++)
    {
        Proc.WriteMemoryValueWithoutIncrement(address + i, (byte)(value & 0xFF));
    }
}
```

**Step 2: Run test to verify memfill passes**

Run: `dotnet test --filter "test-13" -v normal`

Expected: memfill tests PASS, memdump test FAIL

**Step 3: Commit**

```bash
git add sim6502/Grammar/SimBaseListener.cs
git commit -m "feat(listener): implement memfill function"
```

---

### Task 4: Implement memdump in Listener

**Files:**
- Modify: `sim6502/Grammar/SimBaseListener.cs`

**Step 1: Add ExitMemDumpFunction method**

Add to SimBaseListener class:

```csharp
public override void ExitMemDumpFunction(sim6502Parser.MemDumpFunctionContext ctx)
{
    var address = _intValues.Get(ctx.expression(0));
    var count = _intValues.Get(ctx.expression(1));

    var symbolName = Symbols?.GetSymbolByAddress(address);
    var header = symbolName != null
        ? $"[memdump] ${address:X4} ({symbolName}), {count} bytes:"
        : $"[memdump] ${address:X4}, {count} bytes:";

    Console.WriteLine(header);

    for (var offset = 0; offset < count; offset += 8)
    {
        var lineAddr = address + offset;
        var bytes = new System.Text.StringBuilder();
        var ascii = new System.Text.StringBuilder();

        for (var i = 0; i < 8 && offset + i < count; i++)
        {
            var b = Proc.ReadMemoryValueWithoutCycle(lineAddr + i);
            bytes.Append($"{b:X2} ");
            ascii.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
        }

        Console.WriteLine($"{lineAddr:X4}: {bytes,-24}|  {ascii}|");
    }
}
```

**Step 2: Run all grammar tests**

Run: `dotnet test --filter "GrammarTests" -v normal`

Expected: All tests PASS including test-13

**Step 3: Commit**

```bash
git add sim6502/Grammar/SimBaseListener.cs
git commit -m "feat(listener): implement memdump function with hex dump output"
```

---

## Phase 2: Setup Block and Default Timeout

### Task 5: Add Grammar for setup Block

**Files:**
- Modify: `sim6502/Grammar/sim6502.g4`

**Step 1: Add new tokens**

Add after `FailOnBRK` token (line 326):

```antlr
Setup:          'setup';
DefaultTimeout: 'default_timeout';
```

**Step 2: Add setup_block rule**

Add after `loadFunction` rule:

```antlr
setupBlock
    : Setup LBrace testContents* RBrace
    ;

defaultTimeoutStatement
    : DefaultTimeout Assign number
    ;
```

**Step 3: Modify suite rule to include setup**

Change the `suite` rule (line 32) to:

```antlr
suite
    : Suite LParen suiteName RParen LBrace (testFunction | symbolsFunction | loadFunction | setupBlock | defaultTimeoutStatement)+ RBrace
    ;
```

**Step 4: Regenerate and build**

Run: `dotnet build sim6502/sim6502.csproj`

**Step 5: Commit**

```bash
git add sim6502/Grammar/sim6502.g4
git commit -m "feat(grammar): add setup block and default_timeout"
```

---

### Task 6: Add Grammar Test for setup Block

**Files:**
- Create: `sim6502tests/GrammarTests/test-14.txt`
- Modify: `sim6502tests/sim6502tests.csproj`

**Step 1: Create test file**

```
suites {
  suite("Test Suite 14 - Setup Block") {
    symbols("TestPrograms/include_me_full.sym")
    load("TestPrograms/include_me_full.prg", strip_header = true)

    default_timeout = 1000000

    setup {
      $5000 = $42
      $5001 = $43
      a = $00
      x = $00
      y = $00
    }

    test("setup-runs-1", "Setup block runs before test") {
      assert($5000 == $42, "Setup should have set $5000")
      assert($5001 == $43, "Setup should have set $5001")

      ; Modify values
      $5000 = $99
    }

    test("setup-runs-2", "Setup block resets state for each test") {
      ; Previous test changed $5000, but setup should reset it
      assert($5000 == $42, "Setup should have reset $5000")
    }
  }
}
```

**Step 2: Add to csproj**

```xml
<None Update="GrammarTests\test-14.txt">
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
</None>
```

**Step 3: Run test to verify it fails**

Run: `dotnet test --filter "test-14" -v normal`

Expected: FAIL (setup not implemented yet)

**Step 4: Commit**

```bash
git add sim6502tests/GrammarTests/test-14.txt sim6502tests/sim6502tests.csproj
git commit -m "test(grammar): add setup block test (red)"
```

---

### Task 7: Implement setup Block in Listener

**Files:**
- Modify: `sim6502/Grammar/SimBaseListener.cs`

**Step 1: Add state fields**

Add to class fields (around line 55):

```csharp
// Setup block statements to execute before each test
private sim6502Parser.SetupBlockContext _setupBlockContext;

// Suite-level default timeout (cycles, 0 = no limit)
private long _suiteDefaultTimeout = 0;
```

**Step 2: Add ExitSetupBlock to capture setup**

```csharp
public override void ExitSetupBlock(sim6502Parser.SetupBlockContext ctx)
{
    _setupBlockContext = ctx;
    Logger.Debug("Setup block registered");
}
```

**Step 3: Add ExitDefaultTimeoutStatement**

```csharp
public override void ExitDefaultTimeoutStatement(sim6502Parser.DefaultTimeoutStatementContext ctx)
{
    _suiteDefaultTimeout = _intValues.Get(ctx.number());
    Logger.Debug($"Suite default timeout set to {_suiteDefaultTimeout} cycles");
}
```

**Step 4: Modify EnterTestFunction to execute setup**

Find the existing `EnterTestFunction` method and add setup execution after `ResetTest()`:

```csharp
public override void EnterTestFunction(sim6502Parser.TestFunctionContext ctx)
{
    // ... existing code ...
    ResetTest();

    // Execute setup block if defined
    if (_setupBlockContext != null)
    {
        Logger.Debug("Executing setup block");
        foreach (var content in _setupBlockContext.testContents())
        {
            ParseTreeWalker.Default.Walk(this, content);
        }
    }

    // ... rest of existing code ...
}
```

**Step 5: Reset setup in ResetSuite**

In `ResetSuite()` method, add:

```csharp
_setupBlockContext = null;
_suiteDefaultTimeout = 0;
```

**Step 6: Run tests**

Run: `dotnet test --filter "GrammarTests" -v normal`

Expected: All tests PASS including test-14

**Step 7: Commit**

```bash
git add sim6502/Grammar/SimBaseListener.cs
git commit -m "feat(listener): implement setup block execution before each test"
```

---

## Phase 3: Test Options (skip, timeout, tags, trace)

### Task 8: Add Grammar for Test Options

**Files:**
- Modify: `sim6502/Grammar/sim6502.g4`

**Step 1: Add new tokens**

Add after `DefaultTimeout`:

```antlr
Skip:           'skip';
Trace:          'trace';
Timeout:        'timeout';
Tags:           'tags';
```

**Step 2: Add test options rules**

Add after `testDescription`:

```antlr
testOptions
    : testOption (Comma testOption)*
    ;

testOption
    : Skip Assign boolean           # skipOption
    | Trace Assign boolean          # traceOption
    | Timeout Assign number         # timeoutOption
    | Tags Assign StringLiteral     # tagsOption
    ;
```

**Step 3: Modify testFunction to accept options**

Change `testFunction` rule:

```antlr
testFunction
    : Test LParen testName Comma testDescription (Comma testOptions)? RParen LBrace testContents+ RBrace
    ;
```

**Step 4: Build**

Run: `dotnet build sim6502/sim6502.csproj`

**Step 5: Commit**

```bash
git add sim6502/Grammar/sim6502.g4
git commit -m "feat(grammar): add test options (skip, trace, timeout, tags)"
```

---

### Task 9: Add Grammar Test for Test Options

**Files:**
- Create: `sim6502tests/GrammarTests/test-15.txt`
- Modify: `sim6502tests/sim6502tests.csproj`

**Step 1: Create test file**

```
suites {
  suite("Test Suite 15 - Test Options") {
    symbols("TestPrograms/include_me_full.sym")
    load("TestPrograms/include_me_full.prg", strip_header = true)

    default_timeout = 500000

    test("normal-test", "No options") {
      assert(true == true, "Should pass")
    }

    test("skipped-test", "This is skipped", skip = true) {
      ; This should not execute
      assert(false == true, "Should never run")
    }

    test("tagged-test", "Has tags", tags = "smoke,regression") {
      assert(true == true, "Should pass")
    }

    test("custom-timeout", "Override timeout", timeout = 100000) {
      assert(true == true, "Should pass")
    }

    test("no-timeout", "Disable timeout", timeout = 0) {
      assert(true == true, "Should pass")
    }
  }
}
```

**Step 2: Add to csproj**

```xml
<None Update="GrammarTests\test-15.txt">
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
</None>
```

**Step 3: Run test**

Run: `dotnet test --filter "test-15" -v normal`

Expected: FAIL (options not implemented)

**Step 4: Commit**

```bash
git add sim6502tests/GrammarTests/test-15.txt sim6502tests/sim6502tests.csproj
git commit -m "test(grammar): add test options test (red)"
```

---

### Task 10: Implement Test Options in Listener

**Files:**
- Modify: `sim6502/Grammar/SimBaseListener.cs`

**Step 1: Add state fields for test options**

```csharp
// Current test options
private bool _currentTestSkip;
private bool _currentTestTrace;
private long _currentTestTimeout;
private List<string> _currentTestTags = new List<string>();

// Skipped test counter
public int TotalTestsSkipped { get; private set; }
```

**Step 2: Add option exit handlers**

```csharp
public override void ExitSkipOption(sim6502Parser.SkipOptionContext ctx)
{
    _currentTestSkip = _boolValues.Get(ctx.boolean());
}

public override void ExitTraceOption(sim6502Parser.TraceOptionContext ctx)
{
    _currentTestTrace = _boolValues.Get(ctx.boolean());
}

public override void ExitTimeoutOption(sim6502Parser.TimeoutOptionContext ctx)
{
    _currentTestTimeout = _intValues.Get(ctx.number());
}

public override void ExitTagsOption(sim6502Parser.TagsOptionContext ctx)
{
    var tagsString = StripQuotes(ctx.StringLiteral().GetText());
    _currentTestTags = tagsString.Split(',').Select(t => t.Trim()).ToList();
}
```

**Step 3: Modify EnterTestFunction to handle skip**

At the beginning of `EnterTestFunction`, after getting test name:

```csharp
// Reset test options to defaults
_currentTestSkip = false;
_currentTestTrace = false;
_currentTestTimeout = _suiteDefaultTimeout;
_currentTestTags.Clear();

// Parse options first (they're already visited by ANTLR walk)
// Check if test should be skipped
if (_currentTestSkip)
{
    TotalTestsSkipped++;
    Logger.Info($"SKIPPED: {testName}");
    return; // Skip rest of test execution
}
```

**Step 4: Add skip to ResetSuite stats**

```csharp
var totalTests = TotalTestsFailed + TotalTestsPassed + TotalTestsSkipped;
Logger.Log(logLevel, $"{TotalTestsPassed} passed, {TotalTestsFailed} failed, {TotalTestsSkipped} skipped of {totalTests} tests in suite '{CurrentSuite}'.");
TotalTestsSkipped = 0;
```

**Step 5: Run tests**

Run: `dotnet test --filter "GrammarTests" -v normal`

Expected: All tests PASS

**Step 6: Commit**

```bash
git add sim6502/Grammar/SimBaseListener.cs
git commit -m "feat(listener): implement test options (skip, trace, timeout, tags)"
```

---

## Phase 4: CLI Filtering

### Task 11: Add CLI Options for Filtering

**Files:**
- Modify: `sim6502/Sim6502CLI.cs`

**Step 1: Add new options to Options class**

```csharp
[Option('f', "filter", Required = false, HelpText = "Glob pattern for test names (e.g., 'castle*')")]
public string Filter { get; set; }

[Option("test", Required = false, HelpText = "Run single test by exact name")]
public string TestName { get; set; }

[Option("filter-tag", Required = false, HelpText = "Comma-separated tags (OR logic)")]
public string FilterTag { get; set; }

[Option("exclude-tag", Required = false, HelpText = "Exclude tests with these tags")]
public string ExcludeTag { get; set; }

[Option('l', "list", Required = false, Default = false, HelpText = "List matching tests without running")]
public bool ListOnly { get; set; }
```

**Step 2: Pass options to listener**

In `RunTests`, before `walker.Walk`:

```csharp
var sbl = new SimBaseListener
{
    FilterPattern = opts.Filter,
    ExactTestName = opts.TestName,
    FilterTags = string.IsNullOrEmpty(opts.FilterTag)
        ? new List<string>()
        : opts.FilterTag.Split(',').Select(t => t.Trim()).ToList(),
    ExcludeTags = string.IsNullOrEmpty(opts.ExcludeTag)
        ? new List<string>()
        : opts.ExcludeTag.Split(',').Select(t => t.Trim()).ToList(),
    ListOnly = opts.ListOnly
};
```

**Step 3: Commit**

```bash
git add sim6502/Sim6502CLI.cs
git commit -m "feat(cli): add filter, test, filter-tag, exclude-tag, list options"
```

---

### Task 12: Implement Filtering in Listener

**Files:**
- Modify: `sim6502/Grammar/SimBaseListener.cs`

**Step 1: Add filter properties**

```csharp
// CLI filtering options
public string FilterPattern { get; set; }
public string ExactTestName { get; set; }
public List<string> FilterTags { get; set; } = new List<string>();
public List<string> ExcludeTags { get; set; } = new List<string>();
public bool ListOnly { get; set; }
```

**Step 2: Add glob matching helper**

```csharp
private static bool GlobMatch(string text, string pattern)
{
    // Simple glob: * matches any chars, ? matches single char
    var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
        .Replace("\\*", ".*")
        .Replace("\\?", ".") + "$";
    return System.Text.RegularExpressions.Regex.IsMatch(text, regex,
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}

private bool ShouldRunTest(string testName)
{
    // Exact match takes priority
    if (!string.IsNullOrEmpty(ExactTestName))
        return testName.Equals(ExactTestName, StringComparison.OrdinalIgnoreCase);

    // Glob filter
    if (!string.IsNullOrEmpty(FilterPattern) && !GlobMatch(testName, FilterPattern))
        return false;

    // Tag inclusion (OR logic)
    if (FilterTags.Any() && !FilterTags.Any(t => _currentTestTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
        return false;

    // Tag exclusion
    if (ExcludeTags.Any(t => _currentTestTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
        return false;

    return true;
}
```

**Step 3: Modify EnterTestFunction to apply filter**

After parsing options, before skip check:

```csharp
// Check if test passes filters
if (!ShouldRunTest(testName))
{
    Logger.Debug($"Filtered out: {testName}");
    return;
}

// List mode - just print test name
if (ListOnly)
{
    var tagsStr = _currentTestTags.Any() ? $" [{string.Join(", ", _currentTestTags)}]" : "";
    Console.WriteLine($"{testName}{tagsStr}");
    return;
}
```

**Step 4: Run all tests**

Run: `dotnet test --filter "Category!=Slow" -v normal`

Expected: All tests PASS

**Step 5: Commit**

```bash
git add sim6502/Grammar/SimBaseListener.cs
git commit -m "feat(listener): implement test filtering with glob patterns and tags"
```

---

## Phase 5: Trace Output (Failure-Only)

### Task 13: Add Trace Buffering Infrastructure

**Files:**
- Modify: `sim6502/Grammar/SimBaseListener.cs`
- Modify: `sim6502/Proc/Processor.Core.cs`

**Step 1: Add trace callback to Processor**

In `Processor.Core.cs`, add property:

```csharp
/// <summary>
/// Callback invoked after each instruction for tracing
/// </summary>
public Action<int, string, Processor> TraceCallback { get; set; }
```

**Step 2: Call trace callback in NextStep**

In `Processor.Execution.cs`, in `NextStep()` after `ExecuteOpCode()`:

```csharp
// Invoke trace callback if set
TraceCallback?.Invoke(instructionPC, CurrentDisassembly?.DisassemblyOutput ?? "???", this);
```

(Note: `instructionPC` should be captured before PC changes)

**Step 3: Add trace buffer to listener**

```csharp
private readonly List<string> _traceBuffer = new List<string>();
private const int MaxTraceLines = 1000; // Limit buffer size

private string FormatTraceEntry(int pc, string disasm, Processor p)
{
    var flags = string.Concat(
        p.NegativeFlag ? 'N' : 'n',
        p.OverflowFlag ? 'V' : 'v',
        '-',
        p.DecimalFlag ? 'D' : 'd',
        p.DisableInterruptFlag ? 'I' : 'i',
        p.ZeroFlag ? 'Z' : 'z',
        p.CarryFlag ? 'C' : 'c'
    );

    return $"${pc:X4}: {disasm,-18} A=${p.Accumulator:X2} X=${p.XRegister:X2} Y=${p.YRegister:X2} SP=${p.StackPointer:X2} {flags}";
}
```

**Step 4: Enable tracing in test setup**

In `EnterTestFunction`, after skip/filter checks:

```csharp
// Clear trace buffer
_traceBuffer.Clear();

// Set up trace callback if trace enabled
if (_currentTestTrace)
{
    Proc.TraceCallback = (pc, disasm, p) =>
    {
        if (_traceBuffer.Count < MaxTraceLines)
            _traceBuffer.Add(FormatTraceEntry(pc, disasm, p));
    };
}
else
{
    Proc.TraceCallback = null;
}
```

**Step 5: Dump trace on failure**

In `ExitTestFunction`, when test fails:

```csharp
if (!TestPassed && _currentTestTrace && _traceBuffer.Any())
{
    Console.WriteLine($"\nExecution trace ({_traceBuffer.Count} instructions):");
    foreach (var line in _traceBuffer)
        Console.WriteLine(line);
}
```

**Step 6: Commit**

```bash
git add sim6502/Grammar/SimBaseListener.cs sim6502/Proc/Processor.Core.cs sim6502/Proc/Processor.Execution.cs
git commit -m "feat(trace): implement failure-only execution trace output"
```

---

### Task 14: Add Trace Test

**Files:**
- Create: `sim6502tests/GrammarTests/test-16.txt`
- Modify: `sim6502tests/sim6502tests.csproj`

**Step 1: Create test file**

```
suites {
  suite("Test Suite 16 - Trace Output") {
    symbols("TestPrograms/include_me_full.sym")
    load("TestPrograms/include_me_full.prg", strip_header = true)

    test("trace-pass", "Trace on passing test shows nothing", trace = true) {
      a = $42
      assert(a == $42, "A should be $42")
    }

    ; Note: This test intentionally fails to verify trace output
    ; Uncomment to manually verify trace output format:
    ; test("trace-fail", "Trace on failing test shows trace", trace = true) {
    ;   jsr([Loc1], stop_on_rts = true, fail_on_brk = true)
    ;   assert(a == $ff, "Intentional failure to see trace")
    ; }
  }
}
```

**Step 2: Add to csproj**

```xml
<None Update="GrammarTests\test-16.txt">
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
</None>
```

**Step 3: Run tests**

Run: `dotnet test --filter "GrammarTests" -v normal`

Expected: All tests PASS

**Step 4: Commit**

```bash
git add sim6502tests/GrammarTests/test-16.txt sim6502tests/sim6502tests.csproj
git commit -m "test(grammar): add trace output test"
```

---

## Phase 6: Final Integration and Cleanup

### Task 15: Integration Test and Documentation

**Files:**
- Update: `docs/plans/2026-01-27-dsl-enhancements.md` (mark complete)

**Step 1: Run full test suite**

Run: `dotnet test --filter "Category!=Slow" -v normal`

Expected: All tests PASS

**Step 2: Run Klaus Dormann test (optional validation)**

Run: `dotnet test --filter "AllOpcodes_ShouldPassFunctionalTest" -v normal`

Expected: PASS

**Step 3: Manual CLI test**

```bash
# Build
dotnet build sim6502/sim6502.csproj

# Test filtering
dotnet run --project sim6502 -- -s sim6502tests/bin/Debug/net10.0/GrammarTests/test-15.txt --filter "tagged*"
dotnet run --project sim6502 -- -s sim6502tests/bin/Debug/net10.0/GrammarTests/test-15.txt --list
```

**Step 4: Final commit**

```bash
git add -A
git commit -m "feat(dsl): complete DSL enhancements

- memfill(addr, count, value) for memory initialization
- memdump(addr, count) for debugging with hex dump output
- setup {} block for suite-level initialization
- Test options: skip, trace, timeout (cycles), tags
- CLI filtering: --filter, --test, --filter-tag, --exclude-tag, --list
- Failure-only trace output for debugging

Closes #N (if applicable)"
```

**Step 5: Push**

```bash
git push
```

---

## Summary

| Phase | Tasks | Features |
|-------|-------|----------|
| 1 | 1-4 | memfill, memdump |
| 2 | 5-7 | setup block, default_timeout |
| 3 | 8-10 | skip, trace, timeout, tags options |
| 4 | 11-12 | CLI filtering |
| 5 | 13-14 | Trace buffering and output |
| 6 | 15 | Integration test |

Total: 15 tasks, ~2-3 hours estimated
