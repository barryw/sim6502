# 6502 Processor Refactoring Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor the 3,033-line monolithic `Processor.cs` into well-organized partial classes, then modernize the opcode dispatch from a 460-case switch to a clean handler pattern.

**Architecture:** Split into partial classes by concern (Core, Memory, Addressing, Opcodes, Operations, Disassembly), then replace the giant switch statement with a dictionary-based opcode handler system using delegates.

**Tech Stack:** C# 10+, .NET 10.0, xUnit for testing

---

## Phase 1: Split Processor.cs into Partial Classes

Split the monolith while preserving exact behavior. Run tests after each file to ensure nothing breaks.

### Task 1: Create Processor.Core.cs (State & Properties)

**Files:**
- Create: `sim6502/Proc/Processor.Core.cs`
- Modify: `sim6502/Proc/Processor.cs`

**Step 1: Create Processor.Core.cs with properties and state**

```csharp
/*
Copyright (c) 2013, Aaron Mell
All rights reserved.
... [keep original copyright header]
*/

using NLog;

namespace sim6502.Proc;

/// <summary>
/// An Implementation of a 6502 Processor - Core state and properties
/// </summary>
public partial class Processor
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
    private int _programCounter;
    private int _stackPointer;
    private bool _previousInterrupt;
    private bool _interrupt;

    #region Properties

    /// <summary>
    /// Our 64k address space
    /// </summary>
    protected byte[] Memory { get; private set; }

    /// <summary>
    /// The Accumulator. This value is implemented as an integer instead of a byte.
    /// This is done so we can detect wrapping of the value and set the correct number of cycles.
    /// </summary>
    public int Accumulator { get; set; }

    /// <summary>
    /// The X Index Register
    /// </summary>
    public int XRegister { get; set; }

    /// <summary>
    /// The Y Index Register
    /// </summary>
    public int YRegister { get; set; }

    /// <summary>
    /// The Current Op Code being executed by the system
    /// </summary>
    public int CurrentOpCode { get; private set; }

    /// <summary>
    /// The disassembly of the current operation. This value is only set when the CPU is built in debug mode.
    /// </summary>
    public Disassembly CurrentDisassembly { get; private set; }

    /// <summary>
    /// Points to the Current Address of the instruction being executed by the system.
    /// The PC wraps when the value is greater than 65535, or less than 0.
    /// </summary>
    public int ProgramCounter
    {
        get => _programCounter;
        set => _programCounter = WrapProgramCounter(value);
    }

    /// <summary>
    /// Points to the Current Position of the Stack.
    /// This value is a 00-FF value but is offset to point to the location in memory where the stack resides.
    /// </summary>
    public int StackPointer
    {
        get => _stackPointer;
        set
        {
            if (value > 0xFF)
                _stackPointer = value - 0x100;
            else if (value < 0x00)
                _stackPointer = value + 0x100;
            else
                _stackPointer = value;
        }
    }

    /// <summary>
    /// An external action that occurs when the cycle count is incremented
    /// </summary>
    public Action CycleCountIncrementedAction { get; set; }

    // Status Registers
    /// <summary>
    /// This is the carry flag. when adding, if the result is greater than 255 or 99 in BCD Mode, then this bit is enabled.
    /// </summary>
    public bool CarryFlag { get; set; }

    /// <summary>
    /// Is true if one of the registers is set to zero.
    /// </summary>
    public bool ZeroFlag { get; set; }

    /// <summary>
    /// This determines if Interrupts are currently disabled.
    /// </summary>
    public bool DisableInterruptFlag { get; set; }

    /// <summary>
    /// Binary Coded Decimal Mode is set/cleared via this flag.
    /// </summary>
    public bool DecimalFlag { get; set; }

    /// <summary>
    /// This property is set when an overflow occurs.
    /// </summary>
    public bool OverflowFlag { get; set; }

    /// <summary>
    /// Set to true if the result of an operation is negative.
    /// </summary>
    public bool NegativeFlag { get; set; }

    /// <summary>
    /// The number of cycles executed by the processor.
    /// </summary>
    public long CycleCount { get; private set; }

    /// <summary>
    /// Used to indicate that a Non-Maskable Interrupt is triggered.
    /// </summary>
    public bool TriggerNmi { get; set; }

    /// <summary>
    /// Used to indicate that an IRQ has been triggered.
    /// </summary>
    public bool TriggerIrq { get; set; }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Wraps the program counter if needed
    /// </summary>
    private static int WrapProgramCounter(int value)
    {
        return value switch
        {
            > 0xFFFF => value - 0x10000,
            < 0 => value + 0x10000,
            _ => value
        };
    }

    /// <summary>
    /// Sets the IsSignNegative register
    /// </summary>
    protected void SetNegativeFlag(int value)
    {
        NegativeFlag = value > 127;
    }

    /// <summary>
    /// Sets the IsResultZero register
    /// </summary>
    protected void SetZeroFlag(int value)
    {
        ZeroFlag = value == 0;
    }

    /// <summary>
    /// Resets the Cycle Count back to 0
    /// </summary>
    public void ResetCycleCount()
    {
        CycleCount = 0;
    }

    /// <summary>
    /// Increments the cycle count
    /// </summary>
    protected void IncrementCycleCount()
    {
        CycleCount++;
        CycleCountIncrementedAction?.Invoke();
    }

    /// <summary>
    /// Converts the processor flags to a single byte
    /// </summary>
    private byte ConvertFlagsToByte(bool setBreak)
    {
        return (byte)((CarryFlag ? 0x01 : 0) + (ZeroFlag ? 0x02 : 0) + (DisableInterruptFlag ? 0x04 : 0) +
                       (DecimalFlag ? 8 : 0) + (setBreak ? 0x10 : 0) + 0x20 + (OverflowFlag ? 0x40 : 0) +
                       (NegativeFlag ? 0x80 : 0));
    }

    /// <summary>
    /// Sets the processor flags from a byte value
    /// </summary>
    private void SetFlagsFromByte(byte flags)
    {
        CarryFlag = (flags & 0x01) != 0;
        ZeroFlag = (flags & 0x02) != 0;
        DisableInterruptFlag = (flags & 0x04) != 0;
        DecimalFlag = (flags & 0x08) != 0;
        OverflowFlag = (flags & 0x40) != 0;
        NegativeFlag = (flags & 0x80) != 0;
    }

    #endregion
}
```

**Step 2: Run tests to verify nothing broke**

Run: `dotnet test --filter "KlausDormannFunctionalTests"`
Expected: PASS (this validates the emulator still works)

**Step 3: Commit**

```bash
git add sim6502/Proc/Processor.Core.cs sim6502/Proc/Processor.cs
git commit -m "refactor(processor): extract core state to Processor.Core.cs"
```

---

### Task 2: Create Processor.Memory.cs

**Files:**
- Create: `sim6502/Proc/Processor.Memory.cs`
- Modify: `sim6502/Proc/Processor.cs` (remove memory methods)

**Step 1: Create Processor.Memory.cs**

```csharp
namespace sim6502.Proc;

/// <summary>
/// Memory operations for the 6502 Processor
/// </summary>
public partial class Processor
{
    #region Memory Operations

    /// <summary>
    /// Reads a value from memory and increments the cycle count
    /// </summary>
    public virtual byte ReadMemoryValue(int address)
    {
        IncrementCycleCount();
        return Memory[address];
    }

    /// <summary>
    /// Reads a value from memory without incrementing cycle count (for testing/debug)
    /// </summary>
    public virtual byte ReadMemoryValueWithoutCycle(int address)
    {
        return Memory[address];
    }

    /// <summary>
    /// Reads a 16-bit word from memory (low byte first)
    /// </summary>
    public int ReadMemoryWord(int address)
    {
        var lobyte = ReadMemoryValueWithoutCycle(address);
        var hibyte = ReadMemoryValueWithoutCycle(address + 1);
        return lobyte | (hibyte << 8);
    }

    /// <summary>
    /// Writes a value to memory and increments the cycle count
    /// </summary>
    public virtual void WriteMemoryValue(int address, byte value)
    {
        IncrementCycleCount();
        Memory[address] = value;
    }

    /// <summary>
    /// Writes a value to memory without incrementing cycle count
    /// </summary>
    public virtual void WriteMemoryValueWithoutIncrement(int address, byte value)
    {
        Memory[address] = value;
    }

    /// <summary>
    /// Writes a 16-bit word to memory (low byte first)
    /// </summary>
    public void WriteMemoryWord(int address, int value)
    {
        WriteMemoryValueWithoutIncrement(address, (byte)(value & 0xFF));
        WriteMemoryValueWithoutIncrement(address + 1, (byte)((value >> 8) & 0xFF));
    }

    /// <summary>
    /// Resets all memory to zero
    /// </summary>
    public void ResetMemory()
    {
        Memory = new byte[65536];
    }

    /// <summary>
    /// Dumps the entire memory object
    /// </summary>
    public byte[] DumpMemory()
    {
        return Memory;
    }

    /// <summary>
    /// Loads a program into processor memory
    /// </summary>
    public void LoadProgram(int offset, byte[] program, int initialProgramCounter, bool reset = true)
    {
        LoadProgram(offset, program);
        var bytes = BitConverter.GetBytes(initialProgramCounter);
        if (!reset) return;

        WriteMemoryValue(0xFFFC, bytes[0]);
        WriteMemoryValue(0xFFFD, bytes[1]);
        Reset();
    }

    /// <summary>
    /// Loads a program into processor memory at the specified offset
    /// </summary>
    public void LoadProgram(int offset, byte[] program)
    {
        if (offset > Memory.Length)
            throw new InvalidOperationException("Offset '{0}' is larger than memory size '{1}'");

        if (program.Length > Memory.Length + offset)
            throw new InvalidOperationException(
                $"Program Size '{program.Length}' Cannot be Larger than Memory Size '{Memory.Length}' plus offset '{offset}'");

        for (var i = 0; i < program.Length; i++)
            Memory[i + offset] = program[i];
    }

    #endregion
}
```

**Step 2: Run tests**

Run: `dotnet test --filter "KlausDormannFunctionalTests"`
Expected: PASS

**Step 3: Commit**

```bash
git add sim6502/Proc/Processor.Memory.cs sim6502/Proc/Processor.cs
git commit -m "refactor(processor): extract memory operations to Processor.Memory.cs"
```

---

### Task 3: Create Processor.Addressing.cs

**Files:**
- Create: `sim6502/Proc/Processor.Addressing.cs`
- Modify: `sim6502/Proc/Processor.cs` (remove addressing methods)

**Step 1: Create Processor.Addressing.cs with GetAddressByAddressingMode and GetAddressingMode**

Extract the `GetAddressByAddressingMode` method (~lines 1630-1790) and `GetAddressingMode` method (~lines 2100-2160) into this file.

**Step 2: Run tests**

Run: `dotnet test --filter "KlausDormannFunctionalTests"`
Expected: PASS

**Step 3: Commit**

```bash
git add sim6502/Proc/Processor.Addressing.cs sim6502/Proc/Processor.cs
git commit -m "refactor(processor): extract addressing modes to Processor.Addressing.cs"
```

---

### Task 4: Create Processor.Operations.cs

**Files:**
- Create: `sim6502/Proc/Processor.Operations.cs`
- Modify: `sim6502/Proc/Processor.cs` (remove operation methods)

**Step 1: Create Processor.Operations.cs**

Extract all operation methods (~lines 2200-2700):
- `AddWithCarryOperation`
- `SubtractWithBorrowOperation`
- `AndOperation`
- `OrOperation`
- `ExclusiveOrOperation`
- `AslOperation`
- `LsrOperation`
- `RolOperation`
- `RorOperation`
- `BitOperation`
- `CompareOperation`
- `BranchOperation`

**Step 2: Run tests**

Run: `dotnet test --filter "KlausDormannFunctionalTests"`
Expected: PASS

**Step 3: Commit**

```bash
git add sim6502/Proc/Processor.Operations.cs sim6502/Proc/Processor.cs
git commit -m "refactor(processor): extract CPU operations to Processor.Operations.cs"
```

---

### Task 5: Create Processor.Disassembly.cs

**Files:**
- Create: `sim6502/Proc/Processor.Disassembly.cs`
- Modify: `sim6502/Proc/Processor.cs` (remove disassembly methods)

**Step 1: Create Processor.Disassembly.cs**

Extract:
- `SetDisassembly` method (~lines 1809-1960)
- `ConvertOpCodeIntoString` method (~lines 2700-3033)

**Step 2: Run tests**

Run: `dotnet test --filter "KlausDormannFunctionalTests"`
Expected: PASS

**Step 3: Commit**

```bash
git add sim6502/Proc/Processor.Disassembly.cs sim6502/Proc/Processor.cs
git commit -m "refactor(processor): extract disassembly to Processor.Disassembly.cs"
```

---

### Task 6: Rename Processor.cs to Processor.Execution.cs

**Files:**
- Rename: `sim6502/Proc/Processor.cs` → `sim6502/Proc/Processor.Execution.cs`

After all extractions, the remaining file should contain:
- Constructor
- `Reset()` method
- `NextStep()` method
- `RunRoutine()` method
- `ExecuteOpCode()` method (the giant switch)
- Interrupt handling methods

**Step 1: Rename and update namespace**

**Step 2: Run all tests**

Run: `dotnet test`
Expected: All 977 tests PASS

**Step 3: Commit**

```bash
git add sim6502/Proc/
git commit -m "refactor(processor): rename main file to Processor.Execution.cs"
```

---

## Phase 2: Modernize Opcode Dispatch

Replace the 460-case switch statement with a cleaner handler pattern.

### Task 7: Create OpcodeHandler Infrastructure

**Files:**
- Create: `sim6502/Proc/OpcodeHandler.cs`
- Create: `sim6502/Proc/OpcodeHandlers/ArithmeticHandlers.cs`

**Step 1: Create the handler delegate and registry**

```csharp
// sim6502/Proc/OpcodeHandler.cs
namespace sim6502.Proc;

/// <summary>
/// Delegate for opcode handler functions
/// </summary>
public delegate void OpcodeHandler(Processor processor);

/// <summary>
/// Metadata about an opcode
/// </summary>
public record OpcodeInfo(
    byte Opcode,
    string Mnemonic,
    AddressingMode AddressingMode,
    int Bytes,
    int BaseCycles,
    OpcodeHandler Handler
);
```

**Step 2: Run tests**

Run: `dotnet test`
Expected: PASS (no behavior change yet)

**Step 3: Commit**

```bash
git add sim6502/Proc/OpcodeHandler.cs
git commit -m "refactor(processor): add opcode handler infrastructure"
```

---

### Task 8: Create Opcode Registry

**Files:**
- Create: `sim6502/Proc/OpcodeRegistry.cs`

**Step 1: Create registry that maps opcodes to handlers**

```csharp
namespace sim6502.Proc;

/// <summary>
/// Registry of all 6502 opcodes and their handlers
/// </summary>
public static class OpcodeRegistry
{
    private static readonly Dictionary<byte, OpcodeInfo> _opcodes = new();

    static OpcodeRegistry()
    {
        RegisterAllOpcodes();
    }

    public static OpcodeInfo? GetOpcode(byte opcode)
    {
        return _opcodes.TryGetValue(opcode, out var info) ? info : null;
    }

    private static void Register(OpcodeInfo info)
    {
        _opcodes[info.Opcode] = info;
    }

    private static void RegisterAllOpcodes()
    {
        // ADC - Add with Carry
        Register(new OpcodeInfo(0x69, "ADC", AddressingMode.Immediate, 2, 2,
            p => p.AddWithCarryOperation(AddressingMode.Immediate)));
        Register(new OpcodeInfo(0x65, "ADC", AddressingMode.ZeroPage, 2, 3,
            p => p.AddWithCarryOperation(AddressingMode.ZeroPage)));
        // ... continue for all opcodes
    }
}
```

**Step 2: Run tests**

Run: `dotnet test`
Expected: PASS

**Step 3: Commit**

```bash
git add sim6502/Proc/OpcodeRegistry.cs
git commit -m "refactor(processor): add opcode registry"
```

---

### Task 9: Replace Switch with Registry Lookup

**Files:**
- Modify: `sim6502/Proc/Processor.Execution.cs`

**Step 1: Replace ExecuteOpCode switch with registry lookup**

```csharp
private void ExecuteOpCode()
{
    var opcodeInfo = OpcodeRegistry.GetOpcode((byte)CurrentOpCode);

    if (opcodeInfo == null)
    {
        throw new NotSupportedException(
            $"The OpCode {CurrentOpCode} @ address {ProgramCounter} is not supported.");
    }

    opcodeInfo.Handler(this);
}
```

**Step 2: Run ALL tests including functional test**

Run: `dotnet test`
Expected: All 977 tests PASS

**Step 3: Commit**

```bash
git add sim6502/Proc/Processor.Execution.cs
git commit -m "refactor(processor): replace switch with registry lookup"
```

---

### Task 10: Clean Up and Final Validation

**Files:**
- All processor files

**Step 1: Remove dead code (old switch statement backup if any)**

**Step 2: Add XML documentation to new classes**

**Step 3: Run full test suite**

Run: `dotnet test`
Expected: All tests PASS

**Step 4: Run functional test with verbose output**

Run: `dotnet test --filter "AllOpcodes_ShouldPassFunctionalTest" -v n`
Expected: PASS in ~76 seconds

**Step 5: Final commit**

```bash
git add .
git commit -m "refactor(processor): complete processor refactoring

- Split 3033-line monolith into 6 focused partial classes
- Replaced 460-case switch with dictionary-based opcode registry
- All 977 tests pass including Klaus Dormann functional test
- No behavioral changes, pure refactoring"
```

---

## File Structure After Refactoring

```
sim6502/Proc/
├── AddressingMode.cs          (unchanged - enum)
├── Disassembly.cs             (unchanged - data class)
├── LoadableResource.cs        (unchanged)
├── OpcodeHandler.cs           (new - delegate and OpcodeInfo record)
├── OpcodeRegistry.cs          (new - opcode lookup table)
├── Processor.Addressing.cs    (new - addressing mode calculations)
├── Processor.Core.cs          (new - state, properties, flags)
├── Processor.Disassembly.cs   (new - disassembly/debug output)
├── Processor.Execution.cs     (new - main loop, formerly Processor.cs)
├── Processor.Memory.cs        (new - memory read/write)
└── Processor.Operations.cs    (new - ALU operations)
```

---

## Verification Checklist

After each task:
- [ ] `dotnet build` succeeds with no errors
- [ ] `dotnet test --filter "KlausDormannFunctionalTests"` passes
- [ ] Git commit made

After Phase 1 complete:
- [ ] All 977 tests pass
- [ ] No functionality changes (pure refactoring)

After Phase 2 complete:
- [ ] All 977 tests pass
- [ ] Functional test completes in similar time (~76s)
- [ ] Code is more maintainable and readable
