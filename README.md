        __ _____  ___ ___    _    _       _ _     _______        _      _____ _      _____
       / /| ____|/ _ \__ \  | |  | |     (_) |   |__   __|      | |    / ____| |    |_   _|
      / /_| |__ | | | | ) | | |  | |_ __  _| |_     | | ___  ___| |_  | |    | |      | |
     | '_ \___ \| | | |/ /  | |  | | '_ \| | __|    | |/ _ \/ __| __| | |    | |      | |
     | (_) |__) | |_| / /_  | |__| | | | | | |_     | |  __/\__ \ |_  | |____| |____ _| |_
      \___/____/ \___/____|  \____/|_| |_|_|\__|    |_|\___||___/\__|  \_____|______|_____|


#### Introduction

This is a tool to help you unit test your 6502 assembly language programs. There's no valid reason why your 6502 programs shouldn't receive the same DevOps treatment as the rest of your modern applications.

It works by running your assembled programs with a 6502 simulator and then allowing you to make assertions on memory and CPU state. It's very similar to other unit test tools.

A minimal test suite looks like this:

```yaml
init:
  load:
  - filename: kernal.rom
    address: $e000
  - filename: basic.rom
    address: $a000
  - filename: character.rom
    address: $d000
unit_tests:
  program: my_awesome_program.prg
  tests:
  - name: timer-single-disables-on-update
    description: Single shot timer disables when it hits 0
    set_memory:
    - description: Enable timer 0
      address: "{c64lib_timers}"
      byte_value: "{ENABLE}"
    - description: This is a single-shot timer
      address: "{c64lib_timers} + 1"
      byte_value: "{TIMER_SINGLE}"
    - description: Current value
      address: "{c64lib_timers} + 2"
      word_value: "$01"
    - description: Frequency
      address: "{c64lib_timers} + 4"
      word_value: "{TIMER_ONE_SECOND}"
    - description: Timer call address
      address: "{c64lib_timers} + 6"
      word_value: "{UpdateScreen}"
    jump_address: "{UpdateTimers}"
    stop_on: rts
    fail_on_brk: true
    assert:
    - description: Timer is disabled
      address: "{c64lib_timers}"
      op: eq
      byte_value: "{DISABLE}"
```

If your code requires kernal, basic or character roms, you will need to provide them and place them in the same directory as the rest of your files.

You'll also notice things like this: `{UpdateTimers}`. These are symbols referenced from a generated Kickassembler symbol file. These are very handy and allow you to reference symbols from your source in your test suite so that you don't have to hard-code values. If you're creating constants in your source, use the Kickassembler directive `.label` to define them, and they will show up in your symbol file.

The symbol file for the above snippet might look like this:

```
.label ENABLE=$80
.label DISABLE=$00
.label TIMER_ONE_SECOND=$3c
.label TIMER_SINGLE=$0

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

If you wanted to reference the symbol `SP0X` inside of the `vic` namespace, you'd reference it as `{vic.SP0X}`.

You can also perform simple expressions like `{UpdateTimers} + 1` or `peekword({r0L})` in both `set_memory` and in your `assert` expressions.

The CLI's expression parser understands these functions:

- `peekword(address)`: returns the 16-bit word starting at location `address`
- `peekbyte(address)`: returns the byte at location `address`

The `6502tests` project is a good place to look to see what the expression parser supports.

#### Running

You'll need to have the following things available to the test CLI:

- Your assembled 6502 program in .prg format. If the first 2 bytes don't contain the load address, then you'll need to specify the address as `unit_tests.address`
- Your Kickassembler symbol file. While not required, makes testing a LOT easier!
- Your test YAML

Run the CLI with:

```bash
dotnet Sim6502TestRunner.dll -s {path to your symbolfile} -y {path to your test yaml}
```

If all of your tests pass, the CLI will exit with a return code of 0. If any tests fail, it will return with a 1.

If you'd like to see the assembly language instructions that it executes while running your tests, add the `-d` flag:

```bash
dotnet Sim6502TestRunner.dll -d -s {path to your symbolfile} -y {path to your test yaml}
```

There is also a docker image if you'd like to not have to mess with installing the .NET Core framework. You can run it like this:

```bash
docker run -v ${PWD}:/code -it barrywalker71/sim6502cli:latest -y /code/{your test yaml} -s /code/{your symbol file} -d
```

That would mount the current directory to a directory in the container called `/code` and would expect to see all of your artifacts there, unless you've given them absolute paths. Just make sure you update your tests yaml to point to the correct location of any roms and programs.

#### Thanks

Thanks to Aaron Mell for building the 6502 simulator (https://github.com/aaronmell/6502Net). It was a tremendous help in building this tool.


#### License

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
