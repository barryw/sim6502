# sim6502 - 6502 Assembly Testing Framework
        __ _____  ___ ___    _    _       _ _     _______        _      _____ _      _____
       / /| ____|/ _ \__ \  | |  | |     (_) |   |__   __|      | |    / ____| |    |_   _|
      / /_| |__ | | | | ) | | |  | |_ __  _| |_     | | ___  ___| |_  | |    | |      | |
     | '_ \___ \| | | |/ /  | |  | | '_ \| | __|    | |/ _ \/ __| __| | |    | |      | |
     | (_) |__) | |_| / /_  | |__| | | | | | |_     | |  __/\__ \ |_  | |____| |____ _| |_
      \___/____/ \___/____|  \____/|_| |_|_|\__|    |_|\___||___/\__|  \_____|______|_____|


![.NET Core](https://github.com/barryw/sim6502/workflows/.NET%20Core/badge.svg)

#### Introduction

This is a tool to help you unit test your 6502 assembly language programs. There's no valid reason why your 6502 programs shouldn't receive the same DevOps treatment as the rest of your modern applications.

It works by running your assembled programs with a 6502 simulator and then allowing you to make assertions on memory and CPU state. It's very similar to other unit test tools.

A minimal test suite looks like this:

```
suites {
  suite("Tests against hardware register library") {
    ; Load the program under test
    symbols("/code/include_me_full_r.sym")
    load("/code/include_me_full_r.prg")
    load("/code/kernal.rom", address = $e000)

    test("sprites-positions-correctly-without-msb","Sprite X pos < 256 sets MSB to 0") {
      x = $00
      a = $ff
      y = $00
      $02 = $40
      [vic.MSIGX] = $00

      jsr([PositionSprite], stop_on_rts = true, fail_on_brk = true)

      assert([vic.SP0X]   == $ff, "Sprite 0's X pos is at ff")
      assert([vic.SP0Y]   == $40, "Sprite 0's Y pos is at $40")
      assert([vic.MSIGX]  == $00, "And sprite 0's MSB is set to 0")
    }
  }

  suite("Tests against pseudo register library") {
    ; Load the program under test
    symbols("/code/include_me_full.sym")
    load("/code/include_me_full.prg", strip_header = true)
    load("/code/kernal.rom", address = $e000)

    test("memory-fill-1", "Fill an uneven block of memory") {
      [r0L] = $bd   ; Stuff $bd into our memory locations. Odd number, right?
      [r1] = $1234  ; Start at $1234
      [r2] = $12c   ; and do 300 bytes

      jsr([FillMemory], stop_on_rts = true, fail_on_brk = true)

      assert(cycles < 11541, "We can fill this block in fewer than 11541 cycles")
      assert(memchk($1234, $12c, $bd), "Memory was filled properly")
    }
  }
}

```

Each file can contain one or more `suite`s and each suite can contain one more more `test`s. Each suite is tied to a set of binary object files that are the subjects to be tested.

Start your suite by giving it a name. Call it whatever you'd like since the name isn't significant. It's used to identify the suite in output.

```
suites {
  suite("My awesome new test suite! Sweet!") {
  }
}
```

Inside the suite you'll first need to define the programs that you'd like to test. You can also load other things that your test code may need. You can include things like the C64 KERNAL, BASIC, etc.

```
suites {
  suite("My awesome new test suite! Sweet!") {
    load("/code/include_me_full.prg", strip_header = true)
    load("/code/kernal.rom", address = $e000)
  }
}
```

If you don't specify the binary file's `address`, it will be inferred by looking at the first 2 bytes of the file. These will be the 16-bit load address. If you don't specify `address`, then you'll need to specify `strip_header = true` so that those 2 bytes are removed before loading to memory.

You can also include a kickassembler symbol file so that you can use symbol references instead of hardcoded values and addresses.

```
suites {
  suite("My awesome new test suite! Sweet!") {
    symbols("/code/include_me_full.sym")
    load("/code/include_me_full.prg", strip_header = true)
    load("/code/kernal.rom", address = $e000)
  }
}
```

Next, start writing your tests. Tests have 3 main blocks: memory assignment, calling subroutines, assertions. You use the memory assignment area to set things up to test your code. Once memory is set up, you can then call subroutines in the code you want to test. When the subroutine's exit condition is reached, assertions will be performed to make sure your code did what it was supposed to. Here's an example:

```
suites {
  suite("My awesome new test suite! Sweet!") {
    ; Any line that starts with a semi-colon is treated as a comment
    symbols("/code/include_me_full.sym")
    load("/code/include_me_full.prg", strip_header = true)
    load("/code/kernal.rom", address = $e000)

    test("memory-fill-1", "Fill an uneven block of memory") {
      [r0L] = $bd   ; Stuff $bd into our memory locations. Odd number, right?
      [r1] = $1234  ; Start at $1234
      [r2] = $12c   ; and do 300 bytes

      jsr([FillMemory], stop_on_rts = true, fail_on_brk = true)

      assert(cycles < 11541, "We can fill this block in fewer than 11541 cycles")
      assert(memchk($1234, $12c, $bd), "Memory was filled properly")
    }
  }
}
```

This code contains a single test in a single suite. The test is named `memory-fill-1` and has a description of `Fill an uneven block of memory`. These are not significant and are only used to identify the test in output.

The first 3 lines of the test are memory assignment lines. They refer to symbols contained in the symbol file `include_me_full.sym`, which must exist or the test fails.

Once these symbols are resolved to a location, the value to the right of the equals sign is placed in that memory location. If the value is > 255, then a 16-bit word is placed at the symbol location and symbol location +1 ([r1] & [r1] + 1).

The `jsr` line will start executing code starting at the address given, which in this case is a symbol called `FillMemory`. The `jsr` function can take 3 parameters:

- stop_on_rts = true|false : Whether to stop executing when a `rts` instruction is encountered. The code will only return if the `rts` instruction exists at the same level as the subroutine. For example, if your subroutine calls other routines, those `rts` calls won't trigger the jsr to exit.
- stop_on_address = address : If specified, the jsr call will return when the program counter reaches this address.
- fail_on_brk = true|false : Whether to fail the test if a `brk` instruction is encountered.


Once your subroutine exits, the assertions will be run in order. You can assert any of these things:

- memory location values using either the address or a symbol reference (eg. `[vic.SP0X] == $01` or `$3000 == $80`)
- processor cycle counts to ensure that code performs as expected (eg. `cycles < 80`)
- memory compares to verify that copy operations work correctly (eg. `assert(memcmp($e000, $4000, $2000), "Ensure that KERNAL was copied correctly")`)
- memory check to verify that fill operations work correctly (eg. `assert(memchk($1234, $12c, $bd), "Memory was filled properly")`)

The expression syntax is very flexible so that you can do things like this:

```
assert([c64lib_timers] + $00 + peekbyte([r2H]) * 8 == [ENABLE], "Timer enabled")
assert([c64lib_timers] + $01 + peekbyte([r2H]) * 8 == [TIMER_SINGLE], "Timer type is single")
assert(([c64lib_timers] + $02 + peekbyte([r2H]) * 8).w == $1000, "Timer's current value")
assert(([c64lib_timers] + $04 + peekbyte([r2H]) * 8).w == $1000, "Timer's frequency")
assert(([c64lib_timers] + $06 + peekbyte([r2H]) * 8).w == [ReadJoysticks], "Timer's callback address")
```

You can tell the CLI whether to return a byte or a word from a memory location by using the `.b` and `.w` suffix on the address expression. By default, a byte will be returned. (eg. `[MyVector].w`)

You can also return the hi and lo byte for 16-bit number by using the `.l` and `.h` suffix (eg. `[MyVector].h`)

#### Running

You'll need to have the following things available to the test CLI:

- Your assembled 6502 program in .prg format. If the first 2 bytes don't contain the load address, then you'll need to specify the address with the `address` parameter.
- Your Kickassembler symbol file. While not required, makes testing a LOT easier!
- Your test file

Run the CLI with:

```bash
dotnet Sim6502TestRunner.dll -s {path to your test script}
```

If all of your tests pass, the CLI will exit with a return code of 0. If any tests fail, it will return with a 1.

If you'd like to see the assembly language instructions that it executes while running your tests, add the `-t` flag:

```bash
dotnet Sim6502TestRunner.dll -t -s {path to your test script}
```

There is also a Docker image if you'd like to not have to mess with installing the .NET Core framework. You can run it like this:

```bash
docker run -v ${PWD}:/code -it ghcr.io/barryw/sim6502:latest -s /code/{your test script} -t
```

That would mount the current directory to a directory in the container called `/code` and would expect to see all of your artifacts there, unless you've given them absolute paths. Just make sure you update your test script to point to the correct location of any roms and programs.

If you'd like to see a larger example of this tool in action, run `make` from the `example` folder. It's the test suite from my c64lib project.


---

## Complete DSL Reference

### File Structure

Test files use a hierarchical structure:

```
suites {
  suite("Suite Name") {
    ; Suite-level configuration
    symbols("path/to/symbols.sym")
    load("path/to/program.prg", strip_header = true)

    test("test-id", "Test Description") {
      ; Test contents: assignments, jsr calls, assertions
    }

    test("another-test", "Another Test") {
      ; ...
    }
  }

  suite("Another Suite") {
    ; ...
  }
}
```

### Comments

Lines starting with `;` are comments and are ignored:

```
; This is a comment
[r0L] = $ff  ; This is an inline comment
```

### Suite-Level Functions

#### symbols(filename)
Loads a KickAssembler symbol file. Must be called before symbols can be referenced.

```
symbols("path/to/program.sym")
```

#### load(filename, [address = addr], [strip_header = true|false])
Loads a binary program into memory.

| Parameter | Description |
|-----------|-------------|
| `filename` | Path to the .prg file |
| `address` | Optional. Load address (overrides file header) |
| `strip_header` | Optional. If `true`, skips the first 2 bytes (load address header) |

```
; Load with embedded address header (first 2 bytes)
load("program.prg", strip_header = true)

; Load at specific address
load("kernal.rom", address = $e000)

; Load with header intact (uses embedded load address)
load("program.prg")
```

### Number Formats

| Format | Syntax | Example |
|--------|--------|---------|
| Hexadecimal | `$xxxx` or `0xXXXX` | `$d020`, `0xFF` |
| Decimal | Plain digits | `1234`, `255` |
| Binary | `%xxxxxxxx` | `%10101010`, `%11110000` |

### Boolean Values

```
true
false
```

### Symbol References

Symbols from loaded symbol files are referenced with square brackets:

```
[SymbolName]           ; Simple symbol
[namespace.symbol]     ; Namespaced symbol (e.g., [vic.SP0X])
```

### Memory Addresses

Addresses can be specified as numbers or symbol references:

```
$d020                  ; Hexadecimal address
8192                   ; Decimal address
[vic.SP0X]             ; Symbol reference
```

### Registers

The 6502 registers can be read and written:

| Register | Syntax |
|----------|--------|
| Accumulator | `a` or `A` |
| X Index | `x` or `X` |
| Y Index | `y` or `Y` |

```
a = $ff     ; Set accumulator to 255
x = 0       ; Set X register to 0
y = [r0L]   ; Set Y register from symbol value
```

### Processor Flags

The processor status flags can be read and written:

| Flag | Syntax | Description |
|------|--------|-------------|
| Carry | `c` or `C` | Carry flag |
| Negative | `n` or `N` | Negative flag |
| Zero | `z` or `Z` | Zero flag |
| Decimal | `d` or `D` | Decimal mode flag |
| Overflow | `v` or `V` | Overflow flag |

```
c = true    ; Set carry flag
n = false   ; Clear negative flag
z = 1       ; Set zero flag (1 = true)
d = 0       ; Clear decimal flag (0 = false)
v = [FALSE] ; Set from symbol
```

### Assignments

#### Memory Assignment

Write values to memory addresses:

```
$d020 = $0e              ; Write byte to address
$c000 = $1234            ; Write word (2 bytes, little-endian)
[vic.EXTCOL] = $00       ; Write to symbol address
[r1] = $4000             ; Write word to symbol address
```

#### Expression Assignment

Write to computed addresses:

```
[Loc1] + $02 = $d0       ; Write to symbol + offset
$4000 + 8 = $ff          ; Write to address + offset
```

#### Register Assignment

```
a = $ff
x = peekbyte($c000)
y = [r0L] + 1
```

#### Flag Assignment

```
c = true
n = false
z = 1
```

### Expressions

Expressions can combine values with operators:

#### Arithmetic Operators

| Operator | Description |
|----------|-------------|
| `+` | Addition |
| `-` | Subtraction |
| `*` | Multiplication |
| `/` | Division |

#### Bitwise Operators

| Operator | Description |
|----------|-------------|
| `&` | Bitwise AND |
| `\|` | Bitwise OR |
| `^` | Bitwise XOR |

#### Byte/Word Modifiers

| Modifier | Description |
|----------|-------------|
| `.b` | Read/compare as byte (8-bit) - default |
| `.w` | Read/compare as word (16-bit) |
| `.l` | Low byte of 16-bit value |
| `.h` | High byte of 16-bit value |

```
[MyVector].w             ; Read as 16-bit word
[MyValue].b              ; Read as 8-bit byte (default)
$1234.l                  ; Low byte = $34
$1234.h                  ; High byte = $12
```

#### Expression Examples

```
[r0L] + 1
$4000 + peekbyte([offset])
[base] + peekbyte([index]) * 8
([c64lib_timers] + $02).w
peekword([r1]) & $ff00
```

### Built-in Functions

#### peekbyte(address)
Returns the 8-bit value at the specified address.

```
peekbyte($c000)
peekbyte([r0L])
```

#### peekword(address)
Returns the 16-bit value at address and address+1 (little-endian).

```
peekword($c000)          ; Returns value at $c000 (low) and $c001 (high)
peekword([vector])
```

#### memchk(address, size, value)
Returns `true` if all bytes in the memory range equal the specified value.

```
memchk($1234, $100, $00)           ; Check if 256 bytes are zero
memchk([buffer], $40, $ff)         ; Check if 64 bytes are $ff
```

#### memcmp(source, target, size)
Returns `true` if the two memory regions are identical.

```
memcmp($e000, $4000, $2000)        ; Compare 8KB regions
memcmp([src], [dst], peekword([size]))
```

### JSR Function (Subroutine Execution)

The `jsr` function executes code starting at the specified address:

```
jsr(address, stop_condition, fail_on_brk = true|false)
```

#### Stop Conditions

You must specify ONE of these stop conditions:

| Option | Description |
|--------|-------------|
| `stop_on_rts = true\|false` | Stop when RTS is executed at the original call level |
| `stop_on_address = address` | Stop when program counter reaches this address |

**Note:** When using `stop_on_address`, RTS instructions do NOT stop execution. The code continues until the specified address is reached.

#### fail_on_brk Option

| Value | Behavior |
|-------|----------|
| `true` | Test fails if BRK instruction is executed |
| `false` | BRK stops execution but test continues |

#### JSR Examples

```
; Stop when the subroutine returns
jsr([FillMemory], stop_on_rts = true, fail_on_brk = true)

; Stop at a specific address (numeric)
jsr($2000, stop_on_address = $2100, fail_on_brk = true)

; Stop at a specific address (symbol)
jsr([FillMemory], stop_on_address = [CopyMemory], fail_on_brk = true)

; Using namespaced symbol for stop address
jsr([FillMemory], stop_on_address = [Keyboard.Return], fail_on_brk = false)
```

### Assertions

Assertions verify that your code behaved correctly:

```
assert(comparison, "Description of what's being tested")
```

#### Comparison Operators

| Operator | Description |
|----------|-------------|
| `==` | Equal to |
| `!=` or `<>` | Not equal to |
| `>` | Greater than |
| `<` | Less than |
| `>=` | Greater than or equal |
| `<=` | Less than or equal |

#### Assertion Types

**Memory Value Assertions:**
```
assert($d020 == $0e, "Border color is light blue")
assert([vic.SP0X] == $ff, "Sprite 0 X position is 255")
assert(([MyVector].w) == $1000, "Vector points to $1000")
```

**Register Assertions:**
```
assert(a == $00, "Accumulator is zero")
assert(x >= 8, "X register is at least 8")
assert(y != $ff, "Y register is not $ff")
```

**Flag Assertions:**
```
assert(c == true, "Carry flag is set")
assert(z == false, "Zero flag is clear")
assert(n == true, "Negative flag is set")
```

**Cycle Count Assertions:**
```
assert(cycles < 1000, "Routine completed in under 1000 cycles")
assert(cycles <= 500, "Routine is fast enough")
```

**Memory Check Assertions:**
```
assert(memchk($1234, $100, $bd), "Memory filled with $bd")
assert(memcmp($e000, $4000, $2000), "Memory regions match")
```

**Complex Expression Assertions:**
```
assert([c64lib_timers] + $00 + peekbyte([r2H]) * 8 == [ENABLE], "Timer enabled")
assert(([base] + $02).w == $1000, "Word value matches")
```

### Complete Test Example

```
suites {
  suite("Memory Operations Test Suite") {
    ; Load symbol file for symbolic references
    symbols("c64lib.sym")

    ; Load the program under test
    load("c64lib.prg", strip_header = true)

    ; Load C64 KERNAL ROM for any KERNAL calls
    load("kernal.rom", address = $e000)

    test("fill-memory-basic", "Fill 256 bytes with a pattern") {
      ; Setup: Configure parameters for FillMemory routine
      [r0L] = $aa          ; Fill value
      [r1] = $4000         ; Start address
      [r2] = $100          ; Size (256 bytes)

      ; Clear destination first
      a = $00
      x = $00

      ; Execute the routine
      jsr([FillMemory], stop_on_rts = true, fail_on_brk = true)

      ; Verify results
      assert(memchk($4000, $100, $aa), "Memory filled correctly")
      assert(cycles < 5000, "Completed efficiently")
    }

    test("copy-memory-kernal", "Copy KERNAL ROM to RAM") {
      ; Setup copy parameters
      [r0] = $e000         ; Source: KERNAL ROM
      [r1] = $4000         ; Destination
      [r2] = $2000         ; Size: 8KB

      ; Execute
      jsr([CopyMemory], stop_on_rts = true, fail_on_brk = true)

      ; Verify
      assert(memcmp($e000, $4000, $2000), "KERNAL copied correctly")
    }

    test("sprite-position", "Position sprite without MSB") {
      ; Setup sprite 0 position
      x = $00              ; Sprite number
      a = $ff              ; X position (< 256)
      y = $40              ; Y position
      $02 = $40            ; Y position in ZP
      [vic.MSIGX] = $00    ; Clear MSB register

      ; Execute sprite positioning
      jsr([PositionSprite], stop_on_rts = true, fail_on_brk = true)

      ; Verify sprite registers
      assert([vic.SP0X] == $ff, "X position set correctly")
      assert([vic.SP0Y] == $40, "Y position set correctly")
      assert([vic.MSIGX] == $00, "MSB not set for X < 256")
    }
  }
}
```

---

##### Function Reference (Quick Reference)

- `memcmp(source, target, size)`: Compare 2 blocks of memory
- `memchk(source, size, value)`: Ensure that a block of memory contains `value`
- `peekbyte(address)`: Return the 8-bit value at `address`
- `peekword(address)`: Return the 16-bit value at `address` and `address + 1`

---

##### Symbol File Reference

The only supported symbol file right now is Kickassembler's. To load symbols from a symbol file, load it from the top of your suite with the `symbols` function:

```
symbols("/code/include_me_full.sym")
```

You can reference symbols within your tests by wrapping the symbol name in brackets:

```
[MySymbol]
```

The symbol must exist, or the test will fail.

The format of the symbol file looks like this:

```
.label ENABLE=$80
.label DISABLE=$00
.label TIMER_ONE_SECOND=$3c
.label TIMER_SINGLE=$0
.label TIMER_STRUCT_BYTES=$40

.label UpdateTimers=$209d
.label UpdateScreen=$21cc

.label c64lib_timers=$33c
```

The CLI also supports symbol files containing namespaces:

```
.namespace vic {
  .label SP0X   = $d000
  .label SP0Y   = $d001
  .label SP1X   = $d002
  .label SP1Y   = $d003
  .label SP2X   = $d004
  .label SP2Y   = $d005
  .label SP3X   = $d006
  .label SP3Y   = $d007
  .label SP4X   = $d008
  .label SP4Y   = $d009
  .label SP5X   = $d00a
  .label SP5Y   = $d00b
  .label SP6X   = $d00c
  .label SP6Y   = $d00d
  .label SP7X   = $d00e
  .label SP7Y   = $d00f
}
```

If you wanted to reference the symbol `SP0X` inside of the `vic` namespace, you'd reference it as `[vic.SP0X]`.

You can also perform simple expressions like `[UpdateTimers] + 1` or `peekword([r0L])`.


#### What's missing?

There is absolutely no concept of hardware other than the 6502. There's no VIC, SID, CIA, etc, so testing against programs that use these hardware devices is pretty limited. You CAN, and the `example` test suite shows this, test to make sure sprite registers are set properly since it's just memory.

This is a vanilla 6502 with no concept of any C64 specific hardware. I would LOVE to have a full c64 simulator, but that's not where we are right now.

#### Thanks

Thanks to Aaron Mell for building the 6502 simulator (https://github.com/aaronmell/6502Net). It was a tremendous help in building this tool.

Thanks to Terence Parr and Sam Harwell for ANTLR. (https://www.antlr.org/)

#### License

ANTLR 4.8 is Copyright (C) 2012 Terence Parr and Sam Harwell. All Rights Reserved.

The 6502 Simulator and associated test suite are Copyright (C) 2013 by Aaron Mell. All Rights Reserved.

The 6502 Unit Test CLI and associated test suite are Copyright (C) 2020 by Barry Walker. All Rights Reserved.


Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


