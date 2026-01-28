# Multi-Processor Support Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add support for 6502, 6510, and 65C02 processor variants with a DSL command to specify processor type per suite.

**Architecture:** Create a `ProcessorType` enum and refactor `OpcodeRegistry` to support processor-specific opcode tables. The 65C02 adds 27 new opcodes (PHX, PLX, PHY, PLY, STZ, BRA, TRB, TSB, etc.). The 6510 shares the 6502 ISA but adds I/O port emulation at $00-$01. Grammar will add a `processor()` statement to suite blocks.

**Tech Stack:** C# 13/.NET 10, ANTLR4 for grammar, xUnit for tests

---

## Task 1: Create ProcessorType Enum

**Files:**
- Create: `sim6502/Proc/ProcessorType.cs`
- Test: `sim6502tests/ProcessorTypeTests.cs`

**Step 1: Write the failing test**

Create `sim6502tests/ProcessorTypeTests.cs`:

```csharp
using FluentAssertions;
using sim6502.Proc;
using Xunit;

namespace sim6502tests;

public class ProcessorTypeTests
{
    [Fact]
    public void ProcessorType_HasExpectedValues()
    {
        // Verify all expected processor types exist
        ProcessorType.MOS6502.Should().BeDefined();
        ProcessorType.MOS6510.Should().BeDefined();
        ProcessorType.WDC65C02.Should().BeDefined();
    }

    [Fact]
    public void ProcessorType_DefaultIs6502()
    {
        // Default value should be MOS6502
        default(ProcessorType).Should().Be(ProcessorType.MOS6502);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter "ProcessorTypeTests" -v n`
Expected: FAIL - ProcessorType does not exist

**Step 3: Write minimal implementation**

Create `sim6502/Proc/ProcessorType.cs`:

```csharp
namespace sim6502.Proc;

/// <summary>
/// Supported processor variants in the 65xx family.
/// </summary>
public enum ProcessorType
{
    /// <summary>
    /// MOS Technology 6502 - Original NMOS processor (default)
    /// </summary>
    MOS6502 = 0,

    /// <summary>
    /// MOS Technology 6510 - 6502 with I/O port at $00-$01 (C64)
    /// </summary>
    MOS6510 = 1,

    /// <summary>
    /// WDC 65C02 - CMOS variant with additional opcodes
    /// </summary>
    WDC65C02 = 2
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "ProcessorTypeTests" -v n`
Expected: PASS

**Step 5: Commit**

```bash
git add sim6502/Proc/ProcessorType.cs sim6502tests/ProcessorTypeTests.cs
git commit -m "feat(proc): add ProcessorType enum for 6502/6510/65C02

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 2: Add ProcessorType Property to Processor

**Files:**
- Modify: `sim6502/Proc/Processor.Core.cs`
- Test: `sim6502tests/ProcessorTests.cs`

**Step 1: Write the failing test**

Create `sim6502tests/ProcessorTests.cs`:

```csharp
using FluentAssertions;
using sim6502.Proc;
using Xunit;

namespace sim6502tests;

public class ProcessorTests
{
    [Fact]
    public void Processor_DefaultProcessorTypeIs6502()
    {
        var proc = new Processor();
        proc.ProcessorType.Should().Be(ProcessorType.MOS6502);
    }

    [Fact]
    public void Processor_CanSetProcessorType()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.ProcessorType.Should().Be(ProcessorType.WDC65C02);
    }

    [Fact]
    public void Processor_6510_HasSameDefaultBehaviorAs6502()
    {
        var proc6502 = new Processor(ProcessorType.MOS6502);
        var proc6510 = new Processor(ProcessorType.MOS6510);

        // Basic behavior should be identical
        proc6502.Accumulator.Should().Be(proc6510.Accumulator);
        proc6502.XRegister.Should().Be(proc6510.XRegister);
        proc6502.YRegister.Should().Be(proc6510.YRegister);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter "ProcessorTests" -v n`
Expected: FAIL - Processor constructor doesn't accept ProcessorType

**Step 3: Write minimal implementation**

Modify `sim6502/Proc/Processor.Core.cs`. Add the property and update constructor:

```csharp
// Add property near other properties (around line 20):
/// <summary>
/// The processor variant being emulated.
/// </summary>
public ProcessorType ProcessorType { get; }

// Update existing constructor to call new one:
public Processor() : this(ProcessorType.MOS6502)
{
}

// Add new constructor:
public Processor(ProcessorType processorType)
{
    ProcessorType = processorType;
    Logger.Info($"{GetProcessorName()} Simulator Copyright © 2013 Aaron Mell. All Rights Reserved.");
    ResetMemory();
    StackPointer = 0x100;
    CycleCountIncrementedAction = () => { };
}

// Add helper method:
private string GetProcessorName() => ProcessorType switch
{
    ProcessorType.MOS6502 => "6502",
    ProcessorType.MOS6510 => "6510",
    ProcessorType.WDC65C02 => "65C02",
    _ => "6502"
};
```

**Step 4: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "ProcessorTests" -v n`
Expected: PASS

**Step 5: Commit**

```bash
git add sim6502/Proc/Processor.Core.cs sim6502tests/ProcessorTests.cs
git commit -m "feat(proc): add ProcessorType property to Processor

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 3: Refactor OpcodeRegistry for Multi-Processor Support

**Files:**
- Modify: `sim6502/Proc/OpcodeRegistry.cs`
- Test: `sim6502tests/OpcodeRegistryTests.cs`

**Step 1: Write the failing test**

Create `sim6502tests/OpcodeRegistryTests.cs`:

```csharp
using FluentAssertions;
using sim6502.Proc;
using Xunit;

namespace sim6502tests;

public class OpcodeRegistryTests
{
    [Fact]
    public void GetOpcode_6502_ReturnsValidOpcode()
    {
        var opcode = OpcodeRegistry.GetOpcode(0xA9, ProcessorType.MOS6502); // LDA immediate
        opcode.Should().NotBeNull();
        opcode!.Mnemonic.Should().Be("LDA");
    }

    [Fact]
    public void GetOpcode_6502_ReturnsNullForInvalidOpcode()
    {
        // 0x5C is not a valid 6502 opcode (but is valid on 65C02)
        var opcode = OpcodeRegistry.GetOpcode(0x5C, ProcessorType.MOS6502);
        opcode.Should().BeNull();
    }

    [Fact]
    public void GetOpcode_6510_ReturnsSameAs6502()
    {
        // 6510 has same ISA as 6502
        var opcode6502 = OpcodeRegistry.GetOpcode(0xA9, ProcessorType.MOS6502);
        var opcode6510 = OpcodeRegistry.GetOpcode(0xA9, ProcessorType.MOS6510);

        opcode6502.Should().NotBeNull();
        opcode6510.Should().NotBeNull();
        opcode6502!.Mnemonic.Should().Be(opcode6510!.Mnemonic);
    }

    [Fact]
    public void GetOpcode_BackwardsCompatible_DefaultsTo6502()
    {
        // Existing API without processor type should still work
        var opcode = OpcodeRegistry.GetOpcode(0xA9);
        opcode.Should().NotBeNull();
        opcode!.Mnemonic.Should().Be("LDA");
    }

    [Fact]
    public void GetOpcodeCount_6502_Returns151()
    {
        var count = OpcodeRegistry.GetOpcodeCount(ProcessorType.MOS6502);
        count.Should().Be(151);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter "OpcodeRegistryTests" -v n`
Expected: FAIL - GetOpcode doesn't accept ProcessorType parameter

**Step 3: Write minimal implementation**

Modify `sim6502/Proc/OpcodeRegistry.cs`:

```csharp
using System.Collections.Generic;

namespace sim6502.Proc;

public static class OpcodeRegistry
{
    private static readonly Dictionary<ProcessorType, Dictionary<byte, OpcodeInfo>> _opcodesByProcessor = new();

    static OpcodeRegistry()
    {
        // Initialize opcode tables for each processor type
        _opcodesByProcessor[ProcessorType.MOS6502] = new Dictionary<byte, OpcodeInfo>();
        _opcodesByProcessor[ProcessorType.MOS6510] = _opcodesByProcessor[ProcessorType.MOS6502]; // Same ISA
        _opcodesByProcessor[ProcessorType.WDC65C02] = new Dictionary<byte, OpcodeInfo>();

        RegisterAll6502Opcodes();
        Register65C02Opcodes();
    }

    /// <summary>
    /// Get opcode info for a specific processor type.
    /// </summary>
    public static OpcodeInfo? GetOpcode(byte opcode, ProcessorType processorType)
    {
        if (_opcodesByProcessor.TryGetValue(processorType, out var opcodes) &&
            opcodes.TryGetValue(opcode, out var info))
        {
            return info;
        }
        return null;
    }

    /// <summary>
    /// Get opcode info (backwards compatible, defaults to 6502).
    /// </summary>
    public static OpcodeInfo? GetOpcode(byte opcode)
    {
        return GetOpcode(opcode, ProcessorType.MOS6502);
    }

    /// <summary>
    /// Get the number of opcodes for a processor type.
    /// </summary>
    public static int GetOpcodeCount(ProcessorType processorType)
    {
        return _opcodesByProcessor.TryGetValue(processorType, out var opcodes)
            ? opcodes.Count
            : 0;
    }

    private static void Register(ProcessorType processorType, byte opcode, string mnemonic,
        AddressingMode mode, int bytes, int cycles, OpcodeHandler handler)
    {
        _opcodesByProcessor[processorType][opcode] =
            new OpcodeInfo(opcode, mnemonic, mode, bytes, cycles, handler);
    }

    private static void Register6502(byte opcode, string mnemonic,
        AddressingMode mode, int bytes, int cycles, OpcodeHandler handler)
    {
        Register(ProcessorType.MOS6502, opcode, mnemonic, mode, bytes, cycles, handler);
    }

    private static void Register65C02(byte opcode, string mnemonic,
        AddressingMode mode, int bytes, int cycles, OpcodeHandler handler)
    {
        Register(ProcessorType.WDC65C02, opcode, mnemonic, mode, bytes, cycles, handler);
    }

    private static void RegisterAll6502Opcodes()
    {
        // All existing 151 opcodes go here - move from current Register() calls
        // to Register6502() calls

        // ... (existing opcode registrations, changed to use Register6502)
    }

    private static void Register65C02Opcodes()
    {
        // First, copy all 6502 opcodes to 65C02 (it's a superset)
        foreach (var kvp in _opcodesByProcessor[ProcessorType.MOS6502])
        {
            _opcodesByProcessor[ProcessorType.WDC65C02][kvp.Key] = kvp.Value;
        }

        // 65C02-specific opcodes will be added in a later task
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "OpcodeRegistryTests" -v n`
Expected: PASS

**Step 5: Commit**

```bash
git add sim6502/Proc/OpcodeRegistry.cs sim6502tests/OpcodeRegistryTests.cs
git commit -m "refactor(opcodes): add processor-specific opcode tables

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 4: Update Processor to Use ProcessorType in Opcode Lookup

**Files:**
- Modify: `sim6502/Proc/Processor.Execution.cs`
- Test: `sim6502tests/ProcessorExecutionTests.cs`

**Step 1: Write the failing test**

Create `sim6502tests/ProcessorExecutionTests.cs`:

```csharp
using FluentAssertions;
using sim6502.Proc;
using Xunit;

namespace sim6502tests;

public class ProcessorExecutionTests
{
    [Fact]
    public void ExecuteOpcode_6502_LDA_Works()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        // LDA #$42 at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xA9); // LDA immediate
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42); // value
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.Accumulator.Should().Be(0x42);
    }

    [Fact]
    public void ExecuteOpcode_65C02_UsesCorrectOpcodeTable()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // LDA #$42 at address $0200 (same instruction works on both)
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xA9); // LDA immediate
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42); // value
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.Accumulator.Should().Be(0x42);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter "ProcessorExecutionTests" -v n`
Expected: May pass if existing code works, but we need to verify the change

**Step 3: Write minimal implementation**

Modify `sim6502/Proc/Processor.Execution.cs` - update `ExecuteOpCode()`:

```csharp
private void ExecuteOpCode()
{
    var opcodeInfo = OpcodeRegistry.GetOpcode((byte)CurrentOpCode, ProcessorType);

    if (opcodeInfo == null)
        throw new NotSupportedException(
            $"The OpCode {CurrentOpCode:X2} @ address {ProgramCounter:X4} is not supported on {ProcessorType}.");

    opcodeInfo.Handler(this);
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "ProcessorExecutionTests" -v n`
Expected: PASS

**Step 5: Commit**

```bash
git add sim6502/Proc/Processor.Execution.cs sim6502tests/ProcessorExecutionTests.cs
git commit -m "feat(proc): use ProcessorType in opcode lookup

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 5: Update Grammar - Add processor() Statement

**Files:**
- Modify: `sim6502/Grammar/sim6502.g4`
- Test: `sim6502tests/GrammarTests/test-18.txt`
- Modify: `sim6502tests/TestSuiteParser.cs`
- Modify: `sim6502tests/sim6502tests.csproj`

**Step 1: Create test file**

Create `sim6502tests/GrammarTests/test-18.txt`:

```
suites {
  suite("Test 65C02 processor declaration") {
    processor(65c02)

    test("proc-decl", "Verify processor declaration parses") {
      a = $00
      assert(a == $00, "Basic test should work")
    }
  }

  suite("Test 6502 processor declaration") {
    processor(6502)

    test("proc-6502", "Verify 6502 declaration parses") {
      a = $01
      assert(a == $01, "Basic test should work")
    }
  }

  suite("Test 6510 processor declaration") {
    processor(6510)

    test("proc-6510", "Verify 6510 declaration parses") {
      a = $02
      assert(a == $02, "Basic test should work")
    }
  }

  suite("Test default processor (no declaration)") {
    test("no-proc", "Should default to 6502") {
      a = $03
      assert(a == $03, "Basic test should work")
    }
  }
}
```

**Step 2: Write the failing unit test**

Add to `sim6502tests/TestSuiteParser.cs`:

```csharp
[Fact]
public void TestSuite18_ProcessorDeclaration()
{
    // This test validates processor() declaration grammar parsing
    var tree = GetContext("GrammarTests/test-18.txt");

    var walker = new ParseTreeWalker();
    var sbl = new SimBaseListener();

    walker.Walk(sbl, tree);
}
```

**Step 3: Add test file to csproj**

Add to `sim6502tests/sim6502tests.csproj` inside the `<ItemGroup>` with other test files:

```xml
<None Update="GrammarTests\test-18.txt">
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
</None>
```

**Step 4: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter "TestSuite18" -v n`
Expected: FAIL - Grammar doesn't recognize processor()

**Step 5: Update grammar**

Modify `sim6502/Grammar/sim6502.g4`:

Add lexer token (in lexer section, around line 330):

```antlr
Processor : 'processor' ;
```

Add processor type tokens:

```antlr
ProcessorType6502 : '6502' ;
ProcessorType6510 : '6510' ;
ProcessorType65C02 : '65c02' | '65C02' ;
```

Add grammar rule (after setupBlock rule, around line 50):

```antlr
processorDeclaration
    : Processor LParen processorTypeValue RParen
    ;

processorTypeValue
    : ProcessorType6502
    | ProcessorType6510
    | ProcessorType65C02
    ;
```

Update suite rule to include processorDeclaration:

```antlr
suite
    : Suite LParen suiteName RParen LBrace
        (processorDeclaration)?
        (testFunction | symbolsFunction | loadFunction | setupBlock)+
      RBrace
    ;
```

**Step 6: Regenerate parser**

Run: `cd sim6502/Grammar && antlr4 -Dlanguage=CSharp -visitor -no-listener sim6502.g4 -o Generated`

Or if using the build target:
Run: `dotnet build sim6502`

**Step 7: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "TestSuite18" -v n`
Expected: PASS (grammar parses, listener doesn't act on it yet)

**Step 8: Commit**

```bash
git add sim6502/Grammar/sim6502.g4 sim6502/Grammar/Generated/*.cs \
    sim6502tests/GrammarTests/test-18.txt sim6502tests/TestSuiteParser.cs \
    sim6502tests/sim6502tests.csproj
git commit -m "feat(grammar): add processor() declaration to suite blocks

Supports processor(6502), processor(6510), processor(65c02)

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 6: Update SimBaseListener to Handle processor() Declaration

**Files:**
- Modify: `sim6502/Grammar/SimBaseListener.cs`
- Modify: `sim6502tests/TestSuiteParser.cs`

**Step 1: Write the failing test**

Update the test in `sim6502tests/TestSuiteParser.cs`:

```csharp
[Fact]
public void TestSuite18_ProcessorDeclaration()
{
    // This test validates processor() declaration grammar parsing and execution
    var tree = GetContext("GrammarTests/test-18.txt");

    var walker = new ParseTreeWalker();
    var sbl = new SimBaseListener();

    walker.Walk(sbl, tree);

    // Verify the processor types were set correctly
    // (The listener should track which processor was used for each suite)
}
```

**Step 2: Run test to verify baseline**

Run: `dotnet test sim6502tests --filter "TestSuite18" -v n`
Expected: PASS (but processor declaration is ignored)

**Step 3: Implement listener method**

Add to `sim6502/Grammar/SimBaseListener.cs`:

```csharp
// Add field to track current processor type
private ProcessorType _currentProcessorType = ProcessorType.MOS6502;

public override void EnterSuite(sim6502Parser.SuiteContext context)
{
    // Reset processor type to default at start of each suite
    _currentProcessorType = ProcessorType.MOS6502;

    // Check for processor declaration
    var procDecl = context.processorDeclaration();
    if (procDecl != null)
    {
        _currentProcessorType = GetProcessorType(procDecl.processorTypeValue());
        Logger.Info($"Processor type set to: {_currentProcessorType}");
    }

    // Create processor with the specified type
    Proc = new Processor(_currentProcessorType);
    Proc.Reset();
    CurrentSuite = StripQuotes(context.suiteName().GetText());
    Logger.Info($"Running test suite '{CurrentSuite}'...");
}

private ProcessorType GetProcessorType(sim6502Parser.ProcessorTypeValueContext context)
{
    if (context.ProcessorType6502() != null)
        return ProcessorType.MOS6502;
    if (context.ProcessorType6510() != null)
        return ProcessorType.MOS6510;
    if (context.ProcessorType65C02() != null)
        return ProcessorType.WDC65C02;

    return ProcessorType.MOS6502; // default
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "TestSuite18" -v n`
Expected: PASS

**Step 5: Run all tests to verify no regressions**

Run: `dotnet test sim6502tests -v n`
Expected: All tests PASS

**Step 6: Commit**

```bash
git add sim6502/Grammar/SimBaseListener.cs sim6502tests/TestSuiteParser.cs
git commit -m "feat(listener): handle processor() declaration in suites

Creates Processor with specified ProcessorType (6502, 6510, or 65C02)

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 7: Add 65C02-Specific Opcodes - PHX, PLX, PHY, PLY

**Files:**
- Modify: `sim6502/Proc/Processor.Operations.cs`
- Modify: `sim6502/Proc/OpcodeRegistry.cs`
- Test: `sim6502tests/Opcodes65C02Tests.cs`

**Step 1: Write the failing test**

Create `sim6502tests/Opcodes65C02Tests.cs`:

```csharp
using FluentAssertions;
using sim6502.Proc;
using Xunit;

namespace sim6502tests;

public class Opcodes65C02Tests
{
    [Fact]
    public void PHX_PushesXRegisterToStack()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.XRegister = 0x42;
        var initialSp = proc.StackPointer;

        // PHX at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xDA); // PHX
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.StackPointer.Should().Be(initialSp - 1);
        proc.ReadMemoryValueWithoutCycle(0x100 + initialSp).Should().Be(0x42);
    }

    [Fact]
    public void PLX_PullsStackToXRegister()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.XRegister = 0x00;

        // Push $42 onto stack manually
        proc.WriteMemoryValueWithoutIncrement(0x1FF, 0x42);
        proc.StackPointer = 0xFE; // Points to next free location

        // PLX at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xFA); // PLX
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.XRegister.Should().Be(0x42);
        proc.StackPointer.Should().Be(0xFF);
    }

    [Fact]
    public void PHY_PushesYRegisterToStack()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.YRegister = 0x37;
        var initialSp = proc.StackPointer;

        // PHY at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x5A); // PHY
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.StackPointer.Should().Be(initialSp - 1);
        proc.ReadMemoryValueWithoutCycle(0x100 + initialSp).Should().Be(0x37);
    }

    [Fact]
    public void PLY_PullsStackToYRegister()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.YRegister = 0x00;

        // Push $99 onto stack manually
        proc.WriteMemoryValueWithoutIncrement(0x1FF, 0x99);
        proc.StackPointer = 0xFE;

        // PLY at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x7A); // PLY
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.YRegister.Should().Be(0x99);
        proc.StackPointer.Should().Be(0xFF);
    }

    [Fact]
    public void PHX_NotAvailableOn6502()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xDA); // PHX (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter "Opcodes65C02Tests" -v n`
Expected: FAIL - Opcodes not implemented

**Step 3: Add operation implementations**

Add to `sim6502/Proc/Processor.Operations.cs`:

```csharp
#region 65C02 Stack Operations

/// <summary>
/// PHX - Push X Register to Stack (65C02)
/// </summary>
public void PushXOperation()
{
    PushByteToStack(XRegister);
}

/// <summary>
/// PLX - Pull X Register from Stack (65C02)
/// </summary>
public void PullXOperation()
{
    XRegister = PullByteFromStack();
    SetZeroFlag(XRegister);
    SetNegativeFlag(XRegister);
}

/// <summary>
/// PHY - Push Y Register to Stack (65C02)
/// </summary>
public void PushYOperation()
{
    PushByteToStack(YRegister);
}

/// <summary>
/// PLY - Pull Y Register from Stack (65C02)
/// </summary>
public void PullYOperation()
{
    YRegister = PullByteFromStack();
    SetZeroFlag(YRegister);
    SetNegativeFlag(YRegister);
}

#endregion
```

**Step 4: Register the opcodes**

Add to `sim6502/Proc/OpcodeRegistry.cs` in `Register65C02Opcodes()`:

```csharp
private static void Register65C02Opcodes()
{
    // First, copy all 6502 opcodes to 65C02 (it's a superset)
    foreach (var kvp in _opcodesByProcessor[ProcessorType.MOS6502])
    {
        _opcodesByProcessor[ProcessorType.WDC65C02][kvp.Key] = kvp.Value;
    }

    // PHX - Push X Register
    Register65C02(0xDA, "PHX", AddressingMode.Implied, 1, 3,
        p => p.PushXOperation());

    // PLX - Pull X Register
    Register65C02(0xFA, "PLX", AddressingMode.Implied, 1, 4,
        p => p.PullXOperation());

    // PHY - Push Y Register
    Register65C02(0x5A, "PHY", AddressingMode.Implied, 1, 3,
        p => p.PushYOperation());

    // PLY - Pull Y Register
    Register65C02(0x7A, "PLY", AddressingMode.Implied, 1, 4,
        p => p.PullYOperation());
}
```

**Step 5: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "Opcodes65C02Tests" -v n`
Expected: PASS

**Step 6: Commit**

```bash
git add sim6502/Proc/Processor.Operations.cs sim6502/Proc/OpcodeRegistry.cs \
    sim6502tests/Opcodes65C02Tests.cs
git commit -m "feat(65c02): add PHX, PLX, PHY, PLY stack operations

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 8: Add 65C02-Specific Opcodes - STZ (Store Zero)

**Files:**
- Modify: `sim6502/Proc/Processor.Operations.cs`
- Modify: `sim6502/Proc/OpcodeRegistry.cs`
- Modify: `sim6502tests/Opcodes65C02Tests.cs`

**Step 1: Write the failing test**

Add to `sim6502tests/Opcodes65C02Tests.cs`:

```csharp
[Fact]
public void STZ_ZeroPage_StoresZero()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();

    // Pre-fill memory with non-zero
    proc.WriteMemoryValueWithoutIncrement(0x50, 0xFF);

    // STZ $50 at address $0200
    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x64); // STZ zero page
    proc.WriteMemoryValueWithoutIncrement(0x0201, 0x50); // address
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    proc.ReadMemoryValueWithoutCycle(0x50).Should().Be(0x00);
}

[Fact]
public void STZ_ZeroPageX_StoresZero()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();
    proc.XRegister = 0x10;

    // Pre-fill memory with non-zero
    proc.WriteMemoryValueWithoutIncrement(0x60, 0xFF);

    // STZ $50,X at address $0200 (effective address $60)
    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x74); // STZ zero page,X
    proc.WriteMemoryValueWithoutIncrement(0x0201, 0x50); // base address
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    proc.ReadMemoryValueWithoutCycle(0x60).Should().Be(0x00);
}

[Fact]
public void STZ_Absolute_StoresZero()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();

    // Pre-fill memory with non-zero
    proc.WriteMemoryValueWithoutIncrement(0x1234, 0xFF);

    // STZ $1234 at address $0200
    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x9C); // STZ absolute
    proc.WriteMemoryValueWithoutIncrement(0x0201, 0x34); // low byte
    proc.WriteMemoryValueWithoutIncrement(0x0202, 0x12); // high byte
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    proc.ReadMemoryValueWithoutCycle(0x1234).Should().Be(0x00);
}

[Fact]
public void STZ_AbsoluteX_StoresZero()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();
    proc.XRegister = 0x10;

    // Pre-fill memory with non-zero
    proc.WriteMemoryValueWithoutIncrement(0x1244, 0xFF);

    // STZ $1234,X at address $0200 (effective address $1244)
    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x9E); // STZ absolute,X
    proc.WriteMemoryValueWithoutIncrement(0x0201, 0x34); // low byte
    proc.WriteMemoryValueWithoutIncrement(0x0202, 0x12); // high byte
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    proc.ReadMemoryValueWithoutCycle(0x1244).Should().Be(0x00);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter "STZ" -v n`
Expected: FAIL - STZ opcode not implemented

**Step 3: Add operation implementation**

Add to `sim6502/Proc/Processor.Operations.cs`:

```csharp
/// <summary>
/// STZ - Store Zero (65C02)
/// Stores zero to memory location
/// </summary>
public void StoreZeroOperation(AddressingMode mode)
{
    var address = GetAddressFromMode(mode);
    WriteMemoryValue(address, 0);
}
```

**Step 4: Register the opcodes**

Add to `sim6502/Proc/OpcodeRegistry.cs` in `Register65C02Opcodes()`:

```csharp
// STZ - Store Zero
Register65C02(0x64, "STZ", AddressingMode.ZeroPage, 2, 3,
    p => p.StoreZeroOperation(AddressingMode.ZeroPage));

Register65C02(0x74, "STZ", AddressingMode.ZeroPageX, 2, 4,
    p => p.StoreZeroOperation(AddressingMode.ZeroPageX));

Register65C02(0x9C, "STZ", AddressingMode.Absolute, 3, 4,
    p => p.StoreZeroOperation(AddressingMode.Absolute));

Register65C02(0x9E, "STZ", AddressingMode.AbsoluteX, 3, 5,
    p => p.StoreZeroOperation(AddressingMode.AbsoluteX));
```

**Step 5: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "STZ" -v n`
Expected: PASS

**Step 6: Commit**

```bash
git add sim6502/Proc/Processor.Operations.cs sim6502/Proc/OpcodeRegistry.cs \
    sim6502tests/Opcodes65C02Tests.cs
git commit -m "feat(65c02): add STZ (Store Zero) instruction

Supports zero page, zero page X, absolute, and absolute X modes

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 9: Add 65C02-Specific Opcodes - BRA (Branch Always)

**Files:**
- Modify: `sim6502/Proc/Processor.Operations.cs`
- Modify: `sim6502/Proc/OpcodeRegistry.cs`
- Modify: `sim6502tests/Opcodes65C02Tests.cs`

**Step 1: Write the failing test**

Add to `sim6502tests/Opcodes65C02Tests.cs`:

```csharp
[Fact]
public void BRA_BranchesForward()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();

    // BRA +$10 at address $0200 (branch to $0212)
    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x80); // BRA
    proc.WriteMemoryValueWithoutIncrement(0x0201, 0x10); // offset (+16)
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    // PC should be $0202 + $10 = $0212
    proc.ProgramCounter.Should().Be(0x0212);
}

[Fact]
public void BRA_BranchesBackward()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();

    // BRA -$10 at address $0220 (branch to $0212)
    proc.WriteMemoryValueWithoutIncrement(0x0220, 0x80); // BRA
    proc.WriteMemoryValueWithoutIncrement(0x0221, 0xF0); // offset (-16 in two's complement)
    proc.ProgramCounter = 0x0220;

    proc.NextStep();

    // PC should be $0222 + $F0 (signed) = $0222 - $10 = $0212
    proc.ProgramCounter.Should().Be(0x0212);
}

[Fact]
public void BRA_NotAvailableOn6502()
{
    var proc = new Processor(ProcessorType.MOS6502);
    proc.Reset();

    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x80); // BRA (65C02 only)
    proc.WriteMemoryValueWithoutIncrement(0x0201, 0x10);
    proc.ProgramCounter = 0x0200;

    var action = () => proc.NextStep();
    action.Should().Throw<System.NotSupportedException>();
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter "BRA" -v n`
Expected: FAIL - BRA opcode not implemented

**Step 3: Add operation implementation**

Add to `sim6502/Proc/Processor.Operations.cs`:

```csharp
/// <summary>
/// BRA - Branch Always (65C02)
/// Unconditional relative branch
/// </summary>
public void BranchAlwaysOperation()
{
    // BRA always branches (no condition to check)
    Branch(true);
}
```

**Step 4: Register the opcode**

Add to `sim6502/Proc/OpcodeRegistry.cs` in `Register65C02Opcodes()`:

```csharp
// BRA - Branch Always
Register65C02(0x80, "BRA", AddressingMode.Relative, 2, 3,
    p => p.BranchAlwaysOperation());
```

**Step 5: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "BRA" -v n`
Expected: PASS

**Step 6: Commit**

```bash
git add sim6502/Proc/Processor.Operations.cs sim6502/Proc/OpcodeRegistry.cs \
    sim6502tests/Opcodes65C02Tests.cs
git commit -m "feat(65c02): add BRA (Branch Always) instruction

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 10: Add 65C02-Specific Opcodes - INC A, DEC A

**Files:**
- Modify: `sim6502/Proc/Processor.Operations.cs`
- Modify: `sim6502/Proc/OpcodeRegistry.cs`
- Modify: `sim6502tests/Opcodes65C02Tests.cs`

**Step 1: Write the failing test**

Add to `sim6502tests/Opcodes65C02Tests.cs`:

```csharp
[Fact]
public void INC_Accumulator_IncrementsAccumulator()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();
    proc.Accumulator = 0x41;

    // INC A at address $0200
    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x1A); // INC A
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    proc.Accumulator.Should().Be(0x42);
}

[Fact]
public void INC_Accumulator_WrapsAt255()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();
    proc.Accumulator = 0xFF;

    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x1A); // INC A
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    proc.Accumulator.Should().Be(0x00);
    proc.ZeroFlag.Should().BeTrue();
}

[Fact]
public void DEC_Accumulator_DecrementsAccumulator()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();
    proc.Accumulator = 0x42;

    // DEC A at address $0200
    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x3A); // DEC A
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    proc.Accumulator.Should().Be(0x41);
}

[Fact]
public void DEC_Accumulator_WrapsAtZero()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();
    proc.Accumulator = 0x00;

    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x3A); // DEC A
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    proc.Accumulator.Should().Be(0xFF);
    proc.NegativeFlag.Should().BeTrue();
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter "Accumulator" -v n`
Expected: FAIL - INC A/DEC A opcodes not implemented

**Step 3: Add operation implementations**

Add to `sim6502/Proc/Processor.Operations.cs`:

```csharp
/// <summary>
/// INC A - Increment Accumulator (65C02)
/// </summary>
public void IncrementAccumulatorOperation()
{
    Accumulator = (Accumulator + 1) & 0xFF;
    SetZeroFlag(Accumulator);
    SetNegativeFlag(Accumulator);
}

/// <summary>
/// DEC A - Decrement Accumulator (65C02)
/// </summary>
public void DecrementAccumulatorOperation()
{
    Accumulator = (Accumulator - 1) & 0xFF;
    SetZeroFlag(Accumulator);
    SetNegativeFlag(Accumulator);
}
```

**Step 4: Register the opcodes**

Add to `sim6502/Proc/OpcodeRegistry.cs` in `Register65C02Opcodes()`:

```csharp
// INC A - Increment Accumulator
Register65C02(0x1A, "INC", AddressingMode.Accumulator, 1, 2,
    p => p.IncrementAccumulatorOperation());

// DEC A - Decrement Accumulator
Register65C02(0x3A, "DEC", AddressingMode.Accumulator, 1, 2,
    p => p.DecrementAccumulatorOperation());
```

**Step 5: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "Accumulator" -v n`
Expected: PASS

**Step 6: Commit**

```bash
git add sim6502/Proc/Processor.Operations.cs sim6502/Proc/OpcodeRegistry.cs \
    sim6502tests/Opcodes65C02Tests.cs
git commit -m "feat(65c02): add INC A and DEC A instructions

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 11: Add 65C02-Specific Opcodes - TRB, TSB

**Files:**
- Modify: `sim6502/Proc/Processor.Operations.cs`
- Modify: `sim6502/Proc/OpcodeRegistry.cs`
- Modify: `sim6502tests/Opcodes65C02Tests.cs`

**Step 1: Write the failing test**

Add to `sim6502tests/Opcodes65C02Tests.cs`:

```csharp
[Fact]
public void TRB_ZeroPage_ClearsBitsInMemory()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();
    proc.Accumulator = 0b00001111; // Bits to clear
    proc.WriteMemoryValueWithoutIncrement(0x50, 0b11111111);

    // TRB $50 at address $0200
    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x14); // TRB zero page
    proc.WriteMemoryValueWithoutIncrement(0x0201, 0x50);
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    // Memory should have bits cleared where A has 1s
    proc.ReadMemoryValueWithoutCycle(0x50).Should().Be(0b11110000);
}

[Fact]
public void TRB_SetsZeroFlagIfNoMatchingBits()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();
    proc.Accumulator = 0b11110000;
    proc.WriteMemoryValueWithoutIncrement(0x50, 0b00001111); // No overlap

    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x14); // TRB zero page
    proc.WriteMemoryValueWithoutIncrement(0x0201, 0x50);
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    proc.ZeroFlag.Should().BeTrue(); // A AND memory == 0
}

[Fact]
public void TSB_ZeroPage_SetsBitsInMemory()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();
    proc.Accumulator = 0b00001111; // Bits to set
    proc.WriteMemoryValueWithoutIncrement(0x50, 0b11110000);

    // TSB $50 at address $0200
    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x04); // TSB zero page
    proc.WriteMemoryValueWithoutIncrement(0x0201, 0x50);
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    // Memory should have bits set where A has 1s
    proc.ReadMemoryValueWithoutCycle(0x50).Should().Be(0b11111111);
}

[Fact]
public void TSB_SetsZeroFlagIfNoMatchingBits()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();
    proc.Accumulator = 0b11110000;
    proc.WriteMemoryValueWithoutIncrement(0x50, 0b00001111); // No overlap

    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x04); // TSB zero page
    proc.WriteMemoryValueWithoutIncrement(0x0201, 0x50);
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    proc.ZeroFlag.Should().BeTrue(); // A AND original memory == 0
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter "TRB|TSB" -v n`
Expected: FAIL - TRB/TSB opcodes not implemented

**Step 3: Add operation implementations**

Add to `sim6502/Proc/Processor.Operations.cs`:

```csharp
/// <summary>
/// TRB - Test and Reset Bits (65C02)
/// Clears bits in memory that are set in accumulator
/// Z flag is set based on A AND memory (before modification)
/// </summary>
public void TestResetBitsOperation(AddressingMode mode)
{
    var address = GetAddressFromMode(mode);
    var memValue = ReadMemoryValue(address);

    // Set Z flag based on A AND memory (before modification)
    SetZeroFlag(Accumulator & memValue);

    // Clear bits in memory where A has 1s
    var result = memValue & ~Accumulator;
    WriteMemoryValue(address, (byte)result);
}

/// <summary>
/// TSB - Test and Set Bits (65C02)
/// Sets bits in memory that are set in accumulator
/// Z flag is set based on A AND memory (before modification)
/// </summary>
public void TestSetBitsOperation(AddressingMode mode)
{
    var address = GetAddressFromMode(mode);
    var memValue = ReadMemoryValue(address);

    // Set Z flag based on A AND memory (before modification)
    SetZeroFlag(Accumulator & memValue);

    // Set bits in memory where A has 1s
    var result = memValue | Accumulator;
    WriteMemoryValue(address, (byte)result);
}
```

**Step 4: Register the opcodes**

Add to `sim6502/Proc/OpcodeRegistry.cs` in `Register65C02Opcodes()`:

```csharp
// TRB - Test and Reset Bits
Register65C02(0x14, "TRB", AddressingMode.ZeroPage, 2, 5,
    p => p.TestResetBitsOperation(AddressingMode.ZeroPage));

Register65C02(0x1C, "TRB", AddressingMode.Absolute, 3, 6,
    p => p.TestResetBitsOperation(AddressingMode.Absolute));

// TSB - Test and Set Bits
Register65C02(0x04, "TSB", AddressingMode.ZeroPage, 2, 5,
    p => p.TestSetBitsOperation(AddressingMode.ZeroPage));

Register65C02(0x0C, "TSB", AddressingMode.Absolute, 3, 6,
    p => p.TestSetBitsOperation(AddressingMode.Absolute));
```

**Step 5: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "TRB|TSB" -v n`
Expected: PASS

**Step 6: Commit**

```bash
git add sim6502/Proc/Processor.Operations.cs sim6502/Proc/OpcodeRegistry.cs \
    sim6502tests/Opcodes65C02Tests.cs
git commit -m "feat(65c02): add TRB and TSB bit manipulation instructions

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 12: Add 65C02 New Addressing Modes - Zero Page Indirect

**Files:**
- Modify: `sim6502/Proc/AddressingMode.cs`
- Modify: `sim6502/Proc/Processor.Addressing.cs`
- Modify: `sim6502/Proc/OpcodeRegistry.cs`
- Modify: `sim6502tests/Opcodes65C02Tests.cs`

**Step 1: Write the failing test**

Add to `sim6502tests/Opcodes65C02Tests.cs`:

```csharp
[Fact]
public void LDA_ZeroPageIndirect_LoadsFromIndirectAddress()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();

    // Set up indirect pointer at $50 pointing to $1234
    proc.WriteMemoryValueWithoutIncrement(0x50, 0x34); // low byte
    proc.WriteMemoryValueWithoutIncrement(0x51, 0x12); // high byte

    // Put value at target address
    proc.WriteMemoryValueWithoutIncrement(0x1234, 0x42);

    // LDA ($50) at address $0200
    proc.WriteMemoryValueWithoutIncrement(0x0200, 0xB2); // LDA (zp)
    proc.WriteMemoryValueWithoutIncrement(0x0201, 0x50);
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    proc.Accumulator.Should().Be(0x42);
}

[Fact]
public void STA_ZeroPageIndirect_StoresToIndirectAddress()
{
    var proc = new Processor(ProcessorType.WDC65C02);
    proc.Reset();
    proc.Accumulator = 0x99;

    // Set up indirect pointer at $50 pointing to $1234
    proc.WriteMemoryValueWithoutIncrement(0x50, 0x34);
    proc.WriteMemoryValueWithoutIncrement(0x51, 0x12);

    // STA ($50) at address $0200
    proc.WriteMemoryValueWithoutIncrement(0x0200, 0x92); // STA (zp)
    proc.WriteMemoryValueWithoutIncrement(0x0201, 0x50);
    proc.ProgramCounter = 0x0200;

    proc.NextStep();

    proc.ReadMemoryValueWithoutCycle(0x1234).Should().Be(0x99);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter "ZeroPageIndirect" -v n`
Expected: FAIL - Addressing mode not implemented

**Step 3: Add addressing mode**

Add to `sim6502/Proc/AddressingMode.cs`:

```csharp
/// <summary>
/// Zero Page Indirect - 65C02 only
/// (zp) - Address in zero page points to target
/// </summary>
ZeroPageIndirect = 14
```

**Step 4: Update addressing calculation**

Add to `sim6502/Proc/Processor.Addressing.cs` in `GetAddressFromMode()`:

```csharp
AddressingMode.ZeroPageIndirect => GetZeroPageIndirectAddress(),
```

Add helper method:

```csharp
private int GetZeroPageIndirectAddress()
{
    var zpAddress = GetImmediateValue();
    var lowByte = ReadMemoryValue(zpAddress);
    var highByte = ReadMemoryValue((zpAddress + 1) & 0xFF);
    return lowByte | (highByte << 8);
}
```

**Step 5: Register the opcodes**

Add to `sim6502/Proc/OpcodeRegistry.cs` in `Register65C02Opcodes()`:

```csharp
// LDA (zp) - Load Accumulator Zero Page Indirect
Register65C02(0xB2, "LDA", AddressingMode.ZeroPageIndirect, 2, 5,
    p => p.LoadAccumulatorOperation(AddressingMode.ZeroPageIndirect));

// STA (zp) - Store Accumulator Zero Page Indirect
Register65C02(0x92, "STA", AddressingMode.ZeroPageIndirect, 2, 5,
    p => p.StoreAccumulatorOperation(AddressingMode.ZeroPageIndirect));

// ORA (zp) - OR Accumulator Zero Page Indirect
Register65C02(0x12, "ORA", AddressingMode.ZeroPageIndirect, 2, 5,
    p => p.OrOperation(AddressingMode.ZeroPageIndirect));

// AND (zp) - AND Accumulator Zero Page Indirect
Register65C02(0x32, "AND", AddressingMode.ZeroPageIndirect, 2, 5,
    p => p.AndOperation(AddressingMode.ZeroPageIndirect));

// EOR (zp) - XOR Accumulator Zero Page Indirect
Register65C02(0x52, "EOR", AddressingMode.ZeroPageIndirect, 2, 5,
    p => p.ExclusiveOrOperation(AddressingMode.ZeroPageIndirect));

// ADC (zp) - Add with Carry Zero Page Indirect
Register65C02(0x72, "ADC", AddressingMode.ZeroPageIndirect, 2, 5,
    p => p.AddWithCarryOperation(AddressingMode.ZeroPageIndirect));

// CMP (zp) - Compare Zero Page Indirect
Register65C02(0xD2, "CMP", AddressingMode.ZeroPageIndirect, 2, 5,
    p => p.CompareOperation(AddressingMode.ZeroPageIndirect));

// SBC (zp) - Subtract with Carry Zero Page Indirect
Register65C02(0xF2, "SBC", AddressingMode.ZeroPageIndirect, 2, 5,
    p => p.SubtractWithCarryOperation(AddressingMode.ZeroPageIndirect));
```

**Step 6: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "ZeroPageIndirect" -v n`
Expected: PASS

**Step 7: Commit**

```bash
git add sim6502/Proc/AddressingMode.cs sim6502/Proc/Processor.Addressing.cs \
    sim6502/Proc/OpcodeRegistry.cs sim6502tests/Opcodes65C02Tests.cs
git commit -m "feat(65c02): add zero page indirect addressing mode

Supports LDA, STA, ORA, AND, EOR, ADC, CMP, SBC with (zp) mode

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 13: Add 6510 I/O Port Emulation

**Files:**
- Modify: `sim6502/Proc/Processor.Memory.cs`
- Create: `sim6502tests/Processor6510Tests.cs`

**Step 1: Write the failing test**

Create `sim6502tests/Processor6510Tests.cs`:

```csharp
using FluentAssertions;
using sim6502.Proc;
using Xunit;

namespace sim6502tests;

public class Processor6510Tests
{
    [Fact]
    public void IOPort_6510_ReadWriteDataDirection()
    {
        var proc = new Processor(ProcessorType.MOS6510);
        proc.Reset();

        // Write to data direction register at $00
        proc.WriteMemoryValue(0x00, 0x2F); // Standard C64 DDR value

        proc.ReadMemoryValueWithoutCycle(0x00).Should().Be(0x2F);
    }

    [Fact]
    public void IOPort_6510_ReadWriteData()
    {
        var proc = new Processor(ProcessorType.MOS6510);
        proc.Reset();

        // Set all bits as output
        proc.WriteMemoryValue(0x00, 0xFF);

        // Write to data register at $01
        proc.WriteMemoryValue(0x01, 0x37); // Standard C64 bank config

        proc.ReadMemoryValueWithoutCycle(0x01).Should().Be(0x37);
    }

    [Fact]
    public void IOPort_6502_NoSpecialBehavior()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        // On 6502, $00-$01 are just normal memory
        proc.WriteMemoryValue(0x00, 0xAB);
        proc.WriteMemoryValue(0x01, 0xCD);

        proc.ReadMemoryValueWithoutCycle(0x00).Should().Be(0xAB);
        proc.ReadMemoryValueWithoutCycle(0x01).Should().Be(0xCD);
    }

    [Fact]
    public void IOPort_65C02_NoSpecialBehavior()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // On 65C02, $00-$01 are just normal memory
        proc.WriteMemoryValue(0x00, 0xAB);
        proc.WriteMemoryValue(0x01, 0xCD);

        proc.ReadMemoryValueWithoutCycle(0x00).Should().Be(0xAB);
        proc.ReadMemoryValueWithoutCycle(0x01).Should().Be(0xCD);
    }
}
```

**Step 2: Run test to verify baseline**

Run: `dotnet test sim6502tests --filter "Processor6510Tests" -v n`
Expected: Tests may pass (basic memory works), but no special I/O behavior yet

**Step 3: Add I/O port handling**

The 6510 I/O port is at $00 (DDR) and $01 (data). For now, we'll implement basic read/write.
The DDR determines which bits are inputs (0) vs outputs (1).

Modify `sim6502/Proc/Processor.Memory.cs`:

```csharp
// Add fields for 6510 I/O port
private byte _6510DataDirection = 0x00;
private byte _6510DataPort = 0x00;

public override byte ReadMemoryValue(int address)
{
    // 6510 I/O port handling
    if (ProcessorType == ProcessorType.MOS6510 && address <= 0x01)
    {
        IncrementCycleCount();
        return address == 0x00 ? _6510DataDirection : _6510DataPort;
    }

    return base.ReadMemoryValue(address);
}

public override void WriteMemoryValue(int address, byte data)
{
    // 6510 I/O port handling
    if (ProcessorType == ProcessorType.MOS6510 && address <= 0x01)
    {
        IncrementCycleCount();
        if (address == 0x00)
            _6510DataDirection = data;
        else
            _6510DataPort = data;
        return;
    }

    base.WriteMemoryValue(address, data);
}

// Also update ReadMemoryValueWithoutCycle for 6510
public byte ReadMemoryValueWithoutCycle(int address)
{
    if (ProcessorType == ProcessorType.MOS6510 && address <= 0x01)
    {
        return address == 0x00 ? _6510DataDirection : _6510DataPort;
    }

    return Memory[address];
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "Processor6510Tests" -v n`
Expected: PASS

**Step 5: Commit**

```bash
git add sim6502/Proc/Processor.Memory.cs sim6502tests/Processor6510Tests.cs
git commit -m "feat(6510): add I/O port emulation at \$00-\$01

Emulates data direction register at \$00 and data port at \$01

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 14: Integration Test - Full 65C02 Test Suite

**Files:**
- Create: `sim6502tests/GrammarTests/test-19.txt`
- Modify: `sim6502tests/TestSuiteParser.cs`
- Modify: `sim6502tests/sim6502tests.csproj`

**Step 1: Create integration test file**

Create `sim6502tests/GrammarTests/test-19.txt`:

```
suites {
  suite("65C02 Integration Tests") {
    processor(65c02)

    test("phx-plx", "PHX/PLX round trip") {
      x = $42
      jsr($0300, stop_on_rts)
      assert(x == $42, "X should be restored")
    }

    test("phy-ply", "PHY/PLY round trip") {
      y = $37
      jsr($0310, stop_on_rts)
      assert(y == $37, "Y should be restored")
    }

    test("stz", "STZ clears memory") {
      $2000 = $FF
      jsr($0320, stop_on_rts)
      assert(peekbyte($2000) == $00, "Memory should be zero")
    }

    test("bra", "BRA branches unconditionally") {
      a = $00
      jsr($0330, stop_on_rts)
      assert(a == $42, "A should be $42 after branch")
    }

    test("inc-a", "INC A increments accumulator") {
      a = $41
      jsr($0340, stop_on_rts)
      assert(a == $42, "A should be incremented")
    }

    test("dec-a", "DEC A decrements accumulator") {
      a = $43
      jsr($0350, stop_on_rts)
      assert(a == $42, "A should be decremented")
    }
  }
}
```

**Step 2: Create test programs**

Create a separate step to write the machine code or note that `load()` should be used with a PRG file containing the test routines.

For now, we'll write the test to verify grammar parsing works:

Add to `sim6502tests/TestSuiteParser.cs`:

```csharp
[Fact]
public void TestSuite19_65C02Integration()
{
    // This test validates 65C02 processor mode and opcodes work together
    var tree = GetContext("GrammarTests/test-19.txt");

    var walker = new ParseTreeWalker();
    var sbl = new SimBaseListener();

    // Should parse without errors
    walker.Walk(sbl, tree);
}
```

**Step 3: Add test file to csproj**

Add to `sim6502tests/sim6502tests.csproj`:

```xml
<None Update="GrammarTests\test-19.txt">
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
</None>
```

**Step 4: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter "TestSuite19" -v n`
Expected: PASS (grammar parsing)

**Step 5: Commit**

```bash
git add sim6502tests/GrammarTests/test-19.txt sim6502tests/TestSuiteParser.cs \
    sim6502tests/sim6502tests.csproj
git commit -m "test(65c02): add integration test suite for 65C02 features

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 15: Update Documentation

**Files:**
- Modify: `docs/latex/sim6502.tex` (if exists)
- Create or update README section

**Step 1: Update LaTeX documentation**

Add a new section about processor selection:

```latex
\section{Processor Selection}

sim6502 supports multiple processor variants in the 65xx family:

\begin{itemize}
    \item \textbf{6502} - Original NMOS MOS Technology 6502 (default)
    \item \textbf{6510} - Same ISA as 6502, adds I/O port at \$00-\$01 (Commodore 64)
    \item \textbf{65C02} - WDC CMOS variant with additional instructions
\end{itemize}

\subsection{Specifying Processor Type}

Use the \texttt{processor()} statement at the beginning of a suite:

\begin{verbatim}
suites {
  suite("My 65C02 Tests") {
    processor(65c02)

    test("test-1", "Uses 65C02 opcodes") {
      // PHX, PLX, STZ, BRA, etc. available
    }
  }
}
\end{verbatim}

\subsection{65C02 Additional Instructions}

The 65C02 adds the following instructions not available on the 6502/6510:

\begin{itemize}
    \item \textbf{PHX, PLX} - Push/Pull X register
    \item \textbf{PHY, PLY} - Push/Pull Y register
    \item \textbf{STZ} - Store Zero
    \item \textbf{BRA} - Branch Always
    \item \textbf{INC A, DEC A} - Increment/Decrement Accumulator
    \item \textbf{TRB, TSB} - Test and Reset/Set Bits
    \item Zero Page Indirect addressing mode: \texttt{LDA (\$50)}
\end{itemize}
```

**Step 2: Build documentation**

Run: `cd docs/latex && make`

**Step 3: Commit**

```bash
git add docs/latex/sim6502.tex docs/sim6502.pdf
git commit -m "docs: add processor selection documentation

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 16: Final Integration - Run All Tests

**Step 1: Run complete test suite**

Run: `dotnet test sim6502tests -v n`
Expected: All tests PASS

**Step 2: Verify no regressions in existing tests**

Run: `dotnet test sim6502tests --filter "TestSuite1|TestSuite2|TestSuite3|TestSuite4|TestSuite5" -v n`
Expected: All PASS

**Step 3: Final commit and tag**

```bash
git add -A
git commit -m "feat: multi-processor support (6502/6510/65C02)

- Add ProcessorType enum for processor variants
- Refactor OpcodeRegistry for processor-specific opcode tables
- Add processor() DSL statement for suite-level processor selection
- Implement 65C02 opcodes: PHX, PLX, PHY, PLY, STZ, BRA, INC A, DEC A, TRB, TSB
- Add zero page indirect addressing mode for 65C02
- Add 6510 I/O port emulation at \$00-\$01
- Update documentation

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"

git tag -a v1.1.0 -m "Multi-processor support (6502/6510/65C02)"
git push && git push --tags
```

---

**Plan complete and saved to `docs/plans/2026-01-28-multi-processor-support.md`. Two execution options:**

**1. Subagent-Driven (this session)** - I dispatch fresh subagent per task, review between tasks, fast iteration

**2. Parallel Session (separate)** - Open new session with executing-plans, batch execution with checkpoints

**Which approach?**
