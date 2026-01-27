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

There is also a docker image if you'd like to not have to mess with installing the .NET Core framework. You can run it like this:

```bash
docker run -v ${PWD}:/code -it barrywalker71/sim6502cli:latest -s /code/{your test script} -t
```

That would mount the current directory to a directory in the container called `/code` and would expect to see all of your artifacts there, unless you've given them absolute paths. Just make sure you update your test script to point to the correct location of any roms and programs.

If you'd like to see a larger example of this tool in action, run `make` from the `example` folder. It's the test suite from my c64lib project.


##### Function Reference

- memcmp(source, target, size): Compare 2 blocks of memory
- memchk(source, size, value): Ensure that a block of memory contains `value`
- peekbyte(address): Return the 8-bit value at `address`
- peekword(address): Return the 16-bit value at `address` and `address + 1`


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


