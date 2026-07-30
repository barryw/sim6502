# u64sim Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `u64sim` execution backend that runs 6502 code against a simulated Ultimate 64 — the Ultimate Command Interface register block and the Ultimate DOS targets — so code that talks to the Ultimate can be unit tested with no hardware.

**Architecture:** sim6502's existing 6510 core and `C64MemoryMap` are reused unchanged except for adding I/O handler dispatch. A new `UciRegisters` class implements the C64-side protocol at `$DF1B-$DF1F` (ported from Gideon Zweijtzer's `command_protocol.vhd`) and dispatches parsed commands to `ICommandTarget` implementations. `UltimateDosTarget` (ported from `dos.cc`) serves targets `$01`/`$02` against a host directory exposed as `/Usb0`.

**Tech Stack:** C# / .NET 10, xunit + FluentAssertions, ANTLR 4.13.1 (regenerate with `make grammar`), NLog.

## Global Constraints

- **Licence: GPL-3.0.** sim6502 relicenses from BSD-2-Clause. Every file ported from `1541ultimate` carries an origin header naming the upstream file. Aaron Mell's BSD-2 notice for the simulator core is retained verbatim.
- **Origin header form.** For a file derived from a single upstream file, the header is the two-line template in Task 1. For a file derived from several — `UciConstants.cs` draws on three — the first line ends at the colon and the paths follow on indented continuation lines, then the author line. Both forms are correct; the requirement is that every upstream file is named, not that the block is a fixed number of lines.
- **Reference clone** for all ported sources: `/private/tmp/claude-501/-Users-barry-Git-sim6502/7884b568-e680-4b66-a6d6-ab5808997a30/scratchpad/u64`. If absent, re-clone: `git clone --depth 1 https://github.com/GideonZ/1541ultimate.git`.
- **Status strings are byte-exact.** Every DOS/UCI status string must match upstream character for character. A test that asserts `"00,OK"` must fail if the implementation emits `"00, OK"`.
- **Buffer geometry is fixed:** command `0..895` (896 bytes), response `896..1791` (896 bytes), status `1792..2047` (256 bytes). Total backing store 2048 bytes.
- **No assembler in CI.** 6502 test programs are checked in as `.prg` or built as documented byte arrays in C#. Do not add an assembler dependency.
- **`make grammar` must be run** after any `sim6502.g4` edit, and the regenerated files under `sim6502/Grammar/Generated/` committed — they are tracked in git.
- **Target framework** `net10.0`, `Nullable` enabled, `ImplicitUsings` enabled.
- All new production code lives under `sim6502/Systems/Ultimate/` or `sim6502/Backend/`; all new tests under `sim6502tests/Systems/Ultimate/` or `sim6502tests/Backend/`.

---

## Register and protocol reference

Every task below depends on these facts. They are transcribed from
`fpga/io/command_interface/vhdl_source/command_protocol.vhd` and
`command_if_pkg.vhd`.

**C64-visible registers** (base `$DF18`, so offset 3 = `$DF1B`):

| Address | Offset | Read | Write |
|---|---|---|---|
| `$DF1B` | 3 | bus ID | — |
| `$DF1C` | 4 | status byte | control byte |
| `$DF1D` | 5 | `$C9` (`UCI_IDENTIFIER`) | command data |
| `$DF1E` | 6 | response data | — |
| `$DF1F` | 7 | status data | — |

**Status byte** (read `$DF1C`):

| Bit | Meaning |
|---|---|
| 7 | `responseValid` — response data available |
| 6 | `statusValid` — status data available |
| 5-4 | state: `00` idle, `01` processing, `10` data last, `11` data more |
| 3 | `errorBusy` |
| 2 | `handshakeIn[2]` — abort set |
| 1 | `handshakeIn[1]` — data accepted set |
| 0 | `handshakeIn[0]` — new command |

`responseValid = (responsePtr - 896) < responseLength && (state & 2) != 0 && !abort`
`statusValid   = (statusPtr - 1792) < statusLength  && (state & 2) != 0 && !abort`

**Control byte** (write `$DF1C`), applied in this order:

1. bit 3 set → `errorBusy = false`
2. bit 0 set (`PUSH_CMD`) → `freeze = bit7`; `trigger = bit6`; if `state == 00` then `state = 01` and `handshakeIn[0] = true`, else `errorBusy = true`; `cmdIrqEn = bit5`
3. bit 1 set **and** `(state & 2) != 0` (`DATA_ACC`) → `handshakeIn[1] = (state & 1) != 0`; clear bit 1 of `state`; `cmdIrqEn = false`
4. bit 2 set → `handshakeIn[2] = true`

**Command write** (`$DF1D`): `ram[commandPtr] = value`, then `if (commandPtr != 895) commandPtr++`.

**Response read** (`$DF1E`): value = `responseValid ? ram[responsePtr] : 0`; `cmdIrqEn = false`; `if (responsePtr != 1791) responsePtr++`.

**Status read** (`$DF1F`): value = `statusValid ? ram[statusPtr] : 0`; `cmdIrqEn = false`; `if (statusPtr != 2047) statusPtr++`.

**Ultimate-side handshake out** — `HandshakeOut(value)`, applied in this order:

1. bit 0 → `handshakeIn[0] = false`; `commandPtr = 0`
2. bit 1 → `handshakeIn[1] = false`
3. bit 2 → `handshakeIn[2] = false`
4. bit 4 → `trigger = false`; `freeze = false`; set bit 1 of `state`; bit 0 of `state` = bit 5 of value; `ResetResponse()`
5. bit 7 → `freeze = false`; `trigger = false`; `ResetResponse()`; `state = 0`

`ResetResponse()`: `responsePtr = 896; statusPtr = 1792`.

Constants: `HandshakeReset = 0x87`, `AcceptCommand = 0x01`, `AcceptNextData = 0x02`, `ValidateLast = 0x10`, `ValidateMore = 0x30`.

**Ultimate-side service loop** — ported from `command_intf.cc` `run_task()`, lines 116-171:

```
if (handshakeIn[2]) {                       // abort
    target?.Abort(responsePtr - 896);
    HandshakeOut(HandshakeReset);
}
if (handshakeIn[1]) {                       // more data requested
    reply = target.GetMoreData();
    CopyResult(reply);
    HandshakeOut(AcceptNextData);
}
if (handshakeIn[0]) {                       // new command
    length = commandPtr;
    if (length > 0) {
        targetId = ram[0] & 0x0F;
        noReply  = (ram[0] & 0x80) != 0;
        target   = targets[targetId] ?? emptyTarget;
        reply    = target.ParseCommand(ram[0..length]);
        HandshakeOut(AcceptCommand);
        if (noReply) HandshakeOut(HandshakeReset);
        else CopyResult(reply);
    } else {
        responseLength = 0; statusLength = 0;
        HandshakeOut(AcceptCommand);
        HandshakeOut(ValidateLast);
    }
}
```

`CopyResult(reply)`: copy `reply.Data` to `ram[896..]`, `reply.Status` (ASCII) to
`ram[1792..]`, set `responseLength`/`statusLength`, then
`HandshakeOut(reply.LastPart ? ValidateLast : ValidateMore)`.

---

## File structure

| File | Responsibility |
|---|---|
| `sim6502/Systems/IMemoryMap.cs` | add `RegisterIoHandler` with a throwing default implementation |
| `sim6502/Systems/C64MemoryMap.cs` | dispatch `$D000-$DFFF` reads/writes to registered handlers |
| `sim6502/Systems/Ultimate/UciConstants.cs` | addresses, bit masks, buffer geometry, handshake values |
| `sim6502/Systems/Ultimate/UciReply.cs` | `ICommandTarget` return value |
| `sim6502/Systems/Ultimate/ICommandTarget.cs` | the target seam |
| `sim6502/Systems/Ultimate/UciRegisters.cs` | register protocol + service loop; knows nothing about command meanings |
| `sim6502/Systems/Ultimate/UltimateFileSystem.cs` | host directory as `/Usb0`; only component touching real disk |
| `sim6502/Systems/Ultimate/UltimateDosTarget.cs` | targets `$01`/`$02` |
| `sim6502/Systems/Ultimate/ControlTarget.cs` | target `$04` |
| `sim6502/Backend/U64SimBackendConfig.cs` | `FsRoot`, `UciLatencyCycles`, `DosVersion` |
| `sim6502/Backend/U64SimBackend.cs` | composes `SimulatorBackend` + peripherals; host-side command issuing |

---

## Task 1: Relicense to GPL-3.0

Done first so every ported file gets its origin header from the start.

**Files:**
- Create: `LICENSE`
- Create: `NOTICE`
- Modify: `README.md` (licence section, currently around line 1702)

**Interfaces:**
- Consumes: nothing
- Produces: the origin-header convention every later task follows:
  ```csharp
  // Ported from GideonZ/1541ultimate (GPL-3.0): <upstream/path/file.ext>
  // Original author: Gideon Zweijtzer. See NOTICE.
  ```

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/LicenseTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace sim6502tests;

public class LicenseTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "sim6502.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must be able to locate the repository root");
        return dir!.FullName;
    }

    [Fact]
    public void Repository_HasGpl3LicenseFile()
    {
        var path = Path.Combine(RepoRoot(), "LICENSE");
        File.Exists(path).Should().BeTrue("LICENSE must exist at the repository root");
        var text = File.ReadAllText(path);
        text.Should().Contain("GNU GENERAL PUBLIC LICENSE");
        text.Should().Contain("Version 3, 29 June 2007");
    }

    [Fact]
    public void Notice_CreditsUpstreamAuthors()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "NOTICE"));
        text.Should().Contain("Gideon Zweijtzer");
        text.Should().Contain("Aaron Mell");
        text.Should().Contain("1541ultimate");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~LicenseTests"`
Expected: FAIL — both tests fail, `LICENSE` and `NOTICE` do not exist.

- [ ] **Step 3: Create the licence files**

Fetch the canonical GPL-3.0 text and write it to `LICENSE`:

```bash
curl -sSL https://www.gnu.org/licenses/gpl-3.0.txt -o LICENSE
head -3 LICENSE   # must show: GNU GENERAL PUBLIC LICENSE / Version 3, 29 June 2007
```

Create `NOTICE`:

```
sim6502 — 6502 Assembly Testing Framework
Copyright (C) 2020-2026 Barry Walker

This program is free software: you can redistribute it and/or modify it under
the terms of the GNU General Public License as published by the Free Software
Foundation, either version 3 of the License, or (at your option) any later
version. See LICENSE for the full text.

Prior to version 4.0.0, sim6502 was distributed under the BSD 2-Clause licence.
Releases made under that licence remain available under it. Version 4.0.0 and
later are GPL-3.0.

--------------------------------------------------------------------------------
Third-party components
--------------------------------------------------------------------------------

6502 simulator core
  Copyright (C) 2013 Aaron Mell. All Rights Reserved.
  Originally licensed under the BSD 2-Clause licence, reproduced below. BSD
  2-Clause is compatible with the GPL; this notice is retained as that licence
  requires.

  Redistribution and use in source and binary forms, with or without
  modification, are permitted provided that the following conditions are met:

  1. Redistributions of source code must retain the above copyright notice,
     this list of conditions and the following disclaimer.
  2. Redistributions in binary form must reproduce the above copyright notice,
     this list of conditions and the following disclaimer in the documentation
     and/or other materials provided with the distribution.

  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
  AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
  IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
  ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
  LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
  CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
  SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
  INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
  CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
  ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
  POSSIBILITY OF SUCH DAMAGE.

Ultimate Command Interface and Ultimate DOS behaviour
  Copyright (C) Gideon Zweijtzer.
  Ported from https://github.com/GideonZ/1541ultimate (GPL-3.0). The port is a
  reimplementation in C# of the behaviour of these upstream files:
    fpga/io/command_interface/vhdl_source/command_protocol.vhd
    fpga/io/command_interface/vhdl_source/command_if_pkg.vhd
    software/io/command_interface/command_intf.cc
    software/io/command_interface/command_intf.h
    software/filemanager/dos.cc
    software/filemanager/dos.h
  Individual ported files carry a header naming their upstream origin. The
  presence of this GPL-3.0 code is the reason sim6502 is GPL-3.0.

ANTLR 4.13.1
  Copyright (C) 2012 Terence Parr and Sam Harwell. All Rights Reserved.
  Used under the BSD 3-Clause licence.
```

- [ ] **Step 4: Rewrite the README licence section**

Read `README.md` around line 1700 to find the exact current text, then replace the
whole `#### License` section body with:

```markdown
#### License

sim6502 is licensed under the **GNU General Public License v3.0**. See `LICENSE`
for the full text and `NOTICE` for third-party attributions.

Versions prior to 4.0.0 were BSD 2-Clause. Those releases remain available under
that licence; 4.0.0 and later are GPL-3.0. The change was made to allow the
Ultimate 64 backends to port protocol and DOS behaviour from
[GideonZ/1541ultimate](https://github.com/GideonZ/1541ultimate), which is GPL-3.0.

If you embed sim6502 in a distributed product, GPL-3.0 requires you to make the
corresponding source available. Pin to a 3.x release if that does not suit you.

Thanks to Gideon Zweijtzer for the Ultimate hardware, its firmware, and the
documentation that made these backends possible.

ANTLR 4.13.1 is Copyright (C) 2012 Terence Parr and Sam Harwell.
The 6502 simulator core is Copyright (C) 2013 Aaron Mell, BSD 2-Clause (retained
in `NOTICE`).
sim6502 is Copyright (C) 2020-2026 Barry Walker.
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~LicenseTests"`
Expected: PASS — 2 passed.

- [ ] **Step 6: Commit**

```bash
git add LICENSE NOTICE README.md sim6502tests/LicenseTests.cs
git commit -m "chore(license)!: relicense to GPL-3.0 for Ultimate 64 support

Porting UCI protocol and Ultimate DOS behaviour from GideonZ/1541ultimate
(GPL-3.0) requires sim6502 to be GPL-3.0. Aaron Mell's BSD-2 notice for the
simulator core is retained in NOTICE as that licence requires.

BREAKING CHANGE: sim6502 is GPL-3.0 from this commit. Releases before 4.0.0
remain BSD-2-Clause."
```

---

## Task 2: I/O handler dispatch in the C64 memory map

**Files:**
- Modify: `sim6502/Systems/IMemoryMap.cs`
- Modify: `sim6502/Systems/C64MemoryMap.cs:84-101` (read) and `:122-135` (write)
- Test: `sim6502tests/Systems/C64MemoryMapIoHandlerTests.cs`

**Interfaces:**
- Consumes: `IIOHandler` from `sim6502/Systems/IIOHandler.cs` — `byte Read(int address)`, `void Write(int address, byte value)`. Already declared, currently unused.
- Produces: `void IMemoryMap.RegisterIoHandler(int startAddress, int endAddress, IIOHandler handler)` — inclusive range. Default interface implementation throws `NotSupportedException`. `C64MemoryMap` overrides it.

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/Systems/C64MemoryMapIoHandlerTests.cs`:

```csharp
using FluentAssertions;
using sim6502.Systems;
using Xunit;

namespace sim6502tests.Systems;

public class C64MemoryMapIoHandlerTests
{
    private sealed class RecordingHandler : IIOHandler
    {
        public List<(int Address, byte Value)> Writes { get; } = new();
        public byte ReadValue { get; set; } = 0xC9;
        public List<int> Reads { get; } = new();

        public byte Read(int address)
        {
            Reads.Add(address);
            return ReadValue;
        }

        public void Write(int address, byte value) => Writes.Add((address, value));
    }

    [Fact]
    public void RegisterIoHandler_ReadInRange_GoesToHandler()
    {
        var map = new C64MemoryMap();
        var handler = new RecordingHandler { ReadValue = 0xC9 };
        map.RegisterIoHandler(0xDF1B, 0xDF1F, handler);

        map.ReadWithoutCycle(0xDF1D).Should().Be(0xC9);
        handler.Reads.Should().ContainSingle().Which.Should().Be(0xDF1D);
    }

    [Fact]
    public void RegisterIoHandler_WriteInRange_GoesToHandlerAndAlsoToRam()
    {
        var map = new C64MemoryMap();
        var handler = new RecordingHandler();
        map.RegisterIoHandler(0xDF1B, 0xDF1F, handler);

        map.WriteWithoutCycle(0xDF1C, 0x01);

        handler.Writes.Should().ContainSingle().Which.Should().Be((0xDF1C, (byte)0x01));
        // Writes under I/O always reach RAM on a C64. Bank RAM in to observe it.
        map.WriteWithoutCycle(0x01, 0x30);   // LORAM=0, HIRAM=0 -> $D000-$DFFF is RAM
        map.ReadWithoutCycle(0xDF1C).Should().Be(0x01);
    }

    [Fact]
    public void ReadOutsideHandlerRange_StillUsesFlatIoRegisters()
    {
        var map = new C64MemoryMap();
        map.RegisterIoHandler(0xDF1B, 0xDF1F, new RecordingHandler());

        map.WriteWithoutCycle(0xD020, 0x0E);
        map.ReadWithoutCycle(0xD020).Should().Be(0x0E);
    }

    [Fact]
    public void HandlerNotConsultedWhenIoIsBankedOut()
    {
        var map = new C64MemoryMap();
        var handler = new RecordingHandler { ReadValue = 0xC9 };
        map.RegisterIoHandler(0xDF1B, 0xDF1F, handler);

        map.WriteWithoutCycle(0x01, 0x30);          // all RAM
        map.ReadWithoutCycle(0xDF1D).Should().Be(0x00);
        handler.Reads.Should().BeEmpty();
    }

    [Fact]
    public void GenericMemoryMap_RegisterIoHandler_Throws()
    {
        // Must be an interface-typed reference: C# reaches a default interface
        // implementation only through the interface, never through the concrete
        // class, even when the class does not override it.
        IMemoryMap map = new GenericMemoryMap();
        var act = () => map.RegisterIoHandler(0xDF1B, 0xDF1F, new RecordingHandler());
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Reset_ClearsRegisteredHandlers()
    {
        var map = new C64MemoryMap();
        var handler = new RecordingHandler { ReadValue = 0xC9 };
        map.RegisterIoHandler(0xDF1B, 0xDF1F, handler);
        map.Reset();

        map.ReadWithoutCycle(0xDF1D).Should().Be(0x00);
        handler.Reads.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~C64MemoryMapIoHandlerTests"`
Expected: FAIL — compile error, `RegisterIoHandler` is not a member of `IMemoryMap`.

- [ ] **Step 3: Add `RegisterIoHandler` to `IMemoryMap`**

Append to the interface in `sim6502/Systems/IMemoryMap.cs`, before the closing brace:

```csharp
    /// <summary>
    /// Register a handler for an inclusive I/O address range. Systems that model
    /// I/O as a flat byte array do not support this.
    /// </summary>
    void RegisterIoHandler(int startAddress, int endAddress, IIOHandler handler)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support I/O handlers. " +
            "Backends that need them require system(c64).");
```

- [ ] **Step 4: Implement dispatch in `C64MemoryMap`**

Add the handler table next to `_ioRegisters` (line 37):

```csharp
    // Registered I/O handlers, checked before the flat _ioRegisters array.
    private readonly List<(int Start, int End, IIOHandler Handler)> _ioHandlers = new();

    public void RegisterIoHandler(int startAddress, int endAddress, IIOHandler handler)
    {
        if (endAddress < startAddress)
            throw new ArgumentException(
                $"End address ${endAddress:X4} is below start address ${startAddress:X4}");
        if (startAddress < 0xD000 || endAddress > 0xDFFF)
            throw new ArgumentException(
                $"I/O handler range ${startAddress:X4}-${endAddress:X4} must lie within $D000-$DFFF");

        _ioHandlers.Add((startAddress, endAddress, handler));
        Logger.Debug($"Registered I/O handler for ${startAddress:X4}-${endAddress:X4}");
    }

    private IIOHandler? FindHandler(int address)
    {
        foreach (var (start, end, handler) in _ioHandlers)
            if (address >= start && address <= end)
                return handler;
        return null;
    }
```

Change the read path. Replace line 91 (`return _ioRegisters[address - 0xD000]; // I/O visible`) with:

```csharp
            if (Charen)
            {
                var handler = FindHandler(address);
                if (handler != null)
                    return handler.Read(address);
                return _ioRegisters[address - 0xD000]; // I/O visible
            }
```

so the enclosing block reads:

```csharp
        // $D000-$DFFF: I/O, CHAR ROM, or RAM
        if (address < 0xE000)
        {
            if (!LoRam && !HiRam)
                return _ram[address]; // All RAM mode

            if (Charen)
            {
                var handler = FindHandler(address);
                if (handler != null)
                    return handler.Read(address);
                return _ioRegisters[address - 0xD000]; // I/O visible
            }

            return _charRom[address - 0xD000]; // CHAR ROM visible
        }
```

Change the write path. Replace the body at lines 124-131 with:

```csharp
        if (address >= 0xD000 && address < 0xE000)
        {
            if ((LoRam || HiRam) && Charen)
            {
                var handler = FindHandler(address);
                if (handler != null)
                    handler.Write(address, value);
                else
                    _ioRegisters[address - 0xD000] = value;
                // Fall through to also write to RAM!
            }
        }
```

In `Reset()` (line 183), add handler clearing after `_ioRegisters = new byte[0x1000];`:

```csharp
        _ioHandlers.Clear();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~C64MemoryMapIoHandlerTests"`
Expected: PASS — 6 passed.

- [ ] **Step 6: Run the full suite to check for regressions**

Run: `dotnet test`
Expected: PASS — every pre-existing test still green. The C64 read/write path is
used by all C64 tests, so any breakage shows up here.

- [ ] **Step 7: Commit**

```bash
git add sim6502/Systems/IMemoryMap.cs sim6502/Systems/C64MemoryMap.cs \
        sim6502tests/Systems/C64MemoryMapIoHandlerTests.cs
git commit -m "feat(systems): dispatch C64 I/O reads and writes to registered handlers

IIOHandler existed but was never wired up. C64MemoryMap now consults
registered handlers for \$D000-\$DFFF before falling back to the flat
register array, and writes still fall through to RAM underneath."
```

---

## Task 3: UCI constants, reply type, and target seam

Small, pure-data task. No behaviour, so it carries only a compile-and-assert check.

**Files:**
- Create: `sim6502/Systems/Ultimate/UciConstants.cs`
- Create: `sim6502/Systems/Ultimate/UciReply.cs`
- Create: `sim6502/Systems/Ultimate/ICommandTarget.cs`
- Test: `sim6502tests/Systems/Ultimate/UciConstantsTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `static class UciConstants` with the members listed in Step 3.
  - `readonly record struct UciReply(byte[] Data, string Status, bool LastPart)` plus
    `static UciReply Empty(string status)` and `static UciReply Ok(byte[] data)`.
  - `interface ICommandTarget { UciReply ParseCommand(byte[] command); UciReply GetMoreData(); void Abort(int bytesConsumed); }`

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/Systems/Ultimate/UciConstantsTests.cs`:

```csharp
using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class UciConstantsTests
{
    [Fact]
    public void RegisterAddresses_MatchUpstream()
    {
        UciConstants.BusIdAddress.Should().Be(0xDF1B);
        UciConstants.ControlAddress.Should().Be(0xDF1C);
        UciConstants.CommandAddress.Should().Be(0xDF1D);
        UciConstants.ResponseAddress.Should().Be(0xDF1E);
        UciConstants.StatusAddress.Should().Be(0xDF1F);
        UciConstants.Identifier.Should().Be(0xC9);
    }

    [Fact]
    public void BufferGeometry_MatchesCommandIfPkg()
    {
        UciConstants.CommandBufferStart.Should().Be(0);
        UciConstants.CommandBufferSize.Should().Be(896);
        UciConstants.CommandBufferEnd.Should().Be(895);

        UciConstants.ResponseBufferStart.Should().Be(896);
        UciConstants.ResponseBufferSize.Should().Be(896);
        UciConstants.ResponseBufferEnd.Should().Be(1791);

        UciConstants.StatusBufferStart.Should().Be(1792);
        UciConstants.StatusBufferSize.Should().Be(256);
        UciConstants.StatusBufferEnd.Should().Be(2047);

        UciConstants.BackingStoreSize.Should().Be(2048);
    }

    [Fact]
    public void StatusStrings_AreByteExact()
    {
        UciConstants.StatusOk.Should().Be("00,OK");
        UciConstants.StatusUnknownCommand.Should().Be("21,UNKNOWN COMMAND");
        UciConstants.MessageNoTarget.Should().Be("NO TARGET");
    }

    [Fact]
    public void UciReply_Empty_HasNoDataAndIsLastPart()
    {
        var reply = UciReply.Empty(UciConstants.StatusOk);
        reply.Data.Should().BeEmpty();
        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void UciReply_Ok_CarriesDataWithOkStatus()
    {
        var reply = UciReply.Ok(new byte[] { 1, 2, 3 });
        reply.Data.Should().Equal(1, 2, 3);
        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UciConstantsTests"`
Expected: FAIL — compile error, namespace `sim6502.Systems.Ultimate` does not exist.

- [ ] **Step 3: Create `UciConstants.cs`**

```csharp
// Ported from GideonZ/1541ultimate (GPL-3.0):
//   fpga/io/command_interface/vhdl_source/command_if_pkg.vhd
//   software/io/command_interface/command_intf.h
//   software/io/command_interface/command_intf.cc  (status strings)
// Original author: Gideon Zweijtzer. See NOTICE.

namespace sim6502.Systems.Ultimate;

/// <summary>
/// Ultimate Command Interface constants: C64-visible register addresses, status
/// and control bit masks, Ultimate-side handshake values, and buffer geometry.
/// </summary>
public static class UciConstants
{
    // ── C64-visible registers ──
    public const int BusIdAddress    = 0xDF1B; // read: SoftwareIEC bus ID
    public const int ControlAddress  = 0xDF1C; // read: status byte, write: control
    public const int CommandAddress  = 0xDF1D; // read: Identifier, write: command data
    public const int ResponseAddress = 0xDF1E; // read: response data
    public const int StatusAddress   = 0xDF1F; // read: status data

    /// <summary>Reading $DF1D returns this when a UCI is present.</summary>
    public const byte Identifier = 0xC9;

    // ── Control byte, written by the C64 to $DF1C ──
    public const byte ControlPushCommand = 0x01;
    public const byte ControlDataAccept  = 0x02;
    public const byte ControlAbort       = 0x04;
    public const byte ControlClearError  = 0x08;
    public const byte ControlIrqEnable   = 0x20;
    public const byte ControlTrigger     = 0x40;
    public const byte ControlDma         = 0x80;

    // ── Status byte, read by the C64 from $DF1C ──
    public const byte StatusResponseAvailable = 0x80;
    public const byte StatusStatusAvailable   = 0x40;
    public const byte StatusStateMask         = 0x30;
    public const byte StatusError             = 0x08;
    public const byte StatusAbortSet          = 0x04;
    public const byte StatusDataAcceptedSet   = 0x02;
    public const byte StatusNewCommandSet     = 0x01;

    // ── Protocol states, already shifted into bits 5-4 ──
    public const byte StateIdle     = 0x00;
    public const byte StateBusy     = 0x10;
    public const byte StateDataLast = 0x20;
    public const byte StateDataMore = 0x30;

    // ── Ultimate-side handshake-out values ──
    public const byte HandshakeReset      = 0x87;
    public const byte HandshakeAcceptCommand  = 0x01;
    public const byte HandshakeAcceptNextData = 0x02;
    public const byte HandshakeAcceptAbort    = 0x04;
    public const byte HandshakeValidateLast   = 0x10;
    public const byte HandshakeValidateMore   = 0x30;

    // ── Buffer geometry (command_if_pkg.vhd lines 33-41) ──
    public const int CommandBufferStart  = 0;
    public const int CommandBufferSize   = 896;
    public const int CommandBufferEnd    = CommandBufferStart + CommandBufferSize - 1;

    public const int ResponseBufferStart = 896;
    public const int ResponseBufferSize  = 896;
    public const int ResponseBufferEnd   = ResponseBufferStart + ResponseBufferSize - 1;

    public const int StatusBufferStart   = 1792;
    public const int StatusBufferSize    = 256;
    public const int StatusBufferEnd     = StatusBufferStart + StatusBufferSize - 1;

    public const int BackingStoreSize    = 2048;

    /// <summary>Low nibble of command byte 0 selects the target.</summary>
    public const byte TargetMask  = 0x0F;
    /// <summary>Bit 7 of command byte 0 suppresses the reply.</summary>
    public const byte NoReplyFlag = 0x80;
    public const int  MaxTarget   = 0x0F;

    // ── Status strings shared across targets (command_intf.cc lines 223-226) ──
    public const string StatusOk             = "00,OK";
    public const string StatusUnknownCommand = "21,UNKNOWN COMMAND";
    public const string MessageNoTarget      = "NO TARGET";
    public const string StatusEmpty          = "";
}
```

- [ ] **Step 4: Create `UciReply.cs`**

```csharp
namespace sim6502.Systems.Ultimate;

/// <summary>
/// A command target's answer: response data, an ASCII status string, and whether
/// this is the final part. Mirrors the (reply, status) pair plus Message.last_part
/// in Gideon's CommandTarget interface.
/// </summary>
/// <param name="Data">Response bytes. Empty for commands with no data reply.</param>
/// <param name="Status">ASCII status string. Empty string means "no status".</param>
/// <param name="LastPart">False means the C64 should acknowledge and ask again.</param>
public readonly record struct UciReply(byte[] Data, string Status, bool LastPart)
{
    private static readonly byte[] NoData = Array.Empty<byte>();

    /// <summary>A reply carrying only a status, with no response data.</summary>
    public static UciReply Empty(string status) => new(NoData, status, true);

    /// <summary>A final reply carrying data with an OK status.</summary>
    public static UciReply Ok(byte[] data) => new(data, UciConstants.StatusOk, true);

    /// <summary>A non-final reply; the C64 must acknowledge to get the rest.</summary>
    public static UciReply More(byte[] data, string status) => new(data, status, false);
}
```

- [ ] **Step 5: Create `ICommandTarget.cs`**

```csharp
// Ported from GideonZ/1541ultimate (GPL-3.0):
//   software/io/command_interface/command_intf.h  (class CommandTarget)
// Original author: Gideon Zweijtzer. See NOTICE.

namespace sim6502.Systems.Ultimate;

/// <summary>
/// A UCI command target. Byte 0 of every command selects one of these by its low
/// nibble. Targets know what commands mean; UciRegisters knows only the protocol.
/// </summary>
public interface ICommandTarget
{
    /// <summary>
    /// Handle a complete command. <paramref name="command"/> includes byte 0
    /// (target) and byte 1 (command code).
    /// </summary>
    UciReply ParseCommand(byte[] command);

    /// <summary>
    /// Supply the next part after the C64 acknowledged a non-final reply.
    /// </summary>
    UciReply GetMoreData();

    /// <summary>
    /// The C64 aborted mid-transfer. <paramref name="bytesConsumed"/> is how many
    /// response bytes it had already read.
    /// </summary>
    void Abort(int bytesConsumed);
}

/// <summary>
/// Stand-in for unpopulated target slots. Answers IDENTIFY with "NO TARGET" and
/// rejects everything else, matching cmd_if_empty_target upstream.
/// </summary>
public sealed class EmptyCommandTarget : ICommandTarget
{
    public const byte CommandIdentify = 0x01;

    public UciReply ParseCommand(byte[] command)
    {
        if (command.Length > 1 && command[1] == CommandIdentify)
            return new UciReply(
                System.Text.Encoding.ASCII.GetBytes(UciConstants.MessageNoTarget),
                UciConstants.StatusOk,
                true);

        return UciReply.Empty(UciConstants.StatusUnknownCommand);
    }

    public UciReply GetMoreData() => UciReply.Empty(UciConstants.StatusOk);

    public void Abort(int bytesConsumed) { }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~UciConstantsTests"`
Expected: PASS — 5 passed.

- [ ] **Step 7: Commit**

```bash
git add sim6502/Systems/Ultimate/ sim6502tests/Systems/Ultimate/
git commit -m "feat(ultimate): add UCI constants, reply type, and command target seam

Register addresses, control and status bit masks, handshake values, and buffer
geometry ported from command_if_pkg.vhd and command_intf.h. ICommandTarget
mirrors Gideon's CommandTarget so later targets drop in without protocol
changes."
```

---

## Task 4: UciRegisters — register decode and command accumulation

Covers everything the C64 can do up to submitting a command. Target dispatch
arrives in Task 5.

**Files:**
- Create: `sim6502/Systems/Ultimate/UciRegisters.cs`
- Test: `sim6502tests/Systems/Ultimate/UciRegistersDecodeTests.cs`

**Interfaces:**
- Consumes: `UciConstants`, `UciReply`, `ICommandTarget`, `EmptyCommandTarget` (Task 3); `IIOHandler` (`sim6502/Systems/IIOHandler.cs`).
- Produces:
  - `sealed class UciRegisters : IIOHandler`
  - `UciRegisters(int latencyCycles = 0)`
  - `Func<long> CycleCounter { get; set; }` — defaults to `() => 0`
  - `void RegisterTarget(int targetId, ICommandTarget target)`
  - `byte Read(int address)`, `void Write(int address, byte value)`
  - `byte BusId { get; set; }`
  - Internal state exposed for tests: `byte StatusByte { get; }`, `int CommandLength { get; }`

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/Systems/Ultimate/UciRegistersDecodeTests.cs`:

```csharp
using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class UciRegistersDecodeTests
{
    private static UciRegisters NewUci() => new(latencyCycles: 0);

    [Fact]
    public void ReadCommandRegister_ReturnsIdentifier()
    {
        NewUci().Read(UciConstants.CommandAddress).Should().Be(0xC9);
    }

    [Fact]
    public void ReadBusId_ReturnsConfiguredValue()
    {
        var uci = NewUci();
        uci.BusId = 0x0B;
        uci.Read(UciConstants.BusIdAddress).Should().Be(0x0B);
    }

    [Fact]
    public void InitialStatus_IsIdleWithNothingAvailable()
    {
        NewUci().Read(UciConstants.ControlAddress).Should().Be(0x00);
    }

    [Fact]
    public void WritingCommandBytes_AdvancesCommandLength()
    {
        var uci = NewUci();
        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.CommandAddress, 0x11);
        uci.Write(UciConstants.CommandAddress, 0x41);
        uci.CommandLength.Should().Be(3);
    }

    [Fact]
    public void CommandPointer_SaturatesAtBufferEnd()
    {
        var uci = NewUci();
        for (var i = 0; i < UciConstants.CommandBufferSize + 32; i++)
            uci.Write(UciConstants.CommandAddress, 0xAA);

        uci.CommandLength.Should().Be(UciConstants.CommandBufferEnd);
    }

    [Fact]
    public void PushCommandWhenIdle_EntersBusyAndSetsNewCommandFlag()
    {
        var uci = NewUci();
        uci.RegisterTarget(1, new NeverAnsweringTarget());
        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);

        var status = uci.StatusByte;
        (status & UciConstants.StatusStateMask).Should().Be(UciConstants.StateBusy);
        (status & UciConstants.StatusNewCommandSet).Should().Be(UciConstants.StatusNewCommandSet);
    }

    [Fact]
    public void PushCommandWhenNotIdle_SetsErrorFlag()
    {
        var uci = NewUci();
        uci.RegisterTarget(1, new NeverAnsweringTarget());
        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);

        (uci.StatusByte & UciConstants.StatusError).Should().Be(UciConstants.StatusError);
    }

    [Fact]
    public void ClearError_ClearsTheErrorFlag()
    {
        var uci = NewUci();
        uci.RegisterTarget(1, new NeverAnsweringTarget());
        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);
        (uci.StatusByte & UciConstants.StatusError).Should().NotBe(0);

        uci.Write(UciConstants.ControlAddress, UciConstants.ControlClearError);
        (uci.StatusByte & UciConstants.StatusError).Should().Be(0);
    }

    [Fact]
    public void AbortWrite_SetsAbortFlag()
    {
        var uci = NewUci();
        uci.RegisterTarget(1, new NeverAnsweringTarget());
        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlAbort);

        (uci.StatusByte & UciConstants.StatusAbortSet).Should().NotBe(0);
    }

    [Fact]
    public void ReadingResponseWhileIdle_ReturnsZero()
    {
        NewUci().Read(UciConstants.ResponseAddress).Should().Be(0x00);
    }

    [Fact]
    public void ReadingStatusDataWhileIdle_ReturnsZero()
    {
        NewUci().Read(UciConstants.StatusAddress).Should().Be(0x00);
    }

    [Fact]
    public void UnknownAddressInRange_ReadsAsFF()
    {
        NewUci().Read(0xDF1A).Should().Be(0xFF);
    }

    /// <summary>
    /// A target whose ParseCommand is never reached in these tests because the
    /// service loop is suppressed. Used only so RegisterTarget has something to
    /// store; see UciRegistersDispatchTests for real dispatch coverage.
    /// </summary>
    private sealed class NeverAnsweringTarget : ICommandTarget
    {
        public UciReply ParseCommand(byte[] command) => UciReply.Empty(UciConstants.StatusOk);
        public UciReply GetMoreData() => UciReply.Empty(UciConstants.StatusOk);
        public void Abort(int bytesConsumed) { }
    }
}
```

Note for the implementer: several of these tests observe `StateBusy` and
`StatusNewCommandSet` **persisting** after a control write. That only holds while
the service loop has not run. `UciRegisters` must therefore expose a way to
suppress servicing — see Step 3, where servicing runs only when at least one
target is registered *and* `ServiceEnabled` is true. `NeverAnsweringTarget` is
registered but `ServiceEnabled` defaults to false, so state stays observable.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UciRegistersDecodeTests"`
Expected: FAIL — compile error, `UciRegisters` does not exist.

- [ ] **Step 3: Implement `UciRegisters.cs`**

```csharp
// Ported from GideonZ/1541ultimate (GPL-3.0):
//   fpga/io/command_interface/vhdl_source/command_protocol.vhd  (C64-side protocol)
//   software/io/command_interface/command_intf.cc               (service loop)
// Original author: Gideon Zweijtzer. See NOTICE.

using NLog;

namespace sim6502.Systems.Ultimate;

/// <summary>
/// The Ultimate Command Interface as the C64 sees it: five registers at
/// $DF1B-$DF1F backed by three queues, plus the Ultimate-side service loop that
/// dispatches completed commands to an <see cref="ICommandTarget"/>.
///
/// The real UCI is asynchronous — the C64 writes PUSH_CMD then polls $DF1C while
/// the state is Busy. Answering instantly would let a client with a broken or
/// missing busy-wait loop pass here and fail on hardware, so the Busy state is
/// held for <see cref="LatencyCycles"/> CPU cycles before the response appears.
/// </summary>
public sealed class UciRegisters : IIOHandler
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    private static readonly EmptyCommandTarget Empty = new();

    private readonly byte[] _ram = new byte[UciConstants.BackingStoreSize];
    private readonly ICommandTarget?[] _targets = new ICommandTarget?[UciConstants.MaxTarget + 1];

    private int _commandPointer  = UciConstants.CommandBufferStart;
    private int _responsePointer = UciConstants.ResponseBufferStart;
    private int _statusPointer   = UciConstants.StatusBufferStart;
    private int _responseLength;
    private int _statusLength;

    private byte _state = UciConstants.StateIdle;   // already shifted into bits 5-4
    private bool _errorBusy;
    private bool _newCommand;      // handshakeIn[0]
    private bool _dataAccepted;    // handshakeIn[1]
    private bool _abort;           // handshakeIn[2]
    private bool _freeze;
    private bool _trigger;
    private bool _commandIrqEnabled;

    private ICommandTarget? _activeTarget;
    private long _serviceAtCycle;

    /// <summary>Guard against a target that never reports a last part.</summary>
    private const int MaxContinuationParts = 4096;

    public UciRegisters(int latencyCycles = 0)
    {
        if (latencyCycles < 0)
            throw new ArgumentOutOfRangeException(nameof(latencyCycles),
                "UCI latency must not be negative");
        LatencyCycles = latencyCycles;
    }

    /// <summary>Cycles the Busy state is held before a response becomes visible.</summary>
    public int LatencyCycles { get; }

    /// <summary>
    /// Source of the current CPU cycle count. Wired to the processor by the
    /// backend; defaults to a constant so unit tests can run with latency 0.
    /// </summary>
    public Func<long> CycleCounter { get; set; } = () => 0;

    /// <summary>
    /// When false the Ultimate-side service loop never runs, so tests can observe
    /// intermediate protocol state. The backend sets this true.
    /// </summary>
    public bool ServiceEnabled { get; set; }

    /// <summary>Value returned when the C64 reads $DF1B.</summary>
    public byte BusId { get; set; }

    /// <summary>Bytes written to the command register since the last reset.</summary>
    public int CommandLength => _commandPointer - UciConstants.CommandBufferStart;

    public void RegisterTarget(int targetId, ICommandTarget target)
    {
        if (targetId < 0 || targetId > UciConstants.MaxTarget)
            throw new ArgumentOutOfRangeException(nameof(targetId),
                $"Target id must be 0-{UciConstants.MaxTarget}");
        _targets[targetId] = target ?? throw new ArgumentNullException(nameof(target));
    }

    private bool ResponseValid =>
        (_responsePointer - UciConstants.ResponseBufferStart) < _responseLength
        && (_state & 0x20) != 0
        && !_abort;

    private bool StatusValid =>
        (_statusPointer - UciConstants.StatusBufferStart) < _statusLength
        && (_state & 0x20) != 0
        && !_abort;

    /// <summary>The byte the C64 reads from $DF1C.</summary>
    public byte StatusByte
    {
        get
        {
            byte value = _state;
            if (ResponseValid) value |= UciConstants.StatusResponseAvailable;
            if (StatusValid)   value |= UciConstants.StatusStatusAvailable;
            if (_errorBusy)    value |= UciConstants.StatusError;
            if (_abort)        value |= UciConstants.StatusAbortSet;
            if (_dataAccepted) value |= UciConstants.StatusDataAcceptedSet;
            if (_newCommand)   value |= UciConstants.StatusNewCommandSet;
            return value;
        }
    }

    public byte Read(int address)
    {
        switch (address)
        {
            case UciConstants.BusIdAddress:
                return BusId;

            case UciConstants.ControlAddress:
                ServicePending();
                return StatusByte;

            case UciConstants.CommandAddress:
                return UciConstants.Identifier;

            case UciConstants.ResponseAddress:
            {
                var value = ResponseValid ? _ram[_responsePointer] : (byte)0x00;
                _commandIrqEnabled = false;
                if (_responsePointer != UciConstants.ResponseBufferEnd)
                    _responsePointer++;
                return value;
            }

            case UciConstants.StatusAddress:
            {
                var value = StatusValid ? _ram[_statusPointer] : (byte)0x00;
                _commandIrqEnabled = false;
                if (_statusPointer != UciConstants.StatusBufferEnd)
                    _statusPointer++;
                return value;
            }

            default:
                return 0xFF;
        }
    }

    public void Write(int address, byte value)
    {
        switch (address)
        {
            case UciConstants.CommandAddress:
                _ram[_commandPointer] = value;
                if (_commandPointer != UciConstants.CommandBufferEnd)
                    _commandPointer++;
                break;

            case UciConstants.ControlAddress:
                WriteControl(value);
                break;

            default:
                // $DF1B, $DF1E, $DF1F are read-only from the C64 side.
                break;
        }
    }

    // Order of these clauses matches command_protocol.vhd lines 148-170.
    private void WriteControl(byte value)
    {
        if ((value & UciConstants.ControlClearError) != 0)
            _errorBusy = false;

        if ((value & UciConstants.ControlPushCommand) != 0)
        {
            _freeze  = (value & UciConstants.ControlDma) != 0;
            _trigger = (value & UciConstants.ControlTrigger) != 0;

            if (_state == UciConstants.StateIdle)
            {
                _state = UciConstants.StateBusy;
                _newCommand = true;
                ArmService();
            }
            else
            {
                _errorBusy = true;
            }

            _commandIrqEnabled = (value & UciConstants.ControlIrqEnable) != 0;
        }

        if ((value & UciConstants.ControlDataAccept) != 0 && (_state & 0x20) != 0)
        {
            // Only "data more" leaves the accepted flag set for the Ultimate to see.
            _dataAccepted = (_state & 0x10) != 0;
            _state &= unchecked((byte)~0x20);
            _commandIrqEnabled = false;
            if (_dataAccepted) ArmService();
        }

        if ((value & UciConstants.ControlAbort) != 0)
        {
            _abort = true;
            ArmService();
        }

        ServicePending();
    }

    private void ArmService() => _serviceAtCycle = CycleCounter() + LatencyCycles;

    private void ServicePending()
    {
        if (!ServiceEnabled) return;
        if (!_newCommand && !_dataAccepted && !_abort) return;
        if (CycleCounter() < _serviceAtCycle) return;
        ServiceUltimate();
    }

    // Mirrors CommandInterface::run_task, command_intf.cc lines 116-171.
    private void ServiceUltimate()
    {
        if (_abort)
        {
            _activeTarget?.Abort(_responsePointer - UciConstants.ResponseBufferStart);
            HandshakeOut(UciConstants.HandshakeReset);
        }

        if (_dataAccepted)
        {
            if (_activeTarget != null)
            {
                CopyResult(_activeTarget.GetMoreData());
            }
            else
            {
                Logger.Warn("UCI: more data requested but no target is active");
            }
            HandshakeOut(UciConstants.HandshakeAcceptNextData);
        }

        if (_newCommand)
        {
            var length = CommandLength;
            if (length > 0)
            {
                var targetId = _ram[UciConstants.CommandBufferStart] & UciConstants.TargetMask;
                var noReply  = (_ram[UciConstants.CommandBufferStart] & UciConstants.NoReplyFlag) != 0;
                _activeTarget = _targets[targetId] ?? Empty;

                var command = new byte[length];
                Array.Copy(_ram, UciConstants.CommandBufferStart, command, 0, length);

                Logger.Trace($"UCI: target ${targetId:X2} command ${command[1]:X2} ({length} bytes)");
                var reply = _activeTarget.ParseCommand(command);

                HandshakeOut(UciConstants.HandshakeAcceptCommand);

                if (noReply) HandshakeOut(UciConstants.HandshakeReset);
                else CopyResult(reply);
            }
            else
            {
                Logger.Debug("UCI: null command");
                _responseLength = 0;
                _statusLength = 0;
                HandshakeOut(UciConstants.HandshakeAcceptCommand);
                HandshakeOut(UciConstants.HandshakeValidateLast);
            }
        }
    }

    // Mirrors CommandInterface::copy_result, command_intf.cc lines 173-191.
    private void CopyResult(UciReply reply)
    {
        var data = reply.Data;
        if (data.Length > UciConstants.ResponseBufferSize)
        {
            Logger.Warn($"UCI: reply of {data.Length} bytes exceeds the " +
                        $"{UciConstants.ResponseBufferSize}-byte response buffer; truncating");
            data = data[..UciConstants.ResponseBufferSize];
        }

        var status = System.Text.Encoding.ASCII.GetBytes(reply.Status);
        if (status.Length > UciConstants.StatusBufferSize)
        {
            Logger.Warn($"UCI: status of {status.Length} bytes exceeds the " +
                        $"{UciConstants.StatusBufferSize}-byte status buffer; truncating");
            status = status[..UciConstants.StatusBufferSize];
        }

        Array.Copy(data, 0, _ram, UciConstants.ResponseBufferStart, data.Length);
        Array.Copy(status, 0, _ram, UciConstants.StatusBufferStart, status.Length);
        _responseLength = data.Length;
        _statusLength = status.Length;

        HandshakeOut(reply.LastPart
            ? UciConstants.HandshakeValidateLast
            : UciConstants.HandshakeValidateMore);
    }

    // Mirrors command_protocol.vhd lines 209-232 (c_cif_io_handshake_out).
    private void HandshakeOut(byte value)
    {
        if ((value & 0x01) != 0)
        {
            _newCommand = false;
            _commandPointer = UciConstants.CommandBufferStart;
        }

        if ((value & 0x02) != 0)
            _dataAccepted = false;

        if ((value & 0x04) != 0)
            _abort = false;

        if ((value & 0x10) != 0)
        {
            _trigger = false;
            _freeze = false;
            // Set the data bit; bit 5 of the handshake value carries the "more" bit.
            _state = (byte)(0x20 | ((value & 0x20) != 0 ? 0x10 : 0x00));
            ResetResponse();
        }

        if ((value & 0x80) != 0)
        {
            _freeze = false;
            _trigger = false;
            ResetResponse();
            _state = UciConstants.StateIdle;
        }
    }

    private void ResetResponse()
    {
        _responsePointer = UciConstants.ResponseBufferStart;
        _statusPointer = UciConstants.StatusBufferStart;
    }

    /// <summary>
    /// Run a command from the host, bypassing the C64-visible registers. Used by
    /// the DSL's uci() function. Walks every continuation part and concatenates
    /// the data.
    /// </summary>
    public (string Status, byte[] Data) IssueHostCommand(byte[] command)
    {
        if (command.Length < 2)
            throw new ArgumentException("A UCI command needs at least a target byte and a command byte",
                nameof(command));

        var targetId = command[0] & UciConstants.TargetMask;
        var target = _targets[targetId] ?? Empty;

        var data = new List<byte>();
        var status = UciConstants.StatusEmpty;

        var reply = target.ParseCommand(command);
        var parts = 0;
        while (true)
        {
            data.AddRange(reply.Data);
            if (reply.Status.Length > 0) status = reply.Status;
            if (reply.LastPart) break;

            if (++parts > MaxContinuationParts)
            {
                Logger.Error($"UCI: target ${targetId:X2} produced more than " +
                             $"{MaxContinuationParts} parts without a last part; giving up");
                break;
            }
            reply = target.GetMoreData();
        }

        return (status, data.ToArray());
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~UciRegistersDecodeTests"`
Expected: PASS — 12 passed.

- [ ] **Step 5: Commit**

```bash
git add sim6502/Systems/Ultimate/UciRegisters.cs \
        sim6502tests/Systems/Ultimate/UciRegistersDecodeTests.cs
git commit -m "feat(ultimate): implement UCI register decode and command buffering

Ports the C64-side protocol from command_protocol.vhd: register decode,
command buffer accumulation with pointer saturation, status byte assembly,
and the control-write bit order including the error-on-push-while-busy rule."
```

---

## Task 5: UciRegisters — dispatch, response readout, and Busy latency

**Files:**
- Modify: none (Task 4 already wrote the code)
- Test: `sim6502tests/Systems/Ultimate/UciRegistersDispatchTests.cs`

**Interfaces:**
- Consumes: everything produced by Task 4.
- Produces: no new API. This task proves the service loop, response readout,
  `DATA_ACC`, and latency behave correctly, and fixes whatever it finds.

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/Systems/Ultimate/UciRegistersDispatchTests.cs`:

```csharp
using System.Text;
using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class UciRegistersDispatchTests
{
    /// <summary>Answers IDENTIFY with a fixed string, in one part.</summary>
    private sealed class SinglePartTarget : ICommandTarget
    {
        public byte[]? LastCommand { get; private set; }
        public int AbortedAt { get; private set; } = -1;

        public UciReply ParseCommand(byte[] command)
        {
            LastCommand = command;
            return new UciReply(Encoding.ASCII.GetBytes("HELLO"), "00,OK", true);
        }

        public UciReply GetMoreData() => UciReply.Empty("00,OK");
        public void Abort(int bytesConsumed) => AbortedAt = bytesConsumed;
    }

    /// <summary>Answers with three one-byte parts, then stops.</summary>
    private sealed class MultiPartTarget : ICommandTarget
    {
        private int _index;
        public int MoreDataCalls { get; private set; }

        public UciReply ParseCommand(byte[] command)
        {
            _index = 0;
            return Next();
        }

        public UciReply GetMoreData()
        {
            MoreDataCalls++;
            return Next();
        }

        private UciReply Next()
        {
            var payload = new[] { (byte)(0xA0 + _index) };
            _index++;
            return _index >= 3
                ? new UciReply(payload, "00,OK", true)
                : new UciReply(payload, "", false);
        }

        public void Abort(int bytesConsumed) { }
    }

    private static UciRegisters NewUci(ICommandTarget target, int latency = 0, Func<long>? clock = null)
    {
        var uci = new UciRegisters(latency) { ServiceEnabled = true };
        if (clock != null) uci.CycleCounter = clock;
        uci.RegisterTarget(1, target);
        return uci;
    }

    private static void SendCommand(UciRegisters uci, params byte[] bytes)
    {
        foreach (var b in bytes)
            uci.Write(UciConstants.CommandAddress, b);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);
    }

    private static string ReadResponse(UciRegisters uci)
    {
        var sb = new StringBuilder();
        while ((uci.Read(UciConstants.ControlAddress) & UciConstants.StatusResponseAvailable) != 0)
            sb.Append((char)uci.Read(UciConstants.ResponseAddress));
        return sb.ToString();
    }

    private static string ReadStatus(UciRegisters uci)
    {
        var sb = new StringBuilder();
        while ((uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStatusAvailable) != 0)
            sb.Append((char)uci.Read(UciConstants.StatusAddress));
        return sb.ToString();
    }

    [Fact]
    public void Command_IsDeliveredToTargetVerbatim()
    {
        var target = new SinglePartTarget();
        var uci = NewUci(target);
        SendCommand(uci, 0x01, 0x01, 0x2A);

        target.LastCommand.Should().Equal(0x01, 0x01, 0x2A);
    }

    [Fact]
    public void AfterDispatch_StateIsDataLast()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x01, 0x01);

        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateDataLast);
    }

    [Fact]
    public void ResponseBytes_AreReadableThenExhausted()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x01, 0x01);

        ReadResponse(uci).Should().Be("HELLO");
        uci.Read(UciConstants.ResponseAddress).Should().Be(0x00);
    }

    [Fact]
    public void StatusBytes_AreReadableThenExhausted()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x01, 0x01);

        ReadStatus(uci).Should().Be("00,OK");
        uci.Read(UciConstants.StatusAddress).Should().Be(0x00);
    }

    [Fact]
    public void DataAccept_FromDataLast_ReturnsToIdle()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x01, 0x01);
        ReadResponse(uci);

        uci.Write(UciConstants.ControlAddress, UciConstants.ControlDataAccept);

        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateIdle);
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusDataAcceptedSet)
            .Should().Be(0);
    }

    [Fact]
    public void AfterDataAccept_CommandPointerIsReset()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x01, 0x01, 0x02, 0x03);
        uci.CommandLength.Should().Be(0, "AcceptCommand resets the pointer after dispatch");
    }

    [Fact]
    public void MultiPartReply_StateIsDataMoreUntilFinalPart()
    {
        var target = new MultiPartTarget();
        var uci = NewUci(target);
        SendCommand(uci, 0x01, 0x14);

        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateDataMore);
        uci.Read(UciConstants.ResponseAddress).Should().Be(0xA0);

        uci.Write(UciConstants.ControlAddress, UciConstants.ControlDataAccept);
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateDataMore);
        uci.Read(UciConstants.ResponseAddress).Should().Be(0xA1);

        uci.Write(UciConstants.ControlAddress, UciConstants.ControlDataAccept);
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateDataLast);
        uci.Read(UciConstants.ResponseAddress).Should().Be(0xA2);

        target.MoreDataCalls.Should().Be(2);
    }

    [Fact]
    public void Abort_MidTransfer_ReportsBytesConsumedAndReturnsToIdle()
    {
        var target = new SinglePartTarget();
        var uci = NewUci(target);
        SendCommand(uci, 0x01, 0x01);

        uci.Read(UciConstants.ResponseAddress).Should().Be((byte)'H');
        uci.Read(UciConstants.ResponseAddress).Should().Be((byte)'E');

        uci.Write(UciConstants.ControlAddress, UciConstants.ControlAbort);

        target.AbortedAt.Should().Be(2);
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateIdle);
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusAbortSet).Should().Be(0);
    }

    [Fact]
    public void NoReplyFlag_LeavesStateIdleWithNoResponse()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x81, 0x01);   // bit 7 set on the target byte

        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateIdle);
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusResponseAvailable)
            .Should().Be(0);
    }

    [Fact]
    public void UnregisteredTarget_AnswersIdentifyWithNoTarget()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x07, 0x01);   // target 7 is not registered

        ReadResponse(uci).Should().Be("NO TARGET");
        ReadStatus(uci).Should().Be("00,OK");
    }

    [Fact]
    public void UnregisteredTarget_RejectsOtherCommands()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x07, 0x55);

        ReadResponse(uci).Should().BeEmpty();
        ReadStatus(uci).Should().Be("21,UNKNOWN COMMAND");
    }

    [Fact]
    public void BusyState_IsHeldForTheConfiguredLatency()
    {
        long cycles = 0;
        var uci = NewUci(new SinglePartTarget(), latency: 64, clock: () => cycles);

        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);

        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateBusy, "no cycles have elapsed yet");

        cycles = 63;
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateBusy, "one cycle short of the latency");

        cycles = 64;
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateDataLast, "latency has elapsed");
    }

    [Fact]
    public void OversizedReply_IsTruncatedToTheResponseBuffer()
    {
        var uci = NewUci(new OversizedTarget());
        SendCommand(uci, 0x01, 0x01);

        var count = 0;
        while ((uci.Read(UciConstants.ControlAddress) & UciConstants.StatusResponseAvailable) != 0)
        {
            uci.Read(UciConstants.ResponseAddress);
            count++;
            if (count > UciConstants.ResponseBufferSize + 16) break;
        }

        count.Should().Be(UciConstants.ResponseBufferSize);
    }

    private sealed class OversizedTarget : ICommandTarget
    {
        public UciReply ParseCommand(byte[] command) =>
            UciReply.Ok(new byte[UciConstants.ResponseBufferSize + 100]);
        public UciReply GetMoreData() => UciReply.Empty("00,OK");
        public void Abort(int bytesConsumed) { }
    }

    [Fact]
    public void IssueHostCommand_ConcatenatesAllParts()
    {
        var uci = NewUci(new MultiPartTarget());
        var (status, data) = uci.IssueHostCommand(new byte[] { 0x01, 0x14 });

        data.Should().Equal(0xA0, 0xA1, 0xA2);
        status.Should().Be("00,OK");
    }

    [Fact]
    public void IssueHostCommand_RejectsShortCommands()
    {
        var uci = NewUci(new SinglePartTarget());
        var act = () => uci.IssueHostCommand(new byte[] { 0x01 });
        act.Should().Throw<ArgumentException>();
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test --filter "FullyQualifiedName~UciRegistersDispatchTests"`
Expected: PASS — 16 passed. The implementation from Task 4 should satisfy all of
them. If any fail, the failure is a real defect in `UciRegisters` — fix it there,
do not weaken the test. Two places to look first:

- `HandshakeOut` bit-4 handling. `_state = (byte)((_state | 0x20) & 0xF0)` sets the
  data bit and masks to the state nibble; the following line sets or clears the
  "more" bit. If `MultiPartReply_StateIsDataMoreUntilFinalPart` fails, this is why.
- `ServicePending` is called from both `Read(ControlAddress)` and at the end of
  `WriteControl`. If `DataAccept_FromDataLast_ReturnsToIdle` fails, check that
  `DATA_ACC` from `StateDataLast` leaves `_dataAccepted` false so the service loop
  does not then ask the target for more data.

- [ ] **Step 3: Commit**

```bash
git add sim6502tests/Systems/Ultimate/UciRegistersDispatchTests.cs
git commit -m "test(ultimate): cover UCI dispatch, multi-part replies, abort, latency

Pins the service loop against command_intf.cc: verbatim command delivery,
DATA_MORE chaining across DATA_ACC cycles, abort reporting bytes consumed,
the no-reply flag, unregistered-target fallbacks, response truncation, and
that the Busy state is held for the configured cycle latency."
```

---

## Task 6: `UltimateFileSystem`

**Files:**
- Create: `sim6502/Systems/Ultimate/UltimateFileSystem.cs`
- Test: `sim6502tests/Systems/Ultimate/UltimateFileSystemTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `readonly record struct UltimateDirEntry(string Name, byte Attributes, long Size, DateTime Modified)`
  - `sealed class UltimateFileSystem : IDisposable`
    - `UltimateFileSystem(string hostRoot, string mountName = "Usb0")`
    - `const byte AttributeDirectory = 0x10`, `const byte AttributeArchive = 0x20`
    - `string WorkingRoot { get; }` — canonical host path of the throwaway copy
    - `string MountRoot { get; }` — `"/Usb0"`
    - `string CurrentPath { get; }` — starts at `MountRoot`
    - `bool ChangeDirectory(string path)`
    - `string? ResolveToHostPath(string ultimatePath)` — null when the path escapes the root or is malformed
    - `IReadOnlyList<UltimateDirEntry> ListCurrentDirectory()`
    - `void Dispose()`

This is the only component that touches real disk. It is a trust boundary: a UCI
command carrying `../../etc/passwd` must not reach a real path. Two independent
guards are used, deliberately — symlinks are never copied into the working tree, so
they cannot be followed out; and every resolution is canonicalised and prefix
checked, so `..` cannot climb out. Neither guard inspects the input string for
suspicious substrings, which is the approach that reliably gets bypassed.

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/Systems/Ultimate/UltimateFileSystemTests.cs`:

```csharp
using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class UltimateFileSystemTests : IDisposable
{
    private readonly string _fixture;

    public UltimateFileSystemTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64sim-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_fixture, "data"));
        Directory.CreateDirectory(Path.Combine(_fixture, "data", "nested"));
        File.WriteAllText(Path.Combine(_fixture, "hello.txt"), "hello");
        File.WriteAllBytes(Path.Combine(_fixture, "data", "bytes.bin"), new byte[] { 1, 2, 3, 4 });
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    private UltimateFileSystem NewFs() => new(_fixture);

    [Fact]
    public void CurrentPath_StartsAtMountRoot()
    {
        using var fs = NewFs();
        fs.MountRoot.Should().Be("/Usb0");
        fs.CurrentPath.Should().Be("/Usb0");
    }

    [Fact]
    public void Constructor_CopiesFixtureSoOriginalIsNeverMutated()
    {
        using var fs = NewFs();
        var host = fs.ResolveToHostPath("hello.txt");
        host.Should().NotBeNull();
        host.Should().NotStartWith(_fixture, "the working tree must be a copy, not the fixture");

        File.WriteAllText(host!, "overwritten");
        File.ReadAllText(Path.Combine(_fixture, "hello.txt")).Should().Be("hello");
    }

    [Fact]
    public void Dispose_RemovesTheWorkingCopy()
    {
        string host;
        using (var fs = NewFs())
        {
            host = fs.ResolveToHostPath("hello.txt")!;
            File.Exists(host).Should().BeTrue();
        }
        File.Exists(host).Should().BeFalse();
    }

    [Fact]
    public void ChangeDirectory_Relative_Succeeds()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0/data");
        fs.ChangeDirectory("nested").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0/data/nested");
    }

    [Fact]
    public void ChangeDirectory_Absolute_Succeeds()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("/Usb0/data/nested").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0/data/nested");
    }

    [Fact]
    public void ChangeDirectory_DotIsNoOp()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data").Should().BeTrue();
        fs.ChangeDirectory(".").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0/data");
    }

    [Fact]
    public void ChangeDirectory_DotDot_GoesUp()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data/nested").Should().BeTrue();
        fs.ChangeDirectory("..").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0/data");
    }

    [Fact]
    public void ChangeDirectory_DotDotAtRoot_IsNoOpNotAnEscape()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("..").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0");
        fs.ChangeDirectory("../../..").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0");
    }

    [Fact]
    public void ChangeDirectory_NonexistentPath_FailsAndLeavesPathUnchanged()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data").Should().BeTrue();
        fs.ChangeDirectory("nope").Should().BeFalse();
        fs.CurrentPath.Should().Be("/Usb0/data");
    }

    [Fact]
    public void ChangeDirectory_IntoAFile_Fails()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("hello.txt").Should().BeFalse();
        fs.CurrentPath.Should().Be("/Usb0");
    }

    [Fact]
    public void ChangeDirectory_WrongMountName_Fails()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("/SdCard/data").Should().BeFalse();
        fs.CurrentPath.Should().Be("/Usb0");
    }

    [Theory]
    [InlineData("../../../../etc/passwd")]
    [InlineData("/Usb0/../../etc/passwd")]
    [InlineData("data/../../../etc/passwd")]
    public void ResolveToHostPath_TraversalAttempts_StayInsideTheRoot(string attempt)
    {
        using var fs = NewFs();
        var host = fs.ResolveToHostPath(attempt);

        // Either rejected outright, or clamped to somewhere inside the working root.
        if (host != null)
            host.Should().StartWith(fs.WorkingRoot);
        host.Should().NotContain("etc" + Path.DirectorySeparatorChar + "passwd");
    }

    [Fact]
    public void ResolveToHostPath_EmbeddedNul_IsRejected()
    {
        using var fs = NewFs();
        fs.ResolveToHostPath("hel\0lo.txt").Should().BeNull();
    }

    [Fact]
    public void ResolveToHostPath_RelativeToCurrentDirectory()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data").Should().BeTrue();
        var host = fs.ResolveToHostPath("bytes.bin");
        host.Should().NotBeNull();
        File.ReadAllBytes(host!).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void ResolveToHostPath_AbsoluteIgnoresCurrentDirectory()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data/nested").Should().BeTrue();
        var host = fs.ResolveToHostPath("/Usb0/hello.txt");
        host.Should().NotBeNull();
        File.ReadAllText(host!).Should().Be("hello");
    }

    [Fact]
    public void ListCurrentDirectory_ReportsDirectoriesAndFilesWithFatAttributes()
    {
        using var fs = NewFs();
        var entries = fs.ListCurrentDirectory();

        entries.Should().HaveCount(2);
        var dir = entries.Single(e => e.Name == "data");
        dir.Attributes.Should().Be(UltimateFileSystem.AttributeDirectory);

        var file = entries.Single(e => e.Name == "hello.txt");
        file.Attributes.Should().Be(UltimateFileSystem.AttributeArchive);
        file.Size.Should().Be(5);
    }

    [Fact]
    public void ListCurrentDirectory_DirectoriesBeforeFilesEachAlphabetical()
    {
        using var fs = NewFs();
        File.WriteAllText(Path.Combine(fs.WorkingRoot, "aaa.txt"), "a");
        Directory.CreateDirectory(Path.Combine(fs.WorkingRoot, "zzz"));

        var names = fs.ListCurrentDirectory().Select(e => e.Name).ToArray();
        names.Should().Equal("data", "zzz", "aaa.txt", "hello.txt");
    }

    [Fact]
    public void ListCurrentDirectory_EmptyDirectory_IsEmpty()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data/nested").Should().BeTrue();
        fs.ListCurrentDirectory().Should().BeEmpty();
    }

    [Fact]
    public void Constructor_MissingHostRoot_Throws()
    {
        var act = () => new UltimateFileSystem(Path.Combine(_fixture, "does-not-exist"));
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Symlinks_AreNotCopiedIntoTheWorkingTree()
    {
        var outside = Path.Combine(Path.GetTempPath(), "u64sim-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
        try
        {
            try
            {
                File.CreateSymbolicLink(Path.Combine(_fixture, "escape.txt"),
                                        Path.Combine(outside, "secret.txt"));
            }
            catch (Exception)
            {
                return; // platform forbids symlink creation; nothing to assert
            }

            using var fs = NewFs();
            File.Exists(Path.Combine(fs.WorkingRoot, "escape.txt")).Should().BeFalse();
            fs.ListCurrentDirectory().Select(e => e.Name).Should().NotContain("escape.txt");
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UltimateFileSystemTests"`
Expected: FAIL — compile error, `UltimateFileSystem` does not exist.

- [ ] **Step 3: Implement `UltimateFileSystem.cs`**

```csharp
// Models the Ultimate's filesystem namespace as seen through the UCI DOS targets.
// Behaviour corresponds to the Path/FileManager abstractions used by
// GideonZ/1541ultimate software/filemanager/dos.cc (GPL-3.0), reimplemented over
// a host directory. Original author of the upstream behaviour: Gideon Zweijtzer.
// See NOTICE.

using NLog;

namespace sim6502.Systems.Ultimate;

/// <summary>One entry from an Ultimate directory listing.</summary>
/// <param name="Name">Entry name with no path component.</param>
/// <param name="Attributes">FAT attribute byte.</param>
/// <param name="Size">Size in bytes; zero for directories.</param>
/// <param name="Modified">Last write time, used for the FAT date and time fields.</param>
public readonly record struct UltimateDirEntry(
    string Name,
    byte Attributes,
    long Size,
    DateTime Modified);

/// <summary>
/// Exposes a host directory as the Ultimate's mounted filesystem, rooted at
/// <c>/Usb0</c> by default.
///
/// The host tree is copied to a temporary directory at construction and the copy
/// is deleted on dispose, so tests operate on throwaway state and fixture files
/// are never mutated. Symlinks are not copied, so there is no way to follow one
/// out of the working tree. Every path is canonicalised and prefix checked against
/// the working root before it is handed back, so <c>..</c> cannot climb out either.
/// </summary>
public sealed class UltimateFileSystem : IDisposable
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>FAT AM_DIR.</summary>
    public const byte AttributeDirectory = 0x10;
    /// <summary>FAT AM_ARC — what the Ultimate reports for ordinary files.</summary>
    public const byte AttributeArchive = 0x20;

    private readonly string _mountName;
    private readonly List<string> _current = new();
    private bool _disposed;

    public UltimateFileSystem(string hostRoot, string mountName = "Usb0")
    {
        if (string.IsNullOrWhiteSpace(hostRoot))
            throw new ArgumentException("A host root directory is required", nameof(hostRoot));
        if (string.IsNullOrWhiteSpace(mountName))
            throw new ArgumentException("A mount name is required", nameof(mountName));

        var source = Path.GetFullPath(hostRoot);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException(
                $"Ultimate filesystem root not found: '{hostRoot}'");

        _mountName = mountName;
        WorkingRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), "sim6502-u64sim-" + Guid.NewGuid().ToString("N")));

        CopyTree(source, WorkingRoot);
        Logger.Debug($"Ultimate filesystem '{MountRoot}' backed by '{source}', " +
                     $"working copy at '{WorkingRoot}'");
    }

    /// <summary>Canonical host path of the throwaway working copy.</summary>
    public string WorkingRoot { get; }

    /// <summary>The Ultimate-side mount point, e.g. <c>/Usb0</c>.</summary>
    public string MountRoot => "/" + _mountName;

    /// <summary>Current directory as the C64 sees it, e.g. <c>/Usb0/data</c>.</summary>
    public string CurrentPath =>
        _current.Count == 0 ? MountRoot : MountRoot + "/" + string.Join('/', _current);

    /// <summary>
    /// Change directory. Accepts absolute paths under the mount point and relative
    /// paths, and understands <c>.</c> and <c>..</c>. Returns false and leaves the
    /// current directory untouched if the target does not exist or is not a directory.
    /// </summary>
    public bool ChangeDirectory(string path)
    {
        if (!TryNormalise(path, out var segments))
            return false;

        var host = ToHostPath(segments);
        if (host == null || !Directory.Exists(host))
            return false;

        _current.Clear();
        _current.AddRange(segments);
        return true;
    }

    /// <summary>
    /// Map an Ultimate path to a host path. Returns null when the path is malformed
    /// or resolves outside the working root. The returned path is not guaranteed to
    /// exist — callers create, read, or stat it as the command requires.
    /// </summary>
    public string? ResolveToHostPath(string ultimatePath)
    {
        return TryNormalise(ultimatePath, out var segments) ? ToHostPath(segments) : null;
    }

    /// <summary>
    /// List the current directory: directories first, then files, each group sorted
    /// by ordinal name comparison so listings are stable across platforms.
    /// </summary>
    public IReadOnlyList<UltimateDirEntry> ListCurrentDirectory()
    {
        var host = ToHostPath(_current);
        if (host == null || !Directory.Exists(host))
            return Array.Empty<UltimateDirEntry>();

        var entries = new List<UltimateDirEntry>();

        foreach (var dir in Directory.GetDirectories(host).OrderBy(p => p, StringComparer.Ordinal))
        {
            var info = new DirectoryInfo(dir);
            entries.Add(new UltimateDirEntry(info.Name, AttributeDirectory, 0, info.LastWriteTime));
        }

        foreach (var file in Directory.GetFiles(host).OrderBy(p => p, StringComparer.Ordinal))
        {
            var info = new FileInfo(file);
            entries.Add(new UltimateDirEntry(info.Name, AttributeArchive, info.Length, info.LastWriteTime));
        }

        return entries;
    }

    /// <summary>
    /// Split an Ultimate path into normalised segments relative to the mount root.
    /// Returns false for malformed input or a mount name we do not serve.
    /// <c>..</c> at the root is absorbed rather than treated as an escape, matching
    /// the upstream Path behaviour.
    /// </summary>
    private bool TryNormalise(string path, out List<string> segments)
    {
        segments = new List<string>();

        if (path == null) return false;
        if (path.Contains('\0')) return false;

        var trimmed = path.Trim();
        var absolute = trimmed.StartsWith('/');
        var body = trimmed;

        if (absolute)
        {
            var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return true; // "/" means the mount root

            if (!string.Equals(parts[0], _mountName, StringComparison.OrdinalIgnoreCase))
                return false; // a mount we do not serve

            body = string.Join('/', parts.Skip(1));
        }
        else
        {
            segments.AddRange(_current);
        }

        foreach (var segment in body.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;

            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue; // at the root this is a no-op, not an escape
            }

            if (segment.Contains('\0')) { segments.Clear(); return false; }
            segments.Add(segment);
        }

        return true;
    }

    /// <summary>
    /// Combine normalised segments with the working root and confirm the result is
    /// genuinely inside it. This is the second, independent guard: even if
    /// normalisation were wrong, nothing outside the root is ever returned.
    /// </summary>
    private string? ToHostPath(IReadOnlyList<string> segments)
    {
        string candidate;
        try
        {
            candidate = Path.GetFullPath(segments.Count == 0
                ? WorkingRoot
                : Path.Combine(WorkingRoot, Path.Combine(segments.ToArray())));
        }
        catch (Exception ex)
        {
            Logger.Debug($"Ultimate path could not be canonicalised: {ex.Message}");
            return null;
        }

        if (candidate == WorkingRoot)
            return candidate;

        var rootWithSeparator = WorkingRoot.EndsWith(Path.DirectorySeparatorChar)
            ? WorkingRoot
            : WorkingRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            Logger.Warn($"Rejected Ultimate path resolving outside '{MountRoot}': '{candidate}'");
            return null;
        }

        return candidate;
    }

    /// <summary>
    /// Recursively copy a tree, skipping symlinks so the working copy cannot be
    /// used to reach anything outside itself.
    /// </summary>
    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var dir in Directory.GetDirectories(source))
        {
            var info = new DirectoryInfo(dir);
            if (info.LinkTarget != null)
            {
                Logger.Warn($"Skipping symlinked directory '{info.Name}' when building " +
                            "the Ultimate working copy");
                continue;
            }
            CopyTree(dir, Path.Combine(destination, info.Name));
        }

        foreach (var file in Directory.GetFiles(source))
        {
            var info = new FileInfo(file);
            if (info.LinkTarget != null)
            {
                Logger.Warn($"Skipping symlinked file '{info.Name}' when building " +
                            "the Ultimate working copy");
                continue;
            }
            File.Copy(file, Path.Combine(destination, info.Name), overwrite: true);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (Directory.Exists(WorkingRoot))
                Directory.Delete(WorkingRoot, recursive: true);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not remove the Ultimate working copy '{WorkingRoot}': {ex.Message}");
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~UltimateFileSystemTests"`
Expected: PASS — 20 passed (the traversal `[Theory]` contributes 3).

- [ ] **Step 5: Commit**

```bash
git add sim6502/Systems/Ultimate/UltimateFileSystem.cs \
        sim6502tests/Systems/Ultimate/UltimateFileSystemTests.cs
git commit -m "feat(ultimate): map a host directory to the Ultimate /Usb0 namespace

The fixture tree is copied to a temp directory so tests never mutate fixtures.
Two independent guards keep UCI paths inside the root: symlinks are not copied
into the working tree, and every resolution is canonicalised and prefix checked
rather than string-inspected."
```

---

## Task 7: `UltimateDosTarget` — identity, navigation, echo

**Files:**
- Create: `sim6502/Systems/Ultimate/UltimateDosTarget.cs`
- Test: `sim6502tests/Systems/Ultimate/UltimateDosTargetNavigationTests.cs`

**Interfaces:**
- Consumes: `ICommandTarget`, `UciReply`, `UciConstants` (Task 3); `UltimateFileSystem` (Task 6).
- Produces:
  - `sealed class UltimateDosTarget : ICommandTarget`
    - `UltimateDosTarget(UltimateFileSystem fileSystem, string version = "ULTIMATE-II DOS V1.2")`
    - command-code constants `CmdIdentify = 0x01` … `CmdEcho = 0xF0` (full list in Step 3)
    - status-string constants `StatusNoSuchDirectory` … (full list in Step 3)
    - `void ResetState()` — used by `ControlTarget`'s REBOOT in Task 10
  - `protected`/`private` helper `static string ReadString(byte[] command, int offset)`
    used by Tasks 8 and 9.

Tasks 8 and 9 add cases to the same `switch`. Until then, file and directory
commands fall through to `"21,UNKNOWN COMMAND"`; that is expected mid-sequence and
Task 9 adds the test that pins the final command coverage.

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/Systems/Ultimate/UltimateDosTargetNavigationTests.cs`:

```csharp
using System.Text;
using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class UltimateDosTargetNavigationTests : IDisposable
{
    private readonly string _fixture;
    private readonly UltimateFileSystem _fs;
    private readonly UltimateDosTarget _dos;

    public UltimateDosTargetNavigationTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64sim-dosnav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_fixture, "data", "nested"));
        File.WriteAllText(Path.Combine(_fixture, "hello.txt"), "hello");

        _fs = new UltimateFileSystem(_fixture);
        _dos = new UltimateDosTarget(_fs);
    }

    public void Dispose()
    {
        _fs.Dispose();
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    /// <summary>Build a command: target byte, command byte, then an ASCII argument.</summary>
    private static byte[] Cmd(byte code, string? argument = null)
    {
        var bytes = new List<byte> { 0x01, code };
        if (argument != null) bytes.AddRange(Encoding.ASCII.GetBytes(argument));
        return bytes.ToArray();
    }

    private static string Text(UciReply reply) => Encoding.ASCII.GetString(reply.Data);

    [Fact]
    public void Identify_ReturnsTheVersionStringWithOkStatus()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdIdentify));

        Text(reply).Should().Be("ULTIMATE-II DOS V1.2");
        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void Identify_HonoursAConfiguredVersion()
    {
        var dos = new UltimateDosTarget(_fs, "ULTIMATE-II DOS V1.1");
        Text(dos.ParseCommand(Cmd(UltimateDosTarget.CmdIdentify)))
            .Should().Be("ULTIMATE-II DOS V1.1");
    }

    [Fact]
    public void UnknownCommand_IsRejectedWithNoData()
    {
        var reply = _dos.ParseCommand(Cmd(0x7E));

        reply.Data.Should().BeEmpty();
        reply.Status.Should().Be("21,UNKNOWN COMMAND");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void ChangeDir_Relative_MovesAndReportsOk()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "data"));

        reply.Status.Should().Be("00,OK");
        reply.Data.Should().BeEmpty();
        _fs.CurrentPath.Should().Be("/Usb0/data");
    }

    [Fact]
    public void ChangeDir_Absolute_Moves()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "/Usb0/data/nested"))
            .Status.Should().Be("00,OK");
        _fs.CurrentPath.Should().Be("/Usb0/data/nested");
    }

    [Fact]
    public void ChangeDir_DotAndDotDot_Work()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "data/nested"));
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, ".")).Status.Should().Be("00,OK");
        _fs.CurrentPath.Should().Be("/Usb0/data/nested");

        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "..")).Status.Should().Be("00,OK");
        _fs.CurrentPath.Should().Be("/Usb0/data");
    }

    [Fact]
    public void ChangeDir_Nonexistent_FailsAndLeavesThePathAlone()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "data"));

        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "nope"));

        reply.Status.Should().Be("83,NO SUCH DIRECTORY");
        reply.Data.Should().BeEmpty();
        _fs.CurrentPath.Should().Be("/Usb0/data", "a failed cd must not move the path");
    }

    [Fact]
    public void ChangeDir_IntoAFile_Fails()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "hello.txt"))
            .Status.Should().Be("83,NO SUCH DIRECTORY");
        _fs.CurrentPath.Should().Be("/Usb0");
    }

    [Fact]
    public void ChangeDir_TraversalAttempt_CannotEscapeTheMount()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "../../.."));
        _fs.CurrentPath.Should().Be("/Usb0");
    }

    [Fact]
    public void GetPath_ReturnsTheCurrentPath()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "data"));

        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdGetPath));

        Text(reply).Should().Be("/Usb0/data");
        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void GetPath_AtRoot_ReturnsTheMountRoot()
    {
        Text(_dos.ParseCommand(Cmd(UltimateDosTarget.CmdGetPath))).Should().Be("/Usb0");
    }

    [Fact]
    public void CreateDir_MakesTheDirectory()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCreateDir, "fresh"));

        reply.Status.Should().Be("00,OK");
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "fresh")).Status.Should().Be("00,OK");
        _fs.CurrentPath.Should().Be("/Usb0/fresh");
    }

    [Fact]
    public void CreateDir_AlreadyExisting_ReportsAnError()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCreateDir, "twice")).Status.Should().Be("00,OK");
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCreateDir, "twice"))
            .Status.Should().Be("87,INTERNAL ERROR");
    }

    [Fact]
    public void CreateDir_OutsideTheMount_IsRejected()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCreateDir, "/SdCard/evil"));
        reply.Status.Should().Be("83,NO SUCH DIRECTORY");
        Directory.Exists(Path.Combine(_fixture, "..", "SdCard")).Should().BeFalse();
    }

    [Fact]
    public void Echo_ReturnsTheWholeCommandIncludingTheHeaderBytes()
    {
        var command = new byte[] { 0x01, UltimateDosTarget.CmdEcho, 0xDE, 0xAD, 0xBE, 0xEF };

        var reply = _dos.ParseCommand(command);

        reply.Data.Should().Equal(command);
        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void GetMoreData_WhenIdle_ReportsNotInDataMode()
    {
        var reply = _dos.GetMoreData();

        reply.Data.Should().BeEmpty();
        reply.Status.Should().Be("81,NOT IN DATA MODE");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void TwoTargets_HaveIndependentPaths()
    {
        var second = new UltimateDosTarget(new UltimateFileSystem(_fixture));
        try
        {
            _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "data"));

            Text(second.ParseCommand(Cmd(UltimateDosTarget.CmdGetPath))).Should().Be("/Usb0");
            Text(_dos.ParseCommand(Cmd(UltimateDosTarget.CmdGetPath))).Should().Be("/Usb0/data");
        }
        finally
        {
            second.Dispose();
        }
    }

    [Fact]
    public void ShortCommand_IsRejectedRatherThanThrowing()
    {
        var reply = _dos.ParseCommand(new byte[] { 0x01 });
        reply.Status.Should().Be("21,UNKNOWN COMMAND");
    }
}
```

Note the `second.Dispose()` in `TwoTargets_HaveIndependentPaths`: the target owns
nothing, but it needs an `IDisposable` surface to release the file handle opened in
Task 8 and the second `UltimateFileSystem` created here. Implement
`UltimateDosTarget : ICommandTarget, IDisposable` in Step 3, disposing the
filesystem it was handed only when it created it — here the test hands one in, so
have `UltimateDosTarget` dispose the filesystem it holds. That keeps the test
correct and the ownership rule simple: whoever constructs the target hands over the
filesystem.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UltimateDosTargetNavigationTests"`
Expected: FAIL — compile error, `UltimateDosTarget` does not exist.

- [ ] **Step 3: Implement `UltimateDosTarget.cs`**

```csharp
// Ported from GideonZ/1541ultimate (GPL-3.0):
//   software/filemanager/dos.cc
//   software/filemanager/dos.h
// Original author: Gideon Zweijtzer. See NOTICE.

using System.Text;
using NLog;

namespace sim6502.Systems.Ultimate;

/// <summary>
/// The Ultimate DOS command target, served at UCI targets $01 and $02. Each
/// instance keeps its own current directory, open file, and data-mode state, so
/// two of them can be in use at once without interfering.
/// </summary>
public sealed class UltimateDosTarget : ICommandTarget, IDisposable
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    // ── Command codes (dos.h lines 11-38) ──
    public const byte CmdIdentify      = 0x01;
    public const byte CmdOpenFile      = 0x02;
    public const byte CmdCloseFile     = 0x03;
    public const byte CmdReadData      = 0x04;
    public const byte CmdWriteData     = 0x05;
    public const byte CmdFileSeek      = 0x06;
    public const byte CmdFileInfo      = 0x07;
    public const byte CmdFileStat      = 0x08;
    public const byte CmdDeleteFile    = 0x09;
    public const byte CmdRenameFile    = 0x0A;
    public const byte CmdCopyFile      = 0x0B;
    public const byte CmdChangeDir     = 0x11;
    public const byte CmdGetPath       = 0x12;
    public const byte CmdOpenDir       = 0x13;
    public const byte CmdReadDir       = 0x14;
    public const byte CmdCopyUiPath    = 0x15;
    public const byte CmdCreateDir     = 0x16;
    public const byte CmdCopyHomePath  = 0x17;
    public const byte CmdLoadReu       = 0x21;
    public const byte CmdSaveReu       = 0x22;
    public const byte CmdMountDisk     = 0x23;
    public const byte CmdUnmountDisk   = 0x24;
    public const byte CmdSwapDisk      = 0x25;
    public const byte CmdGetTime       = 0x26;
    public const byte CmdSetTime       = 0x27;
    public const byte CmdEcho          = 0xF0;

    // ── File attribute flags for OPEN_FILE ──
    public const byte FileAttributeRead         = 0x01;
    public const byte FileAttributeWrite        = 0x02;
    public const byte FileAttributeCreateNew    = 0x04;
    public const byte FileAttributeCreateAlways = 0x08;

    // ── Status strings, byte-exact from dos.cc lines 15-30 ──
    public const string StatusDirectoryEmpty   = "01,DIRECTORY EMPTY";
    public const string StatusTruncated        = "02,REQUEST TRUNCATED";
    public const string StatusNotImplemented   = "99,FUNCTION NOT IMPLEMENTED";
    public const string StatusNotInDataMode    = "81,NOT IN DATA MODE";
    public const string StatusFileNotFound     = "82,FILE NOT FOUND";
    public const string StatusNoSuchDirectory  = "83,NO SUCH DIRECTORY";
    public const string StatusNoFileToClose    = "84,NO FILE TO CLOSE";
    public const string StatusNoFileOpen       = "85,NO FILE OPEN";
    public const string StatusCannotReadDir    = "86,CAN'T READ DIRECTORY";
    public const string StatusInternalError    = "87,INTERNAL ERROR";
    public const string StatusNoInformation    = "88,NO INFORMATION AVAILABLE";
    public const string StatusNotADiskImage    = "89,NOT A DISK IMAGE";
    public const string StatusDriveNotPresent  = "90,DRIVE NOT PRESENT";
    public const string StatusIncompatible     = "91,INCOMPATIBLE IMAGE";
    public const string StatusProhibited       = "98,FUNCTION PROHIBITED";

    /// <summary>Read chunk size, matching dos.cc get_more_data.</summary>
    public const int ReadChunkSize = 512;

    private enum DosState
    {
        Idle,
        InFile,
        InDirectory
    }

    private readonly UltimateFileSystem _fileSystem;
    private readonly string _version;

    private DosState _state = DosState.Idle;
    private FileStream? _file;
    private int _remaining;
    private IReadOnlyList<UltimateDirEntry> _directory = Array.Empty<UltimateDirEntry>();
    private int _directoryIndex;
    private bool _disposed;

    public UltimateDosTarget(UltimateFileSystem fileSystem, string version = "ULTIMATE-II DOS V1.2")
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _version = version;
    }

    public UciReply ParseCommand(byte[] command)
    {
        if (command.Length < 2)
        {
            Logger.Warn("DOS: command shorter than two bytes");
            return UciReply.Empty(UciConstants.StatusUnknownCommand);
        }

        return command[1] switch
        {
            CmdIdentify  => new UciReply(Encoding.ASCII.GetBytes(_version), UciConstants.StatusOk, true),
            CmdChangeDir => ChangeDirectory(ReadString(command, 2)),
            CmdGetPath   => UciReply.Ok(Encoding.ASCII.GetBytes(_fileSystem.CurrentPath)),
            CmdCreateDir => CreateDirectory(ReadString(command, 2)),
            CmdEcho      => new UciReply(command, UciConstants.StatusOk, true),

            _ => UciReply.Empty(UciConstants.StatusUnknownCommand)
        };
    }

    public UciReply GetMoreData()
    {
        switch (_state)
        {
            case DosState.Idle:
                Logger.Debug("DOS: more data requested while idle");
                return UciReply.Empty(StatusNotInDataMode);

            default:
                Logger.Warn($"DOS: unhandled data-mode state {_state}");
                _state = DosState.Idle;
                return UciReply.Empty(StatusInternalError);
        }
    }

    public void Abort(int bytesConsumed)
    {
        Logger.Debug($"DOS: aborted after {bytesConsumed} response bytes");
        _state = DosState.Idle;
    }

    /// <summary>
    /// Drop all transient state: closes any open file and leaves data mode. Used by
    /// the control target's REBOOT.
    /// </summary>
    public void ResetState()
    {
        _file?.Dispose();
        _file = null;
        _state = DosState.Idle;
        _remaining = 0;
        _directory = Array.Empty<UltimateDirEntry>();
        _directoryIndex = 0;
    }

    private UciReply ChangeDirectory(string path)
    {
        // UltimateFileSystem.ChangeDirectory leaves the current path untouched on
        // failure, so there is nothing to roll back here.
        return _fileSystem.ChangeDirectory(path)
            ? UciReply.Empty(UciConstants.StatusOk)
            : UciReply.Empty(StatusNoSuchDirectory);
    }

    private UciReply CreateDirectory(string name)
    {
        var host = _fileSystem.ResolveToHostPath(name);
        if (host == null)
            return UciReply.Empty(StatusNoSuchDirectory);

        if (Directory.Exists(host) || File.Exists(host))
            return UciReply.Empty(StatusInternalError);

        try
        {
            Directory.CreateDirectory(host);
            return UciReply.Empty(UciConstants.StatusOk);
        }
        catch (Exception ex)
        {
            Logger.Warn($"DOS: could not create directory '{name}': {ex.Message}");
            return UciReply.Empty(StatusInternalError);
        }
    }

    /// <summary>
    /// Read an ASCII string from the command, ending at an embedded NUL or the end
    /// of the command. Upstream writes a NUL at command[length] and reads a C
    /// string, which is the same thing.
    /// </summary>
    internal static string ReadString(byte[] command, int offset)
    {
        if (offset >= command.Length) return string.Empty;

        var end = Array.IndexOf(command, (byte)0x00, offset);
        if (end < 0 || end > command.Length) end = command.Length;

        return Encoding.ASCII.GetString(command, offset, end - offset);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _file?.Dispose();
        _file = null;
        _fileSystem.Dispose();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~UltimateDosTargetNavigationTests"`
Expected: PASS — 18 passed.

- [ ] **Step 5: Commit**

```bash
git add sim6502/Systems/Ultimate/UltimateDosTarget.cs \
        sim6502tests/Systems/Ultimate/UltimateDosTargetNavigationTests.cs
git commit -m "feat(ultimate): Ultimate DOS identity, directory navigation, and echo

Command codes, file attribute flags, and all status strings transcribed
byte-exact from dos.cc. Covers IDENTIFY, CHANGE_DIR, GET_PATH, CREATE_DIR,
and ECHO; file and directory-listing commands follow."
```

---

## Task 8: `UltimateDosTarget` — file open, read, write, seek, close

**Files:**
- Modify: `sim6502/Systems/Ultimate/UltimateDosTarget.cs`
- Test: `sim6502tests/Systems/Ultimate/UltimateDosTargetFileTests.cs`

**Interfaces:**
- Consumes: everything from Task 7.
- Produces: no new public members beyond the existing constants. `GetMoreData`
  gains its `InFile` behaviour, which Task 9 extends with `InDirectory`.

Two deliberate deviations from upstream, both to be recorded as comments in the
code:

1. `dos.cc get_more_data` (lines 784-803) assigns `*status` only on the success
   path. When `file->read` fails it fills `status_message` but never points
   `*status` at it, so the caller reads an unassigned pointer. That is an upstream
   defect, not behaviour to replicate; the port assigns the error status explicitly.
2. Upstream reports open and I/O failures through `FileSystem::get_error_string`,
   which yields FatFs error text. Reproducing that verbatim would mean porting the
   FatFs error table for no test value, so the port maps failures onto the
   documented DOS statuses: missing file to `"82,FILE NOT FOUND"`, everything else
   to `"87,INTERNAL ERROR"`.

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/Systems/Ultimate/UltimateDosTargetFileTests.cs`:

```csharp
using System.Text;
using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class UltimateDosTargetFileTests : IDisposable
{
    private readonly string _fixture;
    private readonly UltimateFileSystem _fs;
    private readonly UltimateDosTarget _dos;

    private static readonly byte[] Payload =
        Enumerable.Range(0, 1300).Select(i => (byte)(i & 0xFF)).ToArray();

    public UltimateDosTargetFileTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64sim-dosfile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixture);
        File.WriteAllText(Path.Combine(_fixture, "short.txt"), "hello");
        File.WriteAllBytes(Path.Combine(_fixture, "big.bin"), Payload);

        _fs = new UltimateFileSystem(_fixture);
        _dos = new UltimateDosTarget(_fs);
    }

    public void Dispose()
    {
        _dos.Dispose();
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    private static byte[] Cmd(byte code, params byte[] rest)
    {
        var bytes = new List<byte> { 0x01, code };
        bytes.AddRange(rest);
        return bytes.ToArray();
    }

    private static byte[] OpenCmd(byte attributes, string name)
    {
        var bytes = new List<byte> { 0x01, UltimateDosTarget.CmdOpenFile, attributes };
        bytes.AddRange(Encoding.ASCII.GetBytes(name));
        return bytes.ToArray();
    }

    private static byte[] ReadCmd(int length) => Cmd(
        UltimateDosTarget.CmdReadData, (byte)(length & 0xFF), (byte)((length >> 8) & 0xFF));

    /// <summary>Drain a data-mode read across every continuation part.</summary>
    private (byte[] Data, string FinalStatus) Drain(UciReply first)
    {
        var data = new List<byte>(first.Data);
        var reply = first;
        var guard = 0;
        while (!reply.LastPart)
        {
            if (++guard > 64) throw new InvalidOperationException("data mode never terminated");
            reply = _dos.GetMoreData();
            data.AddRange(reply.Data);
        }
        return (data.ToArray(), reply.Status);
    }

    [Fact]
    public void OpenFile_ForRead_Succeeds()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "short.txt"))
            .Status.Should().Be("00,OK");
    }

    [Fact]
    public void OpenFile_Missing_ReportsFileNotFound()
    {
        var reply = _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "nope.txt"));

        reply.Status.Should().Be("82,FILE NOT FOUND");
        reply.Data.Should().BeEmpty();
    }

    [Fact]
    public void OpenFile_OutsideTheMount_IsRejected()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "/SdCard/x.txt"))
            .Status.Should().Be("82,FILE NOT FOUND");
    }

    [Fact]
    public void OpenFile_CreateAlways_TruncatesAnExistingFile()
    {
        _dos.ParseCommand(OpenCmd(
                (byte)(UltimateDosTarget.FileAttributeWrite | UltimateDosTarget.FileAttributeCreateAlways),
                "short.txt"))
            .Status.Should().Be("00,OK");
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCloseFile)).Status.Should().Be("00,OK");

        new FileInfo(_fs.ResolveToHostPath("short.txt")!).Length.Should().Be(0);
    }

    [Fact]
    public void OpenFile_CreateNew_FailsWhenTheFileExists()
    {
        _dos.ParseCommand(OpenCmd(
                (byte)(UltimateDosTarget.FileAttributeWrite | UltimateDosTarget.FileAttributeCreateNew),
                "short.txt"))
            .Status.Should().Be("87,INTERNAL ERROR");
    }

    [Fact]
    public void OpenFile_CreateNew_MakesAFreshFile()
    {
        _dos.ParseCommand(OpenCmd(
                (byte)(UltimateDosTarget.FileAttributeWrite | UltimateDosTarget.FileAttributeCreateNew),
                "fresh.dat"))
            .Status.Should().Be("00,OK");
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCloseFile)).Status.Should().Be("00,OK");

        File.Exists(_fs.ResolveToHostPath("fresh.dat")!).Should().BeTrue();
    }

    [Fact]
    public void OpenFile_ReplacesAnAlreadyOpenFile()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "short.txt"));
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "big.bin"))
            .Status.Should().Be("00,OK");

        var (data, _) = Drain(_dos.ParseCommand(ReadCmd(4)));
        data.Should().Equal(Payload.Take(4));
    }

    [Fact]
    public void CloseFile_WithNoOpenFile_ReportsNoFileToClose()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCloseFile))
            .Status.Should().Be("84,NO FILE TO CLOSE");
    }

    [Fact]
    public void CloseFile_Twice_ReportsNoFileToCloseTheSecondTime()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "short.txt"));
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCloseFile)).Status.Should().Be("00,OK");
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCloseFile))
            .Status.Should().Be("84,NO FILE TO CLOSE");
    }

    [Fact]
    public void ReadData_WithNoOpenFile_ReportsNoFileOpen()
    {
        var reply = _dos.ParseCommand(ReadCmd(16));

        reply.Status.Should().Be("85,NO FILE OPEN");
        reply.Data.Should().BeEmpty();
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void ReadData_ShorterThanOneChunk_ArrivesInASinglePart()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "short.txt"));

        var reply = _dos.ParseCommand(ReadCmd(5));

        Encoding.ASCII.GetString(reply.Data).Should().Be("hello");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void ReadData_PastEndOfFile_StopsAtTheShortRead()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "short.txt"));

        var (data, _) = Drain(_dos.ParseCommand(ReadCmd(100)));

        Encoding.ASCII.GetString(data).Should().Be("hello");
    }

    [Fact]
    public void ReadData_SpansChunksOf512Bytes()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "big.bin"));

        var first = _dos.ParseCommand(ReadCmd(1300));
        first.Data.Should().HaveCount(UltimateDosTarget.ReadChunkSize);
        first.LastPart.Should().BeFalse();

        var second = _dos.GetMoreData();
        second.Data.Should().HaveCount(UltimateDosTarget.ReadChunkSize);
        second.LastPart.Should().BeFalse();

        var third = _dos.GetMoreData();
        third.Data.Should().HaveCount(1300 - 2 * UltimateDosTarget.ReadChunkSize);
        third.LastPart.Should().BeTrue();
    }

    [Fact]
    public void ReadData_ReturnsTheWholePayloadAcrossChunks()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "big.bin"));

        var (data, _) = Drain(_dos.ParseCommand(ReadCmd(1300)));

        data.Should().Equal(Payload);
    }

    [Fact]
    public void ReadData_NonFinalChunks_CarryNoStatus()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "big.bin"));

        _dos.ParseCommand(ReadCmd(1300)).Status.Should().BeEmpty();
    }

    [Fact]
    public void ReadData_ZeroLength_IsAnImmediateLastPart()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "short.txt"));

        var reply = _dos.ParseCommand(ReadCmd(0));

        reply.Data.Should().BeEmpty();
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void ReadData_AfterCompletion_LeavesDataMode()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "short.txt"));
        Drain(_dos.ParseCommand(ReadCmd(5)));

        _dos.GetMoreData().Status.Should().Be("81,NOT IN DATA MODE");
    }

    [Fact]
    public void Abort_LeavesDataModeMidTransfer()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "big.bin"));
        _dos.ParseCommand(ReadCmd(1300)).LastPart.Should().BeFalse();

        _dos.Abort(UltimateDosTarget.ReadChunkSize);

        _dos.GetMoreData().Status.Should().Be("81,NOT IN DATA MODE");
    }

    [Fact]
    public void WriteData_WithNoOpenFile_ReportsNoFileOpen()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdWriteData, 0x00, 0x00, 0x41))
            .Status.Should().Be("85,NO FILE OPEN");
    }

    [Fact]
    public void WriteData_SkipsTheTwoDummyBytes()
    {
        _dos.ParseCommand(OpenCmd(
            (byte)(UltimateDosTarget.FileAttributeWrite | UltimateDosTarget.FileAttributeCreateAlways),
            "out.dat"));

        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdWriteData, 0xFF, 0xFF, 0x41, 0x42, 0x43))
            .Status.Should().Be("00,OK");
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCloseFile)).Status.Should().Be("00,OK");

        File.ReadAllBytes(_fs.ResolveToHostPath("out.dat")!).Should().Equal(0x41, 0x42, 0x43);
    }

    [Fact]
    public void WriteData_WithNoPayload_IsAcceptedAndWritesNothing()
    {
        _dos.ParseCommand(OpenCmd(
            (byte)(UltimateDosTarget.FileAttributeWrite | UltimateDosTarget.FileAttributeCreateAlways),
            "empty.dat"));

        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdWriteData, 0x00, 0x00))
            .Status.Should().Be("00,OK");
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCloseFile));

        new FileInfo(_fs.ResolveToHostPath("empty.dat")!).Length.Should().Be(0);
    }

    [Fact]
    public void WriteThenReadBack_RoundTrips()
    {
        _dos.ParseCommand(OpenCmd(
            (byte)(UltimateDosTarget.FileAttributeWrite | UltimateDosTarget.FileAttributeCreateAlways),
            "round.dat"));
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdWriteData, 0x00, 0x00, 0xDE, 0xAD, 0xBE, 0xEF));
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCloseFile));

        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "round.dat"));
        var (data, _) = Drain(_dos.ParseCommand(ReadCmd(4)));

        data.Should().Equal(0xDE, 0xAD, 0xBE, 0xEF);
    }

    [Fact]
    public void FileSeek_WithNoOpenFile_ReportsNoFileOpen()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileSeek, 0x00, 0x00, 0x00, 0x00))
            .Status.Should().Be("85,NO FILE OPEN");
    }

    [Fact]
    public void FileSeek_MovesTheReadPosition()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "big.bin"));

        // 0x00000200 = 512, little-endian
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileSeek, 0x00, 0x02, 0x00, 0x00))
            .Status.Should().Be("00,OK");

        var (data, _) = Drain(_dos.ParseCommand(ReadCmd(4)));
        data.Should().Equal(Payload.Skip(512).Take(4));
    }

    [Fact]
    public void FileSeek_Is32BitLittleEndian()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "big.bin"));

        // 0x00000004
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileSeek, 0x04, 0x00, 0x00, 0x00));
        var (data, _) = Drain(_dos.ParseCommand(ReadCmd(2)));

        data.Should().Equal(Payload[4], Payload[5]);
    }

    [Fact]
    public void FileSeek_BeyondEndOfFile_ClampsToTheEnd()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "short.txt"));

        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileSeek, 0xFF, 0x00, 0x00, 0x00))
            .Status.Should().Be("00,OK");

        _dos.ParseCommand(ReadCmd(4)).Data.Should().BeEmpty();
    }

    [Fact]
    public void FileSeek_TooShortACommand_ReportsAnError()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "short.txt"));

        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileSeek, 0x00))
            .Status.Should().Be("87,INTERNAL ERROR");
    }

    [Fact]
    public void ResetState_ClosesTheOpenFileAndLeavesDataMode()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "big.bin"));
        _dos.ParseCommand(ReadCmd(1300));

        _dos.ResetState();

        _dos.GetMoreData().Status.Should().Be("81,NOT IN DATA MODE");
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCloseFile))
            .Status.Should().Be("84,NO FILE TO CLOSE");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UltimateDosTargetFileTests"`
Expected: FAIL — most tests fail with status `"21,UNKNOWN COMMAND"`, because the
file commands are not yet in the switch.

- [ ] **Step 3: Add the file command cases**

In `UltimateDosTarget.ParseCommand`, add these five cases above the `_ =>` default:

```csharp
            CmdOpenFile  => OpenFile(command),
            CmdCloseFile => CloseFile(),
            CmdReadData  => BeginRead(command),
            CmdWriteData => WriteData(command),
            CmdFileSeek  => Seek(command),
```

- [ ] **Step 4: Add the file operation methods**

Insert these into `UltimateDosTarget`, after `CreateDirectory`:

```csharp
    private UciReply OpenFile(byte[] command)
    {
        if (command.Length < 3)
            return UciReply.Empty(StatusInternalError);

        var attributes = command[2];
        var name = ReadString(command, 3);

        var host = _fileSystem.ResolveToHostPath(name);
        if (host == null)
        {
            Logger.Warn($"DOS: open rejected for out-of-mount path '{name}'");
            return UciReply.Empty(StatusFileNotFound);
        }

        // FatFs flag semantics: CREATE_ALWAYS truncates, CREATE_NEW must not exist,
        // otherwise the file must already be there.
        var mode = (attributes & FileAttributeCreateAlways) != 0 ? FileMode.Create
                 : (attributes & FileAttributeCreateNew) != 0    ? FileMode.CreateNew
                 : FileMode.Open;

        var wantsWrite = (attributes & FileAttributeWrite) != 0;
        var wantsRead  = (attributes & FileAttributeRead) != 0 || !wantsWrite;

        var access = wantsRead && wantsWrite ? FileAccess.ReadWrite
                   : wantsWrite              ? FileAccess.Write
                   : FileAccess.Read;

        // .NET forbids creating a file opened read-only; widen so the flag
        // combination the C64 asked for still works.
        if (mode != FileMode.Open && access == FileAccess.Read)
            access = FileAccess.ReadWrite;

        _file?.Dispose();
        _file = null;
        _state = DosState.Idle;

        try
        {
            _file = new FileStream(host, mode, access);
            return UciReply.Empty(UciConstants.StatusOk);
        }
        catch (FileNotFoundException)
        {
            return UciReply.Empty(StatusFileNotFound);
        }
        catch (DirectoryNotFoundException)
        {
            return UciReply.Empty(StatusNoSuchDirectory);
        }
        catch (Exception ex)
        {
            // Upstream surfaces FatFs error text here. Porting that table buys no
            // test value, so failures map onto the documented DOS statuses.
            Logger.Warn($"DOS: could not open '{name}': {ex.Message}");
            return UciReply.Empty(StatusInternalError);
        }
    }

    private UciReply CloseFile()
    {
        if (_file == null)
            return UciReply.Empty(StatusNoFileToClose);

        _file.Dispose();
        _file = null;
        _state = DosState.Idle;
        return UciReply.Empty(UciConstants.StatusOk);
    }

    private UciReply BeginRead(byte[] command)
    {
        if (_file == null)
            return UciReply.Empty(StatusNoFileOpen);

        if (command.Length < 4)
            return UciReply.Empty(StatusInternalError);

        _remaining = (command[3] << 8) | command[2];
        _state = DosState.InFile;
        return GetMoreData();
    }

    private UciReply WriteData(byte[] command)
    {
        if (_file == null)
            return UciReply.Empty(StatusNoFileOpen);

        // Bytes 2 and 3 are dummies; the payload starts at byte 4.
        var offset = 4;
        var count = Math.Max(0, command.Length - offset);

        try
        {
            if (count > 0)
                _file.Write(command, offset, count);
            _file.Flush();
            return UciReply.Empty(UciConstants.StatusOk);
        }
        catch (Exception ex)
        {
            Logger.Warn($"DOS: write of {count} bytes failed: {ex.Message}");
            return UciReply.Empty(StatusInternalError);
        }
    }

    private UciReply Seek(byte[] command)
    {
        if (_file == null)
            return UciReply.Empty(StatusNoFileOpen);

        if (command.Length < 6)
            return UciReply.Empty(StatusInternalError);

        var position = (long)command[2]
                     | ((long)command[3] << 8)
                     | ((long)command[4] << 16)
                     | ((long)command[5] << 24);

        try
        {
            // FatFs clamps a seek past the end on a read-only file rather than
            // failing, so clamp here too.
            _file.Position = Math.Min(position, _file.Length);
            return UciReply.Empty(UciConstants.StatusOk);
        }
        catch (Exception ex)
        {
            Logger.Warn($"DOS: seek to {position} failed: {ex.Message}");
            return UciReply.Empty(StatusInternalError);
        }
    }

    private UciReply ReadNextChunk()
    {
        var length = Math.Min(_remaining, ReadChunkSize);
        var buffer = new byte[length];
        int transferred;

        try
        {
            transferred = length == 0 ? 0 : _file!.Read(buffer, 0, length);
        }
        catch (Exception ex)
        {
            // dos.cc leaves *status unassigned on this path — an upstream defect.
            // Assign the error status explicitly instead.
            Logger.Warn($"DOS: read failed: {ex.Message}");
            _state = DosState.Idle;
            return UciReply.Empty(StatusInternalError);
        }

        _remaining -= transferred;

        var lastPart = transferred != length || _remaining == 0;
        if (lastPart)
            _state = DosState.Idle;

        var data = transferred == length ? buffer : buffer[..transferred];
        return new UciReply(data, UciConstants.StatusEmpty, lastPart);
    }
```

- [ ] **Step 5: Add the `InFile` branch to `GetMoreData`**

Replace the `switch (_state)` body in `GetMoreData` with:

```csharp
        switch (_state)
        {
            case DosState.Idle:
                Logger.Debug("DOS: more data requested while idle");
                return UciReply.Empty(StatusNotInDataMode);

            case DosState.InFile:
                return ReadNextChunk();

            default:
                Logger.Warn($"DOS: unhandled data-mode state {_state}");
                _state = DosState.Idle;
                return UciReply.Empty(StatusInternalError);
        }
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~UltimateDosTargetFileTests"`
Expected: PASS — 27 passed.

- [ ] **Step 7: Check the navigation tests still pass**

Run: `dotnet test --filter "FullyQualifiedName~UltimateDosTarget"`
Expected: PASS — 45 passed across both files.

- [ ] **Step 8: Commit**

```bash
git add sim6502/Systems/Ultimate/UltimateDosTarget.cs \
        sim6502tests/Systems/Ultimate/UltimateDosTargetFileTests.cs
git commit -m "feat(ultimate): Ultimate DOS file open, read, write, seek, and close

READ_DATA chunks at 512 bytes through the DATA_MORE path and ends on a short
read or an exhausted length, matching dos.cc get_more_data. Two documented
deviations: the error status on a failed read is assigned explicitly rather
than left as upstream's unassigned pointer, and FatFs error text is mapped
onto the documented DOS statuses."
```

---

## Task 9: `UltimateDosTarget` — info, stat, delete, rename, copy, directory listing

**Files:**
- Modify: `sim6502/Systems/Ultimate/UltimateDosTarget.cs`
- Test: `sim6502tests/Systems/Ultimate/UltimateDosTargetInfoTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 7 and 8.
- Produces: `GetMoreData` gains its `InDirectory` branch. Two internal statics
  become available for reuse and direct testing:
  - `static ushort FatDate(DateTime when)`
  - `static ushort FatTime(DateTime when)`

`FILE_INFO` and `FILE_STAT` reply with the `t_dos_info` struct as upstream
`memcpy`s it (`dos.h` lines 46-53), little-endian, `12 + name.Length` bytes with no
terminator:

| Offset | Size | Field |
|---|---|---|
| 0 | 4 | size, `uint32` LE |
| 4 | 2 | FAT date, `uint16` LE |
| 6 | 2 | FAT time, `uint16` LE |
| 8 | 3 | extension, space padded, not terminated |
| 11 | 1 | FAT attribute |
| 12 | n | filename, not terminated |

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/Systems/Ultimate/UltimateDosTargetInfoTests.cs`:

```csharp
using System.Text;
using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class UltimateDosTargetInfoTests : IDisposable
{
    private readonly string _fixture;
    private readonly UltimateFileSystem _fs;
    private readonly UltimateDosTarget _dos;

    private static readonly DateTime KnownStamp = new(2024, 3, 17, 14, 25, 36, DateTimeKind.Local);

    public UltimateDosTargetInfoTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64sim-dosinfo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_fixture, "sub"));
        File.WriteAllBytes(Path.Combine(_fixture, "game.prg"), new byte[321]);
        File.WriteAllText(Path.Combine(_fixture, "notes.txt"), "notes");
        File.WriteAllText(Path.Combine(_fixture, "noext"), "x");
        File.SetLastWriteTime(Path.Combine(_fixture, "game.prg"), KnownStamp);

        _fs = new UltimateFileSystem(_fixture);
        _dos = new UltimateDosTarget(_fs);
    }

    public void Dispose()
    {
        _dos.Dispose();
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    private static byte[] Cmd(byte code, params byte[] rest)
    {
        var bytes = new List<byte> { 0x01, code };
        bytes.AddRange(rest);
        return bytes.ToArray();
    }

    private static byte[] Cmd(byte code, string argument)
    {
        var bytes = new List<byte> { 0x01, code };
        bytes.AddRange(Encoding.ASCII.GetBytes(argument));
        return bytes.ToArray();
    }

    /// <summary>Two NUL-separated names, as RENAME_FILE and COPY_FILE expect.</summary>
    private static byte[] CmdPair(byte code, string first, string second)
    {
        var bytes = new List<byte> { 0x01, code };
        bytes.AddRange(Encoding.ASCII.GetBytes(first));
        bytes.Add(0x00);
        bytes.AddRange(Encoding.ASCII.GetBytes(second));
        return bytes.ToArray();
    }

    private static byte[] OpenCmd(byte attributes, string name)
    {
        var bytes = new List<byte> { 0x01, UltimateDosTarget.CmdOpenFile, attributes };
        bytes.AddRange(Encoding.ASCII.GetBytes(name));
        return bytes.ToArray();
    }

    // ── FILE_STAT ──

    [Fact]
    public void FileStat_ReportsSizeDateTimeExtensionAttributeAndName()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileStat, "game.prg"));

        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
        reply.Data.Should().HaveCount(12 + "game.prg".Length);

        BitConverter.ToUInt32(reply.Data, 0).Should().Be(321);
        BitConverter.ToUInt16(reply.Data, 4).Should().Be(UltimateDosTarget.FatDate(KnownStamp));
        BitConverter.ToUInt16(reply.Data, 6).Should().Be(UltimateDosTarget.FatTime(KnownStamp));
        Encoding.ASCII.GetString(reply.Data, 8, 3).Should().Be("PRG");
        reply.Data[11].Should().Be(UltimateFileSystem.AttributeArchive);
        Encoding.ASCII.GetString(reply.Data, 12, reply.Data.Length - 12).Should().Be("game.prg");
    }

    [Fact]
    public void FileStat_ShortExtension_IsSpacePadded()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileStat, "notes.txt"));
        Encoding.ASCII.GetString(reply.Data, 8, 3).Should().Be("TXT");
    }

    [Fact]
    public void FileStat_NoExtension_IsAllSpaces()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileStat, "noext"));
        Encoding.ASCII.GetString(reply.Data, 8, 3).Should().Be("   ");
    }

    [Fact]
    public void FileStat_Directory_ReportsTheDirectoryAttribute()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileStat, "sub"));

        reply.Status.Should().Be("00,OK");
        reply.Data[11].Should().Be(UltimateFileSystem.AttributeDirectory);
        BitConverter.ToUInt32(reply.Data, 0).Should().Be(0);
    }

    [Fact]
    public void FileStat_Missing_ReportsFileNotFound()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileStat, "nope.txt"));

        reply.Status.Should().Be("82,FILE NOT FOUND");
        reply.Data.Should().BeEmpty();
    }

    [Fact]
    public void FileStat_OutsideTheMount_ReportsFileNotFound()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileStat, "/SdCard/x"))
            .Status.Should().Be("82,FILE NOT FOUND");
    }

    // ── FILE_INFO ──

    [Fact]
    public void FileInfo_DescribesTheOpenFile()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "game.prg"));

        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileInfo));

        reply.Status.Should().Be("00,OK");
        BitConverter.ToUInt32(reply.Data, 0).Should().Be(321);
        Encoding.ASCII.GetString(reply.Data, 12, reply.Data.Length - 12).Should().Be("game.prg");
    }

    [Fact]
    public void FileInfo_WithNoOpenFile_ReportsNoFileOpen()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileInfo));

        reply.Status.Should().Be("85,NO FILE OPEN");
        reply.Data.Should().BeEmpty();
    }

    // ── FAT date and time encoding ──

    [Fact]
    public void FatDate_PacksYearMonthDay()
    {
        var expected = (ushort)(((2024 - 1980) << 9) | (3 << 5) | 17);
        UltimateDosTarget.FatDate(KnownStamp).Should().Be(expected);
    }

    [Fact]
    public void FatTime_PacksHourMinuteAndTwoSecondUnits()
    {
        var expected = (ushort)((14 << 11) | (25 << 5) | (36 / 2));
        UltimateDosTarget.FatTime(KnownStamp).Should().Be(expected);
    }

    [Fact]
    public void FatDate_BeforeTheFatEpoch_ClampsToZero()
    {
        UltimateDosTarget.FatDate(new DateTime(1970, 1, 1)).Should().Be(0);
    }

    // ── DELETE_FILE ──

    [Fact]
    public void DeleteFile_RemovesIt()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdDeleteFile, "notes.txt"))
            .Status.Should().Be("00,OK");

        File.Exists(_fs.ResolveToHostPath("notes.txt")!).Should().BeFalse();
    }

    [Fact]
    public void DeleteFile_Missing_ReportsFileNotFound()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdDeleteFile, "nope.txt"))
            .Status.Should().Be("82,FILE NOT FOUND");
    }

    [Fact]
    public void DeleteFile_EmptyDirectory_Succeeds()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdDeleteFile, "sub"))
            .Status.Should().Be("00,OK");
        Directory.Exists(_fs.ResolveToHostPath("sub")!).Should().BeFalse();
    }

    [Fact]
    public void DeleteFile_OutsideTheMount_IsRejectedAndTouchesNothing()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdDeleteFile, "/SdCard/x"))
            .Status.Should().Be("82,FILE NOT FOUND");
        File.Exists(Path.Combine(_fixture, "notes.txt")).Should().BeTrue();
    }

    // ── RENAME_FILE ──

    [Fact]
    public void RenameFile_MovesTheName()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdRenameFile, "notes.txt", "memo.txt"))
            .Status.Should().Be("00,OK");

        File.Exists(_fs.ResolveToHostPath("notes.txt")!).Should().BeFalse();
        File.ReadAllText(_fs.ResolveToHostPath("memo.txt")!).Should().Be("notes");
    }

    [Fact]
    public void RenameFile_MissingSource_ReportsFileNotFound()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdRenameFile, "nope.txt", "memo.txt"))
            .Status.Should().Be("82,FILE NOT FOUND");
    }

    [Fact]
    public void RenameFile_OntoAnExistingName_ReportsAnError()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdRenameFile, "notes.txt", "game.prg"))
            .Status.Should().Be("87,INTERNAL ERROR");
        File.Exists(_fs.ResolveToHostPath("notes.txt")!).Should().BeTrue();
    }

    [Fact]
    public void RenameFile_MissingSecondName_ReportsAnError()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdRenameFile, "notes.txt"))
            .Status.Should().Be("87,INTERNAL ERROR");
    }

    [Fact]
    public void RenameFile_DestinationOutsideTheMount_IsRejected()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdRenameFile, "notes.txt", "/SdCard/x"))
            .Status.Should().Be("87,INTERNAL ERROR");
        File.Exists(_fs.ResolveToHostPath("notes.txt")!).Should().BeTrue();
    }

    // ── COPY_FILE ──

    [Fact]
    public void CopyFile_DuplicatesTheContent()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdCopyFile, "notes.txt", "copy.txt"))
            .Status.Should().Be("00,OK");

        File.ReadAllText(_fs.ResolveToHostPath("notes.txt")!).Should().Be("notes");
        File.ReadAllText(_fs.ResolveToHostPath("copy.txt")!).Should().Be("notes");
    }

    [Fact]
    public void CopyFile_MissingSource_ReportsFileNotFound()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdCopyFile, "nope.txt", "copy.txt"))
            .Status.Should().Be("82,FILE NOT FOUND");
    }

    [Fact]
    public void CopyFile_OntoAnExistingName_ReportsAnError()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdCopyFile, "notes.txt", "game.prg"))
            .Status.Should().Be("87,INTERNAL ERROR");
        new FileInfo(_fs.ResolveToHostPath("game.prg")!).Length.Should().Be(321);
    }

    // ── OPEN_DIR and READ_DIR ──

    [Fact]
    public void OpenDir_ReportsOkForAPopulatedDirectory()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdOpenDir));

        reply.Status.Should().Be("00,OK");
        reply.Data.Should().BeEmpty();
    }

    [Fact]
    public void OpenDir_EmptyDirectory_ReportsDirectoryEmpty()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "sub"));

        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdOpenDir))
            .Status.Should().Be("01,DIRECTORY EMPTY");
    }

    [Fact]
    public void ReadDir_YieldsOneEntryPerPartDirectoriesFirst()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdOpenDir));

        var names = new List<string>();
        var attributes = new List<byte>();

        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdReadDir));
        var guard = 0;
        while (true)
        {
            if (++guard > 32) throw new InvalidOperationException("directory read never terminated");
            attributes.Add(reply.Data[0]);
            names.Add(Encoding.ASCII.GetString(reply.Data, 1, reply.Data.Length - 1));
            if (reply.LastPart) break;
            reply = _dos.GetMoreData();
        }

        names.Should().Equal("sub", "game.prg", "noext", "notes.txt");
        attributes[0].Should().Be(UltimateFileSystem.AttributeDirectory);
        attributes.Skip(1).Should().AllBeEquivalentTo(UltimateFileSystem.AttributeArchive);
    }

    [Fact]
    public void ReadDir_NonFinalPartsCarryNoStatusFinalPartCarriesOk()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdOpenDir));

        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdReadDir));
        reply.LastPart.Should().BeFalse();
        reply.Status.Should().BeEmpty();

        var guard = 0;
        while (!reply.LastPart)
        {
            if (++guard > 32) throw new InvalidOperationException("directory read never terminated");
            reply = _dos.GetMoreData();
        }
        reply.Status.Should().Be("00,OK");
    }

    [Fact]
    public void ReadDir_WithoutOpenDir_ReportsCannotReadDirectory()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdReadDir))
            .Status.Should().Be("86,CAN'T READ DIRECTORY");
    }

    [Fact]
    public void ReadDir_AfterCompletion_LeavesDataMode()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdOpenDir));
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdReadDir));
        var guard = 0;
        while (!reply.LastPart)
        {
            if (++guard > 32) throw new InvalidOperationException("directory read never terminated");
            reply = _dos.GetMoreData();
        }

        _dos.GetMoreData().Status.Should().Be("81,NOT IN DATA MODE");
    }

    [Fact]
    public void ReadDir_SingleEntryDirectory_IsImmediatelyTheLastPart()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "sub"));
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCreateDir, "only"));
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdOpenDir)).Status.Should().Be("00,OK");

        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdReadDir));

        reply.LastPart.Should().BeTrue();
        reply.Status.Should().Be("00,OK");
        Encoding.ASCII.GetString(reply.Data, 1, reply.Data.Length - 1).Should().Be("only");
    }

    // ── Commands deferred to later milestones ──

    [Theory]
    [InlineData(UltimateDosTarget.CmdCopyUiPath)]
    [InlineData(UltimateDosTarget.CmdCopyHomePath)]
    [InlineData(UltimateDosTarget.CmdLoadReu)]
    [InlineData(UltimateDosTarget.CmdSaveReu)]
    [InlineData(UltimateDosTarget.CmdMountDisk)]
    [InlineData(UltimateDosTarget.CmdUnmountDisk)]
    [InlineData(UltimateDosTarget.CmdSwapDisk)]
    [InlineData(UltimateDosTarget.CmdGetTime)]
    [InlineData(UltimateDosTarget.CmdSetTime)]
    public void DeferredCommands_ReportNotImplementedRatherThanUnknown(byte code)
    {
        var reply = _dos.ParseCommand(Cmd(code));

        reply.Status.Should().Be("99,FUNCTION NOT IMPLEMENTED",
            "a recognised-but-deferred command must not look like a typo");
        reply.Data.Should().BeEmpty();
        reply.LastPart.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UltimateDosTargetInfoTests"`
Expected: FAIL — compile error, `UltimateDosTarget.FatDate` does not exist; the
remaining tests would fail with `"21,UNKNOWN COMMAND"`.

- [ ] **Step 3: Add the new command cases**

In `ParseCommand`, add above the `_ =>` default:

```csharp
            CmdFileInfo   => OpenFileInfo(),
            CmdFileStat   => FileStat(ReadString(command, 2)),
            CmdDeleteFile => Delete(ReadString(command, 2)),
            CmdRenameFile => RenameOrCopy(command, copy: false),
            CmdCopyFile   => RenameOrCopy(command, copy: true),
            CmdOpenDir    => OpenDirectory(),
            CmdReadDir    => BeginReadDirectory(),

            // Recognised commands deferred to a later milestone. Answering
            // "not implemented" rather than "unknown command" keeps the gap
            // visible instead of looking like a malformed request.
            CmdCopyUiPath or CmdCopyHomePath or CmdLoadReu or CmdSaveReu or
            CmdMountDisk or CmdUnmountDisk or CmdSwapDisk or CmdGetTime or CmdSetTime
                => UciReply.Empty(StatusNotImplemented),
```

- [ ] **Step 4: Add the implementation methods**

Insert after `ReadNextChunk`:

```csharp
    /// <summary>FAT packed date: year since 1980 in bits 15-9, month 8-5, day 4-0.</summary>
    internal static ushort FatDate(DateTime when)
    {
        if (when.Year < 1980) return 0;
        return (ushort)(((when.Year - 1980) << 9) | (when.Month << 5) | when.Day);
    }

    /// <summary>FAT packed time: hour in bits 15-11, minute 10-5, two-second units 4-0.</summary>
    internal static ushort FatTime(DateTime when)
        => (ushort)((when.Hour << 11) | (when.Minute << 5) | (when.Second / 2));

    /// <summary>
    /// Build the t_dos_info reply: size, FAT date and time, space-padded three
    /// character extension, attribute, then the name with no terminator.
    /// </summary>
    private static byte[] BuildInfo(string name, long size, byte attributes, DateTime modified)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        var data = new byte[12 + nameBytes.Length];

        BitConverter.TryWriteBytes(data.AsSpan(0, 4), (uint)Math.Min(size, uint.MaxValue));
        BitConverter.TryWriteBytes(data.AsSpan(4, 2), FatDate(modified));
        BitConverter.TryWriteBytes(data.AsSpan(6, 2), FatTime(modified));

        data[8] = data[9] = data[10] = (byte)' ';
        var extension = Path.GetExtension(name);
        if (extension.StartsWith('.')) extension = extension[1..];
        extension = extension.ToUpperInvariant();
        for (var i = 0; i < Math.Min(3, extension.Length); i++)
            data[8 + i] = (byte)extension[i];

        data[11] = attributes;
        Array.Copy(nameBytes, 0, data, 12, nameBytes.Length);
        return data;
    }

    private UciReply FileStat(string name)
    {
        var host = _fileSystem.ResolveToHostPath(name);
        if (host == null)
            return UciReply.Empty(StatusFileNotFound);

        var leaf = Path.GetFileName(host);

        if (Directory.Exists(host))
        {
            var info = new DirectoryInfo(host);
            return UciReply.Ok(BuildInfo(
                leaf, 0, UltimateFileSystem.AttributeDirectory, info.LastWriteTime));
        }

        if (File.Exists(host))
        {
            var info = new FileInfo(host);
            return UciReply.Ok(BuildInfo(
                leaf, info.Length, UltimateFileSystem.AttributeArchive, info.LastWriteTime));
        }

        return UciReply.Empty(StatusFileNotFound);
    }

    // Named OpenFileInfo, not FileInfo: a method called FileInfo would shadow the
    // System.IO.FileInfo type inside this class and break every use of it below.
    private UciReply OpenFileInfo()
    {
        if (_file == null)
            return UciReply.Empty(StatusNoFileOpen);

        try
        {
            var info = new FileInfo(_file.Name);
            return UciReply.Ok(BuildInfo(
                info.Name, info.Length, UltimateFileSystem.AttributeArchive, info.LastWriteTime));
        }
        catch (Exception ex)
        {
            Logger.Warn($"DOS: could not stat the open file: {ex.Message}");
            return UciReply.Empty(StatusFileNotFound);
        }
    }

    private UciReply Delete(string name)
    {
        var host = _fileSystem.ResolveToHostPath(name);
        if (host == null)
            return UciReply.Empty(StatusFileNotFound);

        try
        {
            if (File.Exists(host))
            {
                File.Delete(host);
                return UciReply.Empty(UciConstants.StatusOk);
            }

            if (Directory.Exists(host))
            {
                // Non-recursive, matching f_unlink: a populated directory fails.
                Directory.Delete(host, recursive: false);
                return UciReply.Empty(UciConstants.StatusOk);
            }

            return UciReply.Empty(StatusFileNotFound);
        }
        catch (Exception ex)
        {
            Logger.Warn($"DOS: could not delete '{name}': {ex.Message}");
            return UciReply.Empty(StatusInternalError);
        }
    }

    /// <summary>
    /// RENAME_FILE and COPY_FILE share a wire format: the source name at byte 2,
    /// a NUL, then the destination name.
    /// </summary>
    private UciReply RenameOrCopy(byte[] command, bool copy)
    {
        var source = ReadString(command, 2);
        var separator = 2 + source.Length;

        if (separator >= command.Length)
        {
            Logger.Warn("DOS: rename/copy command carries no destination name");
            return UciReply.Empty(StatusInternalError);
        }

        var destination = ReadString(command, separator + 1);
        if (destination.Length == 0)
            return UciReply.Empty(StatusInternalError);

        var sourceHost = _fileSystem.ResolveToHostPath(source);
        if (sourceHost == null || (!File.Exists(sourceHost) && !Directory.Exists(sourceHost)))
            return UciReply.Empty(StatusFileNotFound);

        var destinationHost = _fileSystem.ResolveToHostPath(destination);
        if (destinationHost == null)
        {
            Logger.Warn($"DOS: rename/copy destination '{destination}' is outside the mount");
            return UciReply.Empty(StatusInternalError);
        }

        if (File.Exists(destinationHost) || Directory.Exists(destinationHost))
            return UciReply.Empty(StatusInternalError);

        try
        {
            if (copy) File.Copy(sourceHost, destinationHost);
            else if (Directory.Exists(sourceHost)) Directory.Move(sourceHost, destinationHost);
            else File.Move(sourceHost, destinationHost);

            return UciReply.Empty(UciConstants.StatusOk);
        }
        catch (Exception ex)
        {
            Logger.Warn($"DOS: {(copy ? "copy" : "rename")} of '{source}' failed: {ex.Message}");
            return UciReply.Empty(StatusInternalError);
        }
    }

    private UciReply OpenDirectory()
    {
        _directory = _fileSystem.ListCurrentDirectory();
        _directoryIndex = 0;

        return UciReply.Empty(_directory.Count == 0
            ? StatusDirectoryEmpty
            : UciConstants.StatusOk);
    }

    private UciReply BeginReadDirectory()
    {
        if (_directory.Count == 0)
        {
            Logger.Debug("DOS: READ_DIR without a preceding OPEN_DIR");
            return UciReply.Empty(StatusCannotReadDir);
        }

        _directoryIndex = 0;
        _state = DosState.InDirectory;
        return GetMoreData();
    }

    private UciReply NextDirectoryEntry()
    {
        if (_directoryIndex >= _directory.Count)
        {
            _state = DosState.Idle;
            return UciReply.Empty(StatusInternalError);
        }

        var entry = _directory[_directoryIndex++];
        var nameBytes = Encoding.ASCII.GetBytes(entry.Name);

        var data = new byte[1 + nameBytes.Length];
        data[0] = entry.Attributes;
        Array.Copy(nameBytes, 0, data, 1, nameBytes.Length);

        var lastPart = _directoryIndex >= _directory.Count;
        if (lastPart)
            _state = DosState.Idle;

        return new UciReply(data, lastPart ? UciConstants.StatusOk : UciConstants.StatusEmpty, lastPart);
    }
```

- [ ] **Step 5: Add the `InDirectory` branch to `GetMoreData`**

Insert before the `default:` label:

```csharp
            case DosState.InDirectory:
                return NextDirectoryEntry();
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~UltimateDosTargetInfoTests"`
Expected: PASS — 37 passed (the deferred-command `[Theory]` contributes 9).

- [ ] **Step 7: Run every DOS test together**

Run: `dotnet test --filter "FullyQualifiedName~UltimateDosTarget"`
Expected: PASS — 82 passed across the three DOS test files.

- [ ] **Step 8: Commit**

```bash
git add sim6502/Systems/Ultimate/UltimateDosTarget.cs \
        sim6502tests/Systems/Ultimate/UltimateDosTargetInfoTests.cs
git commit -m "feat(ultimate): Ultimate DOS stat, info, delete, rename, copy, listing

FILE_INFO and FILE_STAT emit the t_dos_info struct byte for byte, including
FAT-packed date and time and the space-padded three character extension.
READ_DIR walks the listing one entry per DATA_MORE part. Commands recognised
but deferred to later milestones answer '99,FUNCTION NOT IMPLEMENTED' rather
than looking like typos, and a test pins that."
```

---

## Task 10: `ControlTarget`

**Files:**
- Create: `sim6502/Systems/Ultimate/ControlTarget.cs`
- Test: `sim6502tests/Systems/Ultimate/ControlTargetTests.cs`

**Interfaces:**
- Consumes: `ICommandTarget`, `UciReply`, `UciConstants` (Task 3);
  `UltimateDosTarget.ResetState()` (Task 7).
- Produces:
  - `sealed class ControlTarget : ICommandTarget`
    - `ControlTarget(IEnumerable<UltimateDosTarget> dosTargets, string modelName = "Ultimate 64", string version = "CONTROL TARGET V1.1")`
    - command constants `CmdIdentify = 0x01`, `CmdFinishCapture = 0x03`,
      `CmdFreeze = 0x05`, `CmdReboot = 0x06`, `CmdLoadReu = 0x08`,
      `CmdSaveReu = 0x09`, `CmdSaveMemory = 0x0F`, `CmdGetHwInfo = 0x28`
    - status constants `StatusReuNotEnabled = "84,REU NOT ENABLED"`,
      `StatusNotImplemented = "99,FUNCTION NOT IMPLEMENTED"`
    - `int RebootCount { get; }` so tests and the backend can observe reboots

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/Systems/Ultimate/ControlTargetTests.cs`:

```csharp
using System.Text;
using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class ControlTargetTests : IDisposable
{
    private readonly string _fixture;
    private readonly UltimateDosTarget _dos;
    private readonly ControlTarget _control;

    public ControlTargetTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64sim-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixture);
        File.WriteAllText(Path.Combine(_fixture, "data.bin"), "payload");

        _dos = new UltimateDosTarget(new UltimateFileSystem(_fixture));
        _control = new ControlTarget(new[] { _dos });
    }

    public void Dispose()
    {
        _dos.Dispose();
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    private static byte[] Cmd(byte code, params byte[] rest)
    {
        var bytes = new List<byte> { 0x04, code };
        bytes.AddRange(rest);
        return bytes.ToArray();
    }

    private static string Text(UciReply reply) => Encoding.ASCII.GetString(reply.Data);

    [Fact]
    public void Identify_ReturnsTheVersionString()
    {
        var reply = _control.ParseCommand(Cmd(ControlTarget.CmdIdentify));

        Text(reply).Should().Be("CONTROL TARGET V1.1");
        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void GetHwInfo_ReturnsTheModelName()
    {
        var reply = _control.ParseCommand(Cmd(ControlTarget.CmdGetHwInfo));

        Text(reply).Should().Be("Ultimate 64");
        reply.Status.Should().Be("00,OK");
    }

    [Fact]
    public void GetHwInfo_HonoursAConfiguredModelName()
    {
        var control = new ControlTarget(new[] { _dos }, modelName: "Ultimate-II+");
        Text(control.ParseCommand(Cmd(ControlTarget.CmdGetHwInfo))).Should().Be("Ultimate-II+");
    }

    [Fact]
    public void Reboot_ReportsOkAndCountsTheReboot()
    {
        var reply = _control.ParseCommand(Cmd(ControlTarget.CmdReboot));

        reply.Status.Should().Be("00,OK");
        reply.Data.Should().BeEmpty();
        _control.RebootCount.Should().Be(1);
    }

    [Fact]
    public void Reboot_ClearsDosTargetState()
    {
        var open = new List<byte> { 0x01, UltimateDosTarget.CmdOpenFile, UltimateDosTarget.FileAttributeRead };
        open.AddRange(Encoding.ASCII.GetBytes("data.bin"));
        _dos.ParseCommand(open.ToArray()).Status.Should().Be("00,OK");

        _control.ParseCommand(Cmd(ControlTarget.CmdReboot));

        _dos.ParseCommand(new byte[] { 0x01, UltimateDosTarget.CmdCloseFile })
            .Status.Should().Be("84,NO FILE TO CLOSE", "reboot must close any open file");
    }

    [Fact]
    public void Reboot_ResetsEveryRegisteredDosTarget()
    {
        var second = new UltimateDosTarget(new UltimateFileSystem(_fixture));
        try
        {
            var control = new ControlTarget(new[] { _dos, second });

            foreach (var target in new[] { _dos, second })
            {
                var open = new List<byte> { 0x01, UltimateDosTarget.CmdOpenFile, UltimateDosTarget.FileAttributeRead };
                open.AddRange(Encoding.ASCII.GetBytes("data.bin"));
                target.ParseCommand(open.ToArray()).Status.Should().Be("00,OK");
            }

            control.ParseCommand(Cmd(ControlTarget.CmdReboot));

            foreach (var target in new[] { _dos, second })
                target.ParseCommand(new byte[] { 0x01, UltimateDosTarget.CmdCloseFile })
                      .Status.Should().Be("84,NO FILE TO CLOSE");
        }
        finally
        {
            second.Dispose();
        }
    }

    [Theory]
    [InlineData(ControlTarget.CmdLoadReu)]
    [InlineData(ControlTarget.CmdSaveReu)]
    public void ReuCommands_ReportReuNotEnabled(byte code)
    {
        var reply = _control.ParseCommand(Cmd(code));

        reply.Status.Should().Be("84,REU NOT ENABLED",
            "the REU arrives in a later milestone and must say so plainly");
        reply.Data.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ControlTarget.CmdFinishCapture)]
    [InlineData(ControlTarget.CmdFreeze)]
    [InlineData(ControlTarget.CmdSaveMemory)]
    public void DeferredCommands_ReportNotImplemented(byte code)
    {
        _control.ParseCommand(Cmd(code)).Status.Should().Be("99,FUNCTION NOT IMPLEMENTED");
    }

    [Fact]
    public void UnknownCommand_IsRejected()
    {
        var reply = _control.ParseCommand(Cmd(0x7B));

        reply.Status.Should().Be("21,UNKNOWN COMMAND");
        reply.Data.Should().BeEmpty();
    }

    [Fact]
    public void ShortCommand_IsRejectedRatherThanThrowing()
    {
        _control.ParseCommand(new byte[] { 0x04 }).Status.Should().Be("21,UNKNOWN COMMAND");
    }

    [Fact]
    public void GetMoreData_IsAlwaysAFinalEmptyReply()
    {
        var reply = _control.GetMoreData();

        reply.Data.Should().BeEmpty();
        reply.LastPart.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ControlTargetTests"`
Expected: FAIL — compile error, `ControlTarget` does not exist.

- [ ] **Step 3: Implement `ControlTarget.cs`**

```csharp
// Ported from GideonZ/1541ultimate (GPL-3.0): the control target command set
// documented in GideonZ/1541u-documentation uci/control_target.rst and
// implemented across software/ (c64.cc, command handlers).
// Original author: Gideon Zweijtzer. See NOTICE.

using System.Text;
using NLog;

namespace sim6502.Systems.Ultimate;

/// <summary>
/// The UCI control target, served at target $04. Only the commands that are
/// meaningful without an REU or a real machine are implemented; the rest report
/// their absence explicitly rather than looking like unrecognised requests.
/// </summary>
public sealed class ControlTarget : ICommandTarget
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    public const byte CmdIdentify      = 0x01;
    public const byte CmdFinishCapture = 0x03;
    public const byte CmdFreeze        = 0x05;
    public const byte CmdReboot        = 0x06;
    public const byte CmdLoadReu       = 0x08;
    public const byte CmdSaveReu       = 0x09;
    public const byte CmdSaveMemory    = 0x0F;
    public const byte CmdGetHwInfo     = 0x28;

    public const string StatusReuNotEnabled = "84,REU NOT ENABLED";
    public const string StatusNotImplemented = "99,FUNCTION NOT IMPLEMENTED";

    private readonly UltimateDosTarget[] _dosTargets;
    private readonly string _modelName;
    private readonly string _version;

    public ControlTarget(
        IEnumerable<UltimateDosTarget> dosTargets,
        string modelName = "Ultimate 64",
        string version = "CONTROL TARGET V1.1")
    {
        _dosTargets = (dosTargets ?? throw new ArgumentNullException(nameof(dosTargets))).ToArray();
        _modelName = modelName;
        _version = version;
    }

    /// <summary>How many REBOOT commands have been handled.</summary>
    public int RebootCount { get; private set; }

    public UciReply ParseCommand(byte[] command)
    {
        if (command.Length < 2)
        {
            Logger.Warn("Control: command shorter than two bytes");
            return UciReply.Empty(UciConstants.StatusUnknownCommand);
        }

        return command[1] switch
        {
            CmdIdentify  => UciReply.Ok(Encoding.ASCII.GetBytes(_version)),
            CmdGetHwInfo => UciReply.Ok(Encoding.ASCII.GetBytes(_modelName)),
            CmdReboot    => Reboot(),

            // The REU is a later milestone. Answering with the documented
            // "not enabled" status is what real hardware reports when no REU is
            // configured, so client code takes the same path it would there.
            CmdLoadReu or CmdSaveReu => UciReply.Empty(StatusReuNotEnabled),

            CmdFinishCapture or CmdFreeze or CmdSaveMemory
                => UciReply.Empty(StatusNotImplemented),

            _ => UciReply.Empty(UciConstants.StatusUnknownCommand)
        };
    }

    public UciReply GetMoreData() => UciReply.Empty(UciConstants.StatusOk);

    public void Abort(int bytesConsumed) { }

    private UciReply Reboot()
    {
        RebootCount++;
        Logger.Info($"Control: reboot ({RebootCount})");

        foreach (var dos in _dosTargets)
            dos.ResetState();

        return UciReply.Empty(UciConstants.StatusOk);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ControlTargetTests"`
Expected: PASS — 14 passed (the two `[Theory]` cases contribute 5).

- [ ] **Step 5: Commit**

```bash
git add sim6502/Systems/Ultimate/ControlTarget.cs \
        sim6502tests/Systems/Ultimate/ControlTargetTests.cs
git commit -m "feat(ultimate): add the UCI control target at \$04

IDENTIFY, GET_HWINFO, and REBOOT (which clears every DOS target's state).
REU commands answer '84,REU NOT ENABLED' — the same status real hardware gives
with no REU configured — so client code takes the hardware path until the REU
milestone lands."
```

---

## Task 11: `U64SimBackend`, config, factory, CLI

**Files:**
- Create: `sim6502/Backend/U64SimBackendConfig.cs`
- Create: `sim6502/Backend/U64SimBackend.cs`
- Modify: `sim6502/Backend/BackendFactory.cs:11-53`
- Modify: `sim6502/Sim6502CLI.cs:92-127` and `:214-228`
- Test: `sim6502tests/Backend/U64SimBackendTests.cs`, and add cases to
  `sim6502tests/Backend/BackendFactoryTests.cs`

**Interfaces:**
- Consumes: `UciRegisters`, `UltimateFileSystem`, `UltimateDosTarget`, `ControlTarget`
  (Tasks 4-10); `SimulatorBackend`, `IExecutionBackend` (existing);
  `C64MemoryMap.RegisterIoHandler` (Task 2).
- Produces:
  - `class U64SimBackendConfig` — `string FsRoot = ""`, `int UciLatencyCycles = 64`,
    `string DosVersion = "ULTIMATE-II DOS V1.2"`, `string ModelName = "Ultimate 64"`
  - `class U64SimBackend : IExecutionBackend`
    - `U64SimBackend(U64SimBackendConfig config, IMemoryMap memoryMap)`
    - `(string Status, byte[] Data) IssueUciCommand(byte[] command)`
    - `UciRegisters Uci { get; }` (internal, for tests)
  - `BackendFactory.Create(..., U64SimBackendConfig? u64SimConfig = null)` handling
    `"u64sim"`

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/Backend/U64SimBackendTests.cs`:

```csharp
using System.Text;
using FluentAssertions;
using sim6502.Backend;
using sim6502.Proc;
using sim6502.Systems;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Backend;

public class U64SimBackendTests : IDisposable
{
    private readonly string _fixture;

    public U64SimBackendTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64sim-backend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_fixture, "data"));
        File.WriteAllText(Path.Combine(_fixture, "data", "hi.txt"), "hi");
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    private U64SimBackend NewBackend(int latency = 0)
    {
        var config = new U64SimBackendConfig { FsRoot = _fixture, UciLatencyCycles = latency };
        return new U64SimBackend(config, new C64MemoryMap());
    }

    [Fact]
    public void Backend_DelegatesMemoryOperationsToTheSimulator()
    {
        using var backend = NewBackend();

        backend.WriteByte(0xC000, 0x42);
        backend.ReadByte(0xC000).Should().Be(0x42);

        backend.WriteWord(0xC010, 0xBEEF);
        backend.ReadWord(0xC010).Should().Be(0xBEEF);
    }

    [Fact]
    public void Backend_DelegatesRegistersAndFlags()
    {
        using var backend = NewBackend();

        backend.SetRegister("a", 0x7F);
        backend.GetRegister("a").Should().Be(0x7F);

        backend.SetFlag("c", true);
        backend.GetFlag("c").Should().BeTrue();
    }

    [Fact]
    public void UciRegisters_AreVisibleToTheCpuAtDf1d()
    {
        using var backend = NewBackend();
        backend.ReadByte(UciConstants.CommandAddress).Should().Be(0xC9);
    }

    [Fact]
    public void IssueUciCommand_Identify_ReachesTheDosTarget()
    {
        using var backend = NewBackend();

        var (status, data) = backend.IssueUciCommand(
            new byte[] { 0x01, UltimateDosTarget.CmdIdentify });

        status.Should().Be("00,OK");
        Encoding.ASCII.GetString(data).Should().Be("ULTIMATE-II DOS V1.2");
    }

    [Fact]
    public void IssueUciCommand_ReachesTheSecondDosTargetIndependently()
    {
        using var backend = NewBackend();

        backend.IssueUciCommand(BuildChangeDir(0x01, "data")).Status.Should().Be("00,OK");

        var first  = backend.IssueUciCommand(new byte[] { 0x01, UltimateDosTarget.CmdGetPath });
        var second = backend.IssueUciCommand(new byte[] { 0x02, UltimateDosTarget.CmdGetPath });

        Encoding.ASCII.GetString(first.Data).Should().Be("/Usb0/data");
        Encoding.ASCII.GetString(second.Data).Should().Be("/Usb0");
    }

    [Fact]
    public void IssueUciCommand_ReachesTheControlTarget()
    {
        using var backend = NewBackend();

        var (status, data) = backend.IssueUciCommand(
            new byte[] { 0x04, ControlTarget.CmdIdentify });

        status.Should().Be("00,OK");
        Encoding.ASCII.GetString(data).Should().Be("CONTROL TARGET V1.1");
    }

    [Fact]
    public void IssueUciCommand_ReadsAFileAcrossContinuationParts()
    {
        using var backend = NewBackend();

        backend.IssueUciCommand(BuildChangeDir(0x01, "data")).Status.Should().Be("00,OK");

        var open = new List<byte> { 0x01, UltimateDosTarget.CmdOpenFile, UltimateDosTarget.FileAttributeRead };
        open.AddRange(Encoding.ASCII.GetBytes("hi.txt"));
        backend.IssueUciCommand(open.ToArray()).Status.Should().Be("00,OK");

        var read = backend.IssueUciCommand(
            new byte[] { 0x01, UltimateDosTarget.CmdReadData, 0x02, 0x00 });

        Encoding.ASCII.GetString(read.Data).Should().Be("hi");
    }

    [Fact]
    public void Config_OverridesTheDosVersion()
    {
        var config = new U64SimBackendConfig
        {
            FsRoot = _fixture,
            UciLatencyCycles = 0,
            DosVersion = "ULTIMATE-II DOS V1.1"
        };
        using var backend = new U64SimBackend(config, new C64MemoryMap());

        var (_, data) = backend.IssueUciCommand(new byte[] { 0x01, UltimateDosTarget.CmdIdentify });
        Encoding.ASCII.GetString(data).Should().Be("ULTIMATE-II DOS V1.1");
    }

    [Fact]
    public void Config_OverridesTheModelName()
    {
        var config = new U64SimBackendConfig
        {
            FsRoot = _fixture,
            UciLatencyCycles = 0,
            ModelName = "Ultimate-II+"
        };
        using var backend = new U64SimBackend(config, new C64MemoryMap());

        var (_, data) = backend.IssueUciCommand(new byte[] { 0x04, ControlTarget.CmdGetHwInfo });
        Encoding.ASCII.GetString(data).Should().Be("Ultimate-II+");
    }

    [Fact]
    public void Constructor_MissingFsRoot_Throws()
    {
        var config = new U64SimBackendConfig { FsRoot = Path.Combine(_fixture, "gone") };
        var act = () => new U64SimBackend(config, new C64MemoryMap());
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Constructor_EmptyFsRoot_ThrowsWithAHelpfulMessage()
    {
        var act = () => new U64SimBackend(new U64SimBackendConfig(), new C64MemoryMap());
        act.Should().Throw<ArgumentException>()
           .WithMessage("*u64sim-fs-root*");
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var backend = NewBackend();
        backend.Dispose();

        var act = () => backend.Dispose();
        act.Should().NotThrow("Dispose runs twice when a suite ends after an error");
    }

    [Fact]
    public void Reset_RebootsTheUltimateSoOpenFilesAreClosed()
    {
        using var backend = NewBackend();

        var open = new List<byte> { 0x01, UltimateDosTarget.CmdOpenFile, UltimateDosTarget.FileAttributeRead };
        open.AddRange(Encoding.ASCII.GetBytes("data/hi.txt"));
        backend.IssueUciCommand(open.ToArray()).Status.Should().Be("00,OK");

        backend.Reset();

        backend.IssueUciCommand(new byte[] { 0x01, UltimateDosTarget.CmdCloseFile })
               .Status.Should().Be("84,NO FILE TO CLOSE");
    }

    [Fact]
    public void DefaultLatency_IsNonZeroSoBusyWaitLoopsAreExercised()
    {
        new U64SimBackendConfig().UciLatencyCycles.Should().BeGreaterThan(0,
            "answering instantly would let a client with no busy-wait loop pass here " +
            "and fail on hardware");
    }

    private static byte[] BuildChangeDir(byte target, string path)
    {
        var bytes = new List<byte> { target, UltimateDosTarget.CmdChangeDir };
        bytes.AddRange(Encoding.ASCII.GetBytes(path));
        return bytes.ToArray();
    }
}
```

Add to `sim6502tests/Backend/BackendFactoryTests.cs`:

```csharp
    [Fact]
    public void Create_U64Sim_ReturnsU64SimBackend()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "u64sim-factory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var (memMap, procType) = MemoryMapFactory.CreateForSystem(SystemType.C64);
            var config = new U64SimBackendConfig { FsRoot = fixture, UciLatencyCycles = 0 };

            var backend = BackendFactory.Create("u64sim", procType, memMap, u64SimConfig: config);

            backend.Should().BeOfType<U64SimBackend>();
            backend.Dispose();
        }
        finally
        {
            Directory.Delete(fixture, recursive: true);
        }
    }

    [Fact]
    public void Create_U64Sim_WithoutC64MemoryMap_ThrowsNamingSystemC64()
    {
        var (memMap, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6510);

        var act = () => BackendFactory.Create("u64sim", ProcessorType.MOS6510, memMap);

        act.Should().Throw<ArgumentException>().WithMessage("*system(c64)*");
    }

    [Fact]
    public void Create_UnknownBackend_ListsU64SimAsAnOption()
    {
        var (memMap, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6502);

        var act = () => BackendFactory.Create("nonsense", ProcessorType.MOS6502, memMap);

        act.Should().Throw<ArgumentException>().WithMessage("*u64sim*");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~U64SimBackendTests|FullyQualifiedName~BackendFactoryTests"`
Expected: FAIL — compile error, `U64SimBackend` and `U64SimBackendConfig` do not exist.

- [ ] **Step 3: Create `U64SimBackendConfig.cs`**

```csharp
namespace sim6502.Backend;

/// <summary>Configuration for the simulated Ultimate 64 backend.</summary>
public class U64SimBackendConfig
{
    /// <summary>
    /// Host directory exposed to the C64 as the Ultimate's /Usb0 mount. Required.
    /// The tree is copied to a temporary location, so the fixture is never mutated.
    /// </summary>
    public string FsRoot { get; set; } = "";

    /// <summary>
    /// CPU cycles the UCI holds the Busy state before a response becomes readable.
    ///
    /// Deliberately non-zero. The real UCI is asynchronous and a client must poll
    /// $DF1C while the state is Busy; if the simulator answered instantly, a client
    /// with a broken or missing busy-wait loop would pass here and fail on
    /// hardware. Set to 0 only when a test is specifically not about timing.
    /// </summary>
    public int UciLatencyCycles { get; set; } = 64;

    /// <summary>String the DOS targets return for IDENTIFY.</summary>
    public string DosVersion { get; set; } = "ULTIMATE-II DOS V1.2";

    /// <summary>String the control target returns for GET_HWINFO.</summary>
    public string ModelName { get; set; } = "Ultimate 64";
}
```

- [ ] **Step 4: Create `U64SimBackend.cs`**

```csharp
using NLog;
using sim6502.Proc;
using sim6502.Systems;
using sim6502.Systems.Ultimate;

namespace sim6502.Backend;

/// <summary>
/// A simulated Ultimate 64: sim6502's own 6510 core with the Ultimate Command
/// Interface mapped at $DF1B-$DF1F, two Ultimate DOS targets, and a control
/// target. Every <see cref="IExecutionBackend"/> member delegates to an inner
/// <see cref="SimulatorBackend"/>; the Ultimate behaviour is additive.
/// </summary>
public class U64SimBackend : IExecutionBackend
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    private readonly SimulatorBackend _sim;
    private readonly UltimateFileSystem _dosFileSystemOne;
    private readonly UltimateFileSystem _dosFileSystemTwo;
    private readonly UltimateDosTarget _dosOne;
    private readonly UltimateDosTarget _dosTwo;
    private readonly ControlTarget _control;

    public U64SimBackend(U64SimBackendConfig config, IMemoryMap memoryMap)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(memoryMap);

        if (string.IsNullOrWhiteSpace(config.FsRoot))
            throw new ArgumentException(
                "The u64sim backend needs a filesystem root. Set --u64sim-fs-root, " +
                "or ultimate(fs_root = \"...\") in your suite file.",
                nameof(config));

        _sim = new SimulatorBackend(ProcessorType.MOS6510, memoryMap);

        // Targets $01 and $02 keep independent state, so each gets its own view.
        _dosFileSystemOne = new UltimateFileSystem(config.FsRoot);
        _dosFileSystemTwo = new UltimateFileSystem(config.FsRoot);
        _dosOne = new UltimateDosTarget(_dosFileSystemOne, config.DosVersion);
        _dosTwo = new UltimateDosTarget(_dosFileSystemTwo, config.DosVersion);
        _control = new ControlTarget(new[] { _dosOne, _dosTwo }, config.ModelName);

        Uci = new UciRegisters(config.UciLatencyCycles)
        {
            // Busy is held relative to the processor's own cycle count, so a
            // polling loop in 6502 code really does advance it.
            CycleCounter = () => _sim.Processor.CycleCount,
            ServiceEnabled = true
        };

        Uci.RegisterTarget(1, _dosOne);
        Uci.RegisterTarget(2, _dosTwo);
        Uci.RegisterTarget(4, _control);

        memoryMap.RegisterIoHandler(UciConstants.BusIdAddress, UciConstants.StatusAddress, Uci);

        Logger.Info($"u64sim ready: /Usb0 -> '{config.FsRoot}', " +
                    $"UCI latency {config.UciLatencyCycles} cycles");
    }

    /// <summary>The UCI register block, exposed for tests and the DSL.</summary>
    internal UciRegisters Uci { get; }

    /// <summary>
    /// Run a UCI command from the host rather than from 6502 code, walking every
    /// continuation part. Backs the DSL's uci() function.
    /// </summary>
    public (string Status, byte[] Data) IssueUciCommand(byte[] command)
        => Uci.IssueHostCommand(command);

    public void LoadBinary(byte[] data, int address) => _sim.LoadBinary(data, address);
    public void WriteByte(int address, byte value) => _sim.WriteByte(address, value);
    public void WriteWord(int address, int value) => _sim.WriteWord(address, value);
    public void WriteMemoryValue(int address, int value) => _sim.WriteMemoryValue(address, value);
    public byte ReadByte(int address) => _sim.ReadByte(address);
    public int ReadWord(int address) => _sim.ReadWord(address);

    public int GetRegister(string name) => _sim.GetRegister(name);
    public void SetRegister(string name, int value) => _sim.SetRegister(name, value);
    public bool GetFlag(string name) => _sim.GetFlag(name);
    public void SetFlag(string name, bool value) => _sim.SetFlag(name, value);

    public ExecutionResult ExecuteJsr(int address, int stopOnAddress, bool stopOnRts, bool failOnBrk)
        => _sim.ExecuteJsr(address, stopOnAddress, stopOnRts, failOnBrk);

    public int GetCycles() => _sim.GetCycles();
    public void ResetCycleCount() => _sim.ResetCycleCount();

    public void LoadSymbols(string path) => _sim.LoadSymbols(path);
    public void SaveSnapshot(string name) => _sim.SaveSnapshot(name);
    public void RestoreSnapshot(string name) => _sim.RestoreSnapshot(name);

    public void Reset()
    {
        _sim.Reset();
        _control.ParseCommand(new byte[] { 0x04, ControlTarget.CmdReboot });
    }

    public void SetWarpMode(bool enabled) => _sim.SetWarpMode(enabled);

    public bool TraceEnabled
    {
        get => _sim.TraceEnabled;
        set => _sim.TraceEnabled = value;
    }

    public void ClearTraceBuffer() => _sim.ClearTraceBuffer();
    public List<string> GetTraceBuffer() => _sim.GetTraceBuffer();

    public void Dispose()
    {
        // Each DOS target owns the filesystem it was handed and disposes it.
        _dosOne.Dispose();
        _dosTwo.Dispose();
        _sim.Dispose();
    }
}
```

- [ ] **Step 5: Add the factory case**

In `sim6502/Backend/BackendFactory.cs`, add the parameter and the case. The
signature becomes:

```csharp
    public static IExecutionBackend Create(
        string backendType,
        ProcessorType processorType,
        IMemoryMap memoryMap,
        ViceBackendConfig? viceConfig = null,
        NovaVmBackendConfig? novaVmConfig = null,
        U64SimBackendConfig? u64SimConfig = null)
```

Add before the `default:` label:

```csharp
            case "u64sim":
                u64SimConfig ??= new U64SimBackendConfig();

                // The UCI lives in the cartridge I/O range, which only the C64 map
                // models. Fail with the fix rather than a null-reference later.
                if (memoryMap is not C64MemoryMap)
                    throw new ArgumentException(
                        "The 'u64sim' backend requires a C64 memory map. " +
                        "Add system(c64) to your suite file.");

                return new U64SimBackend(u64SimConfig, memoryMap);
```

Update the `default:` message to list the new backend:

```csharp
            default:
                throw new ArgumentException(
                    $"Unknown backend type: {backendType}. " +
                    "Valid options: sim, vice, novavm, verilator, u64sim");
```

- [ ] **Step 6: Add the CLI options**

In `sim6502/Sim6502CLI.cs`, change the `--backend` help text at line 92-94:

```csharp
            [Option("backend", Required = false, Default = "sim",
                HelpText = "Execution backend: 'sim' for internal simulator, 'vice' for VICE MCP, " +
                           "'novavm' for e6502 emulator, 'verilator' for FPGA simulation, " +
                           "'u64sim' for a simulated Ultimate 64")]
            public string Backend { get; set; } = "sim";
```

Add after the `NovaVmTimeout` option (line 126):

```csharp
            [Option("u64sim-fs-root", Required = false,
                HelpText = "Host directory exposed to the C64 as the Ultimate's /Usb0 mount")]
            public string? U64SimFsRoot { get; set; }

            [Option("u64sim-uci-latency", Required = false, Default = 64,
                HelpText = "CPU cycles the UCI holds the Busy state before answering. " +
                           "Non-zero by default so busy-wait loops are exercised")]
            public int U64SimUciLatency { get; set; } = 64;
```

Add to the `SimBaseListener` initialiser after `NovaVmConfig` (line 222-227):

```csharp
                    U64SimConfig = opts.Backend == "u64sim" ? new U64SimBackendConfig
                    {
                        FsRoot = opts.U64SimFsRoot ?? "",
                        UciLatencyCycles = opts.U64SimUciLatency
                    } : null
```

- [ ] **Step 7: Add the listener property**

In `sim6502/Grammar/SimBaseListener.cs`, next to `NovaVmConfig`, add:

```csharp
        public U64SimBackendConfig? U64SimConfig { get; set; }
```

and pass it through at line 391:

```csharp
                Backend = BackendFactory.Create(BackendType, _currentProcessorType,
                    _currentMemoryMap!, ViceConfig, NovaVmConfig, U64SimConfig);
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~U64SimBackendTests|FullyQualifiedName~BackendFactoryTests"`
Expected: PASS — 13 U64SimBackend tests plus the existing and 3 new factory tests.

- [ ] **Step 9: Run the full suite**

Run: `dotnet test`
Expected: PASS — everything green. The `BackendFactory.Create` signature grew an
optional parameter, so existing call sites are unaffected, but confirm it.

- [ ] **Step 10: Commit**

```bash
git add sim6502/Backend/U64SimBackend.cs sim6502/Backend/U64SimBackendConfig.cs \
        sim6502/Backend/BackendFactory.cs sim6502/Sim6502CLI.cs \
        sim6502/Grammar/SimBaseListener.cs \
        sim6502tests/Backend/U64SimBackendTests.cs \
        sim6502tests/Backend/BackendFactoryTests.cs
git commit -m "feat(backend): add the u64sim execution backend

Composes SimulatorBackend with the UCI register block, two independent
Ultimate DOS targets, and the control target, mapping the UCI into the C64 I/O
range. Busy latency is wired to the processor's own cycle count so 6502
polling loops really advance it. Requires system(c64), and says so when it is
missing rather than failing later."
```

---

## Task 12: Grammar and listener — `ultimate()`, `uci()`, `uci_status()`, `uci_data()`

**Files:**
- Modify: `sim6502/Grammar/sim6502.g4`
- Modify: `sim6502/Grammar/SimBaseListener.cs`
- Regenerate and commit: `sim6502/Grammar/Generated/*`
- Test: `sim6502tests/GrammarTests/UltimateGrammarTests.cs`

**Interfaces:**
- Consumes: `U64SimBackend.IssueUciCommand` and `SimBaseListener.U64SimConfig` (Task 11).
- Produces: four DSL constructs — `ultimate(fs_root = "...")`,
  `uci(target, command, args...)`, `uci_status("...")`, `uci_data(n)`.

`uci` becomes a reserved word. Any existing suite using it as a symbol name needs
renaming; say so in the commit message.

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/GrammarTests/UltimateGrammarTests.cs`:

```csharp
using Antlr4.Runtime;
using FluentAssertions;
using sim6502.Errors;
using sim6502.Grammar;
using sim6502.Grammar.Generated;
using Xunit;

namespace sim6502tests.GrammarTests;

public class UltimateGrammarTests
{
    private static ErrorCollector Parse(string source)
    {
        var collector = new ErrorCollector();
        collector.SetSource(source, "test.6502");

        var lexer = new sim6502Lexer(new AntlrInputStream(source));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));

        var parser = new sim6502Parser(new CommonTokenStream(lexer)) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));

        parser.suites();
        return collector;
    }

    private static string Wrap(string body) => $@"
suites {{
  suite(""ultimate"") {{
    system(c64)
    ultimate(fs_root = ""fixtures/usb0"")
    test(""t"", ""d"") {{
{body}
    }}
  }}
}}";

    [Fact]
    public void UltimateDeclaration_Parses()
    {
        Parse(Wrap("      a = $01")).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciCall_WithNoArguments_Parses()
    {
        Parse(Wrap("      uci($01, $01)")).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciCall_WithAStringArgument_Parses()
    {
        Parse(Wrap(@"      uci($01, $11, ""/Usb0/data"")")).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciCall_WithMixedArguments_Parses()
    {
        Parse(Wrap(@"      uci($01, $02, $01, ""game.prg"")")).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciStatus_ParsesInsideAssert()
    {
        Parse(Wrap(@"      assert(uci_status(""00,OK""), ""ok"")")).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciData_ParsesInsideAComparison()
    {
        Parse(Wrap(@"      assert(uci_data(0) == $55, ""first byte"")"))
            .HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciData_ParsesInsideAnExpression()
    {
        Parse(Wrap(@"      assert(uci_data(0) + uci_data(1) == $10, ""sum"")"))
            .HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UciCall_ParsesInsideASetupBlock()
    {
        var source = @"
suites {
  suite(""ultimate"") {
    system(c64)
    ultimate(fs_root = ""fixtures/usb0"")
    setup {
      uci($01, $11, ""/Usb0/data"")
    }
    test(""t"", ""d"") {
      a = $01
    }
  }
}";
        Parse(source).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void UltimateDeclaration_WithoutFsRoot_IsASyntaxError()
    {
        var source = @"
suites {
  suite(""ultimate"") {
    system(c64)
    ultimate()
    test(""t"", ""d"") {
      a = $01
    }
  }
}";
        Parse(source).HasErrors.Should().BeTrue();
    }

    [Fact]
    public void UciCall_WithOnlyOneArgument_IsASyntaxError()
    {
        Parse(Wrap("      uci($01)")).HasErrors.Should().BeTrue();
    }

    [Fact]
    public void ExistingSuitesWithoutUltimate_StillParse()
    {
        var source = @"
suites {
  suite(""plain"") {
    system(c64)
    test(""t"", ""d"") {
      a = $01
      assert(a == $01, ""a"")
    }
  }
}";
        Parse(source).HasErrors.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UltimateGrammarTests"`
Expected: FAIL — every test touching `ultimate(...)`, `uci(...)`,
`uci_status(...)` or `uci_data(...)` reports syntax errors. Only
`ExistingSuitesWithoutUltimate_StillParse` and
`UltimateDeclaration_WithoutFsRoot_IsASyntaxError` pass, the latter for the wrong
reason.

- [ ] **Step 3: Add the grammar rules**

In `sim6502/Grammar/sim6502.g4`, change the `suite` rule (line 35):

```antlr
suite
    : Suite LParen suiteName RParen LBrace
        (systemDeclaration | processorDeclaration)?
        ultimateDeclaration?
        (testFunction | symbolsFunction | loadFunction | romDeclaration | setupBlock)+
      RBrace
    ;
```

Add the new rules after `romFilename` (after line 78):

```antlr
// ── Ultimate 64 (u64sim backend) ──

ultimateDeclaration
    : Ultimate LParen FsRoot Assign StringLiteral RParen
    ;

uciFunction
    : Uci LParen expression Comma expression (Comma uciArg)* RParen
    ;

uciArg
    : expression
    | StringLiteral
    ;

uciStatusFunction
    : UciStatus LParen StringLiteral RParen
    ;

uciDataFunction
    : UciData LParen expression RParen
    ;
```

Add an alternative to `comparison` (line 114):

```antlr
comparison
    : compareLHS CompareOperator expression     # compareExpression
    | memoryChkFunction                         # memoryChk
    | memoryCmpFunction                         # memoryCmp
    | screenContainsFunction                    # screenContains
    | screenLineFunction                        # screenLineCheck
    | uciStatusFunction                         # uciStatusCheck
    ;
```

Add `| uciFunction` as the last alternative of both `testContents` (line 195) and
`setupContents` (line 212).

Add alternatives to `intFunction` (line 344) and `boolFunction` (line 349):

```antlr
intFunction
    : peekByteFunction  # peekByteFunctionValue
    | peekWordFunction  # peekWordFunctionValue
    | uciDataFunction   # uciDataFunctionValue
    ;

boolFunction
    : memoryChkFunction      # memoryChkFunctionValue
    | memoryCmpFunction      # memoryCmpFunctionValue
    | screenContainsFunction # screenContainsFunctionValue
    | screenLineFunction     # screenLineFunctionValue
    | uciStatusFunction      # uciStatusFunctionValue
    ;
```

Add the tokens after the NovaVM keyword block (after line 507). `UciStatus` and
`UciData` are listed before `Uci` for readability — ANTLR's longest-match rule makes
the order immaterial, but a reader should not have to know that:

```antlr
// Ultimate 64 keywords
Ultimate:       'ultimate';
FsRoot:         'fs_root';
UciStatus:      'uci_status';
UciData:        'uci_data';
Uci:            'uci';
```

- [ ] **Step 4: Regenerate the parser**

Run: `make grammar`
Expected: no output on success. Confirm the new rules landed:

```bash
grep -c "UciFunctionContext" sim6502/Grammar/Generated/sim6502Parser.cs
```
Expected: a non-zero count.

- [ ] **Step 5: Add the listener state and the suite declaration**

In `sim6502/Grammar/SimBaseListener.cs`, add near the other private fields:

```csharp
        // Result of the most recent uci() call, read by uci_status() and uci_data().
        private string _lastUciStatus = "";
        private byte[] _lastUciData = Array.Empty<byte>();
        private bool _uciCalled;
```

In `EnterSuite`, insert **before** the `BackendFactory.Create` call at line 390, so
the config is complete when the backend is constructed:

```csharp
            // A suite-level ultimate() declaration overrides --u64sim-fs-root.
            var ultimateDecl = context.ultimateDeclaration();
            if (ultimateDecl != null)
            {
                var fsRoot = StripQuotes(ultimateDecl.StringLiteral().GetText());
                U64SimConfig ??= new U64SimBackendConfig();
                U64SimConfig.FsRoot = fsRoot;
                Logger.Info($"Ultimate filesystem root set to: {fsRoot}");
            }
```

In `EnterTestFunction` (line 909), clear the stored result so one test cannot read
another's response:

```csharp
            _lastUciStatus = "";
            _lastUciData = Array.Empty<byte>();
            _uciCalled = false;
```

- [ ] **Step 6: Add the command handlers**

Add a new region beside the NovaVM one:

```csharp
        #region Ultimate 64 commands

        private U64SimBackend RequireU64SimBackend(string command)
        {
            if (Backend is U64SimBackend u64)
                return u64;

            throw new InvalidOperationException(
                $"'{command}' requires the u64sim backend. Current backend: {BackendType}");
        }

        public override void ExitUciFunction(sim6502Parser.UciFunctionContext context)
        {
            if (_inSetupBlockDefinition || _currentTestSkipped)
                return;

            var backend = RequireU64SimBackend("uci()");

            var bytes = new List<byte>
            {
                (byte)(GetIntValue(context.expression(0)) & 0xFF),   // target
                (byte)(GetIntValue(context.expression(1)) & 0xFF)    // command
            };

            foreach (var arg in context.uciArg())
            {
                var literal = arg.StringLiteral();
                if (literal != null)
                    bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(StripQuotes(literal.GetText())));
                else
                    bytes.Add((byte)(GetIntValue(arg.expression()) & 0xFF));
            }

            var command = bytes.ToArray();
            Logger.Debug($"uci(${command[0]:X2}, ${command[1]:X2}) — {command.Length} bytes");

            var (status, data) = backend.IssueUciCommand(command);
            _lastUciStatus = status;
            _lastUciData = data;
            _uciCalled = true;
        }

        public override void ExitUciStatusCheck(sim6502Parser.UciStatusCheckContext context)
        {
            if (_currentTestSkipped)
                return;

            var expected = StripQuotes(context.uciStatusFunction().StringLiteral().GetText());
            var matched = CheckUciStatus(expected, out var actual);
            SetBoolValue(context, matched);

            if (!matched)
                FailAssertion($"uci_status(\"{expected}\") failed — actual status was \"{actual}\"");
        }

        public override void ExitUciStatusFunctionValue(
            sim6502Parser.UciStatusFunctionValueContext context)
        {
            if (_currentTestSkipped)
                return;

            var expected = StripQuotes(context.uciStatusFunction().StringLiteral().GetText());
            SetBoolValue(context, CheckUciStatus(expected, out _));
        }

        private bool CheckUciStatus(string expected, out string actual)
        {
            actual = _lastUciStatus;

            if (!_uciCalled)
            {
                FailAssertion("uci_status() called before any uci() command in this test");
                return false;
            }

            return string.Equals(actual, expected, StringComparison.Ordinal);
        }

        public override void ExitUciDataFunction(sim6502Parser.UciDataFunctionContext context)
        {
            SetIntValue(context, ReadUciData(context));
        }

        public override void ExitUciDataFunctionValue(
            sim6502Parser.UciDataFunctionValueContext context)
        {
            SetIntValue(context, GetIntValue(context.uciDataFunction()));
        }

        private int ReadUciData(sim6502Parser.UciDataFunctionContext context)
        {
            if (_currentTestSkipped)
                return 0;

            if (!_uciCalled)
            {
                FailAssertion("uci_data() called before any uci() command in this test");
                return 0;
            }

            var index = GetIntValue(context.expression());

            if (index < 0 || index >= _lastUciData.Length)
            {
                FailAssertion(
                    $"uci_data({index}) is out of range — the last response was " +
                    $"{_lastUciData.Length} bytes");
                return 0;
            }

            return _lastUciData[index];
        }

        #endregion
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~UltimateGrammarTests"`
Expected: PASS — 11 passed.

- [ ] **Step 8: Run the full suite**

Run: `dotnet test`
Expected: PASS. The grammar changed, so every parse test and example suite is at
risk. If a pre-existing suite used `uci` as a symbol name it now fails to parse —
rename the symbol rather than removing the keyword.

- [ ] **Step 9: Commit**

```bash
git add sim6502/Grammar/sim6502.g4 sim6502/Grammar/Generated/ \
        sim6502/Grammar/SimBaseListener.cs \
        sim6502tests/GrammarTests/UltimateGrammarTests.cs
git commit -m "feat(grammar): add ultimate(), uci(), uci_status(), uci_data()

uci() issues a command from the host and stores the response. uci_status() is a
predicate rather than a string accessor so it reuses the existing boolFunction
path, and reports the actual status on failure. Stored results are cleared per
test so one test cannot read another's response.

BREAKING CHANGE: 'uci' is now a reserved word. Suites using it as a symbol name
must rename that symbol."
```

---


## Task 13: Functional test — a 6502 UCI client through the whole stack

**Files:**
- Create: `sim6502tests/Systems/Ultimate/UciClientProgramTests.cs`
- Create: `sim6502tests/Fixtures/usb0/readme.txt` (content `readme`)
- Create: `sim6502tests/Fixtures/usb0/data/hello.txt` (content `HELLO FROM USB0`)
- Create: `example/ultimate.suite`
- Modify: `sim6502tests/sim6502tests.csproj` (copy the fixture tree to output)
- Modify: `sim6502tests/Backend/IntegrationSuiteParseTests.cs`

**Interfaces:**
- Consumes: `U64SimBackend`, `U64SimBackendConfig` (Task 11); the DSL from Task 12.
- Produces: nothing new. This is the end-to-end proof.

The client is a hand-assembled byte array, not an assembled file — there is no
assembler in CI and adding one is not worth it for 61 bytes.

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/Systems/Ultimate/UciClientProgramTests.cs`:

```csharp
using System.Text;
using FluentAssertions;
using sim6502.Backend;
using sim6502.Systems;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

/// <summary>
/// Drives the whole stack the way a real program does: 6502 code executing on the
/// simulated 6510, touching $DF1C-$DF1F through the C64 memory map, reaching the
/// UCI register block and the Ultimate DOS target.
/// </summary>
public class UciClientProgramTests : IDisposable
{
    private const int ProgramAddress = 0xC200;
    private const int ResultBuffer = 0xC000;
    private const int LengthByte = 0xC0FF;

    /// <summary>Offset and length of the busy-wait loop within the program.</summary>
    private const int WaitLoopOffset = 20;
    private const int WaitLoopLength = 9;

    /// <summary>
    /// IDENTIFY against DOS target $01. Copies the response to $C000 and stores the
    /// byte count at $C0FF. Hand-assembled at $C200:
    ///
    ///   C200  A9 08        lda #$08         ; CMD_ERROR - clear any stale error
    ///   C202  8D 1C DF     sta $DF1C
    ///   C205  A9 01        lda #$01         ; target $01 = Ultimate DOS
    ///   C207  8D 1D DF     sta $DF1D
    ///   C20A  A9 01        lda #$01         ; DOS_CMD_IDENTIFY
    ///   C20C  8D 1D DF     sta $DF1D
    ///   C20F  A9 01        lda #$01         ; CMD_PUSH_CMD
    ///   C211  8D 1C DF     sta $DF1C
    ///   C214  AD 1C DF     lda $DF1C        ; wait: poll while state == Busy
    ///   C217  29 30        and #$30
    ///   C219  C9 10        cmp #$10
    ///   C21B  F0 F7        beq $C214
    ///   C21D  A2 00        ldx #$00
    ///   C21F  AD 1C DF     lda $DF1C        ; rdloop: bit 7 = response available
    ///   C222  10 09        bpl $C22D
    ///   C224  AD 1E DF     lda $DF1E
    ///   C227  9D 00 C0     sta $C000,x
    ///   C22A  E8           inx
    ///   C22B  D0 F2        bne $C21F
    ///   C22D  8E FF C0     stx $C0FF        ; done
    ///   C230  A9 02        lda #$02         ; CMD_NEXT_DATA
    ///   C232  8D 1C DF     sta $DF1C
    ///   C235  AD 1C DF     lda $DF1C        ; ack: wait for bit 1 to clear
    ///   C238  29 02        and #$02
    ///   C23A  D0 F9        bne $C235
    ///   C23C  60           rts
    /// </summary>
    private static readonly byte[] CorrectClient =
    {
        0xA9, 0x08, 0x8D, 0x1C, 0xDF,
        0xA9, 0x01, 0x8D, 0x1D, 0xDF,
        0xA9, 0x01, 0x8D, 0x1D, 0xDF,
        0xA9, 0x01, 0x8D, 0x1C, 0xDF,
        0xAD, 0x1C, 0xDF, 0x29, 0x30, 0xC9, 0x10, 0xF0, 0xF7,
        0xA2, 0x00,
        0xAD, 0x1C, 0xDF, 0x10, 0x09,
        0xAD, 0x1E, 0xDF, 0x9D, 0x00, 0xC0, 0xE8, 0xD0, 0xF2,
        0x8E, 0xFF, 0xC0,
        0xA9, 0x02, 0x8D, 0x1C, 0xDF,
        0xAD, 0x1C, 0xDF, 0x29, 0x02, 0xD0, 0xF9,
        0x60
    };

    private readonly string _fixture;

    public UciClientProgramTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64sim-client-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixture);
        File.WriteAllText(Path.Combine(_fixture, "readme.txt"), "readme");
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    private U64SimBackend NewBackend(int latency)
    {
        var config = new U64SimBackendConfig { FsRoot = _fixture, UciLatencyCycles = latency };
        return new U64SimBackend(config, new C64MemoryMap());
    }

    private static string ReadResult(U64SimBackend backend, int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
            bytes[i] = backend.ReadByte(ResultBuffer + i);
        return Encoding.ASCII.GetString(bytes);
    }

    /// <summary>The same program with its busy-wait loop replaced by NOPs.</summary>
    private static byte[] BrokenClient()
    {
        var broken = (byte[])CorrectClient.Clone();
        for (var i = WaitLoopOffset; i < WaitLoopOffset + WaitLoopLength; i++)
            broken[i] = 0xEA;   // NOP
        return broken;
    }

    [Fact]
    public void ProgramIsExactlyTheDocumentedLength()
    {
        CorrectClient.Should().HaveCount(61, "the hand-assembled listing is 61 bytes");
    }

    [Fact]
    public void WaitLoopOffsetPointsAtTheDocumentedInstructions()
    {
        // C214: AD 1C DF  lda $DF1C
        CorrectClient.Skip(WaitLoopOffset).Take(3).Should().Equal(0xAD, 0x1C, 0xDF);
        // C21B: F0 F7  beq $C214 -- the last two bytes of the loop
        CorrectClient.Skip(WaitLoopOffset + WaitLoopLength - 2).Take(2).Should().Equal(0xF0, 0xF7);
    }

    [Fact]
    public void CorrectClient_ReadsTheIdentifyResponse()
    {
        using var backend = NewBackend(latency: 64);
        backend.LoadBinary(CorrectClient, ProgramAddress);

        var result = backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true);

        result.ExitedCleanly.Should().BeTrue();
        backend.ReadByte(LengthByte).Should().Be(20, "\"ULTIMATE-II DOS V1.2\" is 20 bytes");
        ReadResult(backend, 20).Should().Be("ULTIMATE-II DOS V1.2");
    }

    [Fact]
    public void CorrectClient_WorksWithZeroLatencyToo()
    {
        using var backend = NewBackend(latency: 0);
        backend.LoadBinary(CorrectClient, ProgramAddress);

        backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true)
               .ExitedCleanly.Should().BeTrue();
        ReadResult(backend, 20).Should().Be("ULTIMATE-II DOS V1.2");
    }

    [Fact]
    public void CorrectClient_LeavesTheUciReadyForAnotherCommand()
    {
        using var backend = NewBackend(latency: 64);
        backend.LoadBinary(CorrectClient, ProgramAddress);
        backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true);

        // Rerunning must work. If the acknowledge sequence were wrong the UCI would
        // be stuck out of the idle state and the second run would read nothing.
        for (var i = 0; i < 20; i++) backend.WriteByte(ResultBuffer + i, 0x00);
        backend.WriteByte(LengthByte, 0x00);

        backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true);

        backend.ReadByte(LengthByte).Should().Be(20);
        ReadResult(backend, 20).Should().Be("ULTIMATE-II DOS V1.2");
    }

    [Fact]
    public void BrokenClient_WithNoBusyWait_ReadsNothingAtTheDefaultLatency()
    {
        using var backend = NewBackend(latency: 64);
        backend.LoadBinary(BrokenClient(), ProgramAddress);

        backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true);

        backend.ReadByte(LengthByte).Should().Be(0,
            "without a busy-wait the response is not ready yet -- exactly the bug a " +
            "zero-latency simulator would hide");
    }

    [Fact]
    public void BrokenClient_PassesAtZeroLatency_WhichIsWhyTheDefaultIsNonZero()
    {
        using var backend = NewBackend(latency: 0);
        backend.LoadBinary(BrokenClient(), ProgramAddress);

        backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true);

        backend.ReadByte(LengthByte).Should().Be(20,
            "a zero-latency UCI answers instantly and the missing busy-wait goes " +
            "unnoticed -- documented here so the non-zero default is not simplified away");
    }

    [Fact]
    public void HostAndCpuPathsAgree()
    {
        using var backend = NewBackend(latency: 64);

        var (status, data) = backend.IssueUciCommand(new byte[] { 0x01, 0x01 });
        status.Should().Be("00,OK");

        backend.LoadBinary(CorrectClient, ProgramAddress);
        backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true);

        ReadResult(backend, data.Length).Should().Be(Encoding.ASCII.GetString(data),
            "the host-side and register-level paths must return the same bytes");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UciClientProgramTests"`
Expected: FAIL if anything in the stack is wrong. If Tasks 2-11 are correct these
pass immediately. A failure here is a genuine integration defect, most likely one
of two things: the UCI is not reachable through the memory map (check the
`RegisterIoHandler` call in `U64SimBackend`), or the C64 banking has I/O switched
out (`$01` should default to `$37`, giving LORAM, HIRAM and CHAREN all set).

- [ ] **Step 3: Create the fixture tree**

```bash
mkdir -p sim6502tests/Fixtures/usb0/data
printf 'HELLO FROM USB0' > sim6502tests/Fixtures/usb0/data/hello.txt
printf 'readme' > sim6502tests/Fixtures/usb0/readme.txt
```

Add to `sim6502tests/sim6502tests.csproj` so the fixtures reach the output
directory:

```xml
    <ItemGroup>
      <None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
    </ItemGroup>
```

- [ ] **Step 4: Create `example/ultimate.suite`**

```
; Ultimate 64 feature tests against the simulated UCI and Ultimate DOS.
;
; Run with:
;   dotnet run --project sim6502 -- --suitefile example/ultimate.suite \
;     --backend u64sim --u64sim-fs-root sim6502tests/Fixtures/usb0
;
; Every uci() call here runs host-side, so no assembled program is needed.

suites {
  suite("ultimate dos through the UCI") {
    system(c64)
    ultimate(fs_root = "sim6502tests/Fixtures/usb0")

    test("dos-identify", "the DOS target reports its version") {
      uci($01, $01)
      assert(uci_status("00,OK"), "IDENTIFY succeeded")
      assert(uci_data(0) == $55, "response starts with 'U'")
      assert(uci_data(1) == $4c, "second byte is 'L'")
    }

    test("dos-change-directory", "changing into an existing directory succeeds") {
      uci($01, $11, "/Usb0/data")
      assert(uci_status("00,OK"), "chdir to /Usb0/data succeeded")

      uci($01, $12)
      assert(uci_status("00,OK"), "GET_PATH succeeded")
      assert(uci_data(0) == $2f, "path starts with '/'")
    }

    test("dos-change-directory-missing", "a missing directory is reported, not ignored") {
      uci($01, $11, "/Usb0/nowhere")
      assert(uci_status("83,NO SUCH DIRECTORY"), "chdir to a missing directory failed")
    }

    test("dos-open-and-read", "a file opens and its first bytes read back") {
      uci($01, $11, "/Usb0/data")
      assert(uci_status("00,OK"), "chdir succeeded")

      uci($01, $02, $01, "hello.txt")
      assert(uci_status("00,OK"), "OPEN_FILE succeeded")

      uci($01, $04, $0f, $00)
      assert(uci_data(0) == $48, "first byte is 'H'")
      assert(uci_data(1) == $45, "second byte is 'E'")

      uci($01, $03)
      assert(uci_status("00,OK"), "CLOSE_FILE succeeded")
    }

    test("dos-open-missing", "opening a missing file is reported") {
      uci($01, $02, $01, "no-such-file.prg")
      assert(uci_status("82,FILE NOT FOUND"), "OPEN_FILE on a missing file failed")
    }

    test("dos-echo", "ECHO returns the command it was given") {
      uci($01, $f0, $de, $ad)
      assert(uci_status("00,OK"), "ECHO succeeded")
      assert(uci_data(0) == $01, "echo includes the target byte")
      assert(uci_data(1) == $f0, "echo includes the command byte")
      assert(uci_data(2) == $de, "echo includes the payload")
    }

    test("control-identify", "the control target answers on $04") {
      uci($04, $01)
      assert(uci_status("00,OK"), "control IDENTIFY succeeded")
      assert(uci_data(0) == $43, "response starts with 'C'")
    }

    test("control-reu-absent", "REU commands report the REU is not enabled") {
      uci($04, $08, "image.reu")
      assert(uci_status("84,REU NOT ENABLED"), "LOAD_REU reports no REU")
    }

    test("unknown-target", "an unpopulated target identifies as NO TARGET") {
      uci($07, $01)
      assert(uci_status("00,OK"), "IDENTIFY on an empty target still answers")
      assert(uci_data(0) == $4e, "response starts with 'N' for NO TARGET")
    }

    test("unknown-command", "a nonsense command is rejected") {
      uci($01, $7e)
      assert(uci_status("21,UNKNOWN COMMAND"), "unknown DOS command rejected")
    }
  }
}
```

- [ ] **Step 5: Add the suite to the parse check**

Add to `sim6502tests/Backend/IntegrationSuiteParseTests.cs`:

```csharp
    [Fact]
    public void UltimateSuite_Parses()
    {
        var path = Path.Combine("../../../../example", "ultimate.suite");
        File.Exists(path).Should().BeTrue($"expected the example suite at '{path}'");

        var collector = new ErrorCollector();
        var source = File.ReadAllText(path);
        collector.SetSource(source, path);

        var lexer = new sim6502Lexer(new AntlrInputStream(source));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new SimErrorListener(collector));

        var parser = new sim6502Parser(new CommonTokenStream(lexer)) { BuildParseTree = true };
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new SimErrorListener(collector));
        parser.suites();

        collector.HasErrors.Should().BeFalse();
    }
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~UciClientProgramTests|FullyQualifiedName~IntegrationSuiteParseTests"`
Expected: PASS — 8 client tests plus the existing and new parse tests.

- [ ] **Step 7: Run the example suite end to end**

```bash
dotnet run --project sim6502 -- --suitefile example/ultimate.suite \
  --backend u64sim --u64sim-fs-root sim6502tests/Fixtures/usb0
echo "exit: $?"
```
Expected: `1 of 1 suites passed.` and `exit: 0`.

Note the suite hard-codes `fs_root`, so the `--u64sim-fs-root` flag is redundant
here — the declaration wins. Run it once with the flag and once without to confirm
both paths work.

- [ ] **Step 8: Commit**

```bash
git add sim6502tests/Systems/Ultimate/UciClientProgramTests.cs \
        sim6502tests/Fixtures/ sim6502tests/sim6502tests.csproj \
        example/ultimate.suite \
        sim6502tests/Backend/IntegrationSuiteParseTests.cs
git commit -m "test(ultimate): drive the u64sim stack from 6502 code and from the DSL

A hand-assembled 61-byte UCI client executes on the simulated 6510, reaches
\$DF1C-\$DF1F through the C64 memory map, and reads the DOS IDENTIFY response.
A variant with its busy-wait loop replaced by NOPs reads nothing at the default
latency and succeeds at zero latency; that pair is the standing argument for why
the latency default is not zero.

example/ultimate.suite covers the same ground through the DSL with no assembly."
```

---

## Task 14: Documentation and version

**Files:**
- Modify: `README.md` (backend table near line 20; new Ultimate 64 section)
- Modify: `CHANGELOG.md`
- Modify: `sim6502/sim6502.csproj` (`Version`, `AssemblyVersion`, `FileVersion`)
- Test: extend `sim6502tests/LicenseTests.cs`

**Interfaces:**
- Consumes: everything. Nothing consumes this.

- [ ] **Step 1: Write the failing test**

Add to `sim6502tests/LicenseTests.cs`:

```csharp
    [Fact]
    public void Readme_DocumentsTheU64SimBackend()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));

        text.Should().Contain("u64sim");
        text.Should().Contain("--u64sim-fs-root");
        text.Should().Contain("--u64sim-uci-latency");
        text.Should().Contain("uci_status");
        text.Should().Contain("system(c64)");
    }

    [Fact]
    public void Changelog_RecordsTheLicenceChangeAndReservedWord()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "CHANGELOG.md"));

        text.Should().Contain("4.0.0");
        text.Should().Contain("GPL-3.0");
        text.Should().Contain("reserved word");
    }

    [Fact]
    public void ProjectVersion_IsFourPointZero()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "sim6502", "sim6502.csproj"));
        text.Should().Contain("<Version>4.0.0</Version>");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~LicenseTests"`
Expected: FAIL — the three new tests fail; the two from Task 1 still pass.

- [ ] **Step 3: Update the README backend table**

Replace the four-row table near line 20:

```markdown
sim6502 now has five execution backends:

| Backend | Use it for |
|---------|------------|
| `sim` | Fast internal 6502/6510/65C02 assembly-unit tests |
| `vice` | Hardware-accurate C64 tests through a VICE MCP server |
| `novavm` | BASIC-level integration tests against the e6502/NovaVM Avalonia emulator |
| `verilator` | BASIC-level integration tests against the NovaVM FPGA Verilator simulation |
| `u64sim` | Ultimate 64 feature tests against a simulated UCI and Ultimate DOS |
```

- [ ] **Step 4: Add the Ultimate 64 README section**

Insert before the `#### License` section:

````markdown
#### Ultimate 64 testing (`u64sim`)

The `u64sim` backend runs your code against a simulated Ultimate 64: the Ultimate
Command Interface at `$DF1B-$DF1F`, two Ultimate DOS targets at `$01` and `$02`,
and the control target at `$04`. No hardware needed, so it runs in CI.

It requires `system(c64)` — the UCI lives in the cartridge I/O range, which only
the C64 memory map models.

```
suites {
  suite("ultimate dos") {
    system(c64)
    ultimate(fs_root = "tests/fixtures/usb0")   ; exposed to the C64 as /Usb0

    test("dos-identify", "the DOS target reports its version") {
      uci($01, $01)
      assert(uci_status("00,OK"), "IDENTIFY succeeded")
      assert(uci_data(0) == $55, "response starts with 'U'")
    }

    test("client-code", "our own UCI client works") {
      jsr([load_via_uci], stop_on_rts = true, fail_on_brk = true)
      assert(peekbyte($c000) == $42, "first byte arrived")
    }
  }
}
```

| DSL | What it does |
|---|---|
| `ultimate(fs_root = "...")` | Suite-level. Exposes a host directory as `/Usb0`. The tree is copied to a temp location, so fixtures are never mutated. |
| `uci(target, command, args...)` | Issues a UCI command from the host. String literals become raw ASCII bytes; numeric expressions become single bytes. |
| `uci_status("00,OK")` | True when the last `uci()` call's status matches. Failure reports the actual status. |
| `uci_data(n)` | Byte `n` of the last `uci()` call's response data. |

Your own 6502 code drives `$DF1C-$DF1F` directly and needs no DSL support — the
backend answers as the Ultimate would, and you assert on memory and registers as
usual.

| Flag | Default | Meaning |
|---|---|---|
| `--u64sim-fs-root` | none | Host directory exposed as `/Usb0`. Required unless `ultimate(fs_root = ...)` sets it; the declaration wins when both are present. |
| `--u64sim-uci-latency` | `64` | CPU cycles the UCI holds the Busy state before answering. |

**Why the latency default is not zero.** The real UCI is asynchronous: a client
writes `PUSH_CMD` to `$DF1C` then polls until the state leaves Busy. If the
simulator answered instantly, a client with a broken or missing busy-wait loop
would pass here and fail on hardware — the simulator would hide the exact class of
bug it exists to catch. Set it to `0` only when a test is deliberately not about
timing.

Not yet implemented, and next on the list: the REU (`$DF00-$DF0A`), the UCI network
target `$03`, drive emulation and disk mounting, and a `u64` backend that drives
real hardware over the network. Ultimate DOS commands in that group answer
`99,FUNCTION NOT IMPLEMENTED`, and the control target's REU commands answer
`84,REU NOT ENABLED` — the same status hardware gives with no REU configured — so
your code takes the hardware path today.
````

- [ ] **Step 5: Add the changelog entry**

Prepend to `CHANGELOG.md`, matching the existing format:

```markdown
## 4.0.0

### Breaking

- **Licence changed from BSD-2-Clause to GPL-3.0.** Ultimate 64 support ports
  protocol and DOS behaviour from
  [GideonZ/1541ultimate](https://github.com/GideonZ/1541ultimate), which is
  GPL-3.0. Releases before 4.0.0 remain BSD-2-Clause. If you embed sim6502 in a
  distributed closed-source product, pin to 3.x.
- **`uci` is now a reserved word.** Suites using it as a symbol name must rename
  that symbol.

### Added

- `u64sim` execution backend: a simulated Ultimate 64 with the Ultimate Command
  Interface at `$DF1B-$DF1F`, two Ultimate DOS targets, and the control target.
  Requires `system(c64)`.
- DSL: `ultimate(fs_root = "...")`, `uci(target, command, args...)`,
  `uci_status("...")`, `uci_data(n)`.
- CLI: `--backend u64sim`, `--u64sim-fs-root`, `--u64sim-uci-latency`.
- `IMemoryMap.RegisterIoHandler` and I/O handler dispatch in `C64MemoryMap`, so
  peripherals can claim address ranges within `$D000-$DFFF`. `IIOHandler` had
  existed since the systems refactor but was never wired up.

### Notes

- The UCI holds the Busy state for 64 cycles by default rather than answering
  instantly, so client busy-wait loops are genuinely exercised. A test pins this by
  running a deliberately broken client that passes at zero latency and fails at the
  default.
- REU, the UCI network target, drive emulation, and a real-hardware `u64` backend
  are the next milestone.
```

- [ ] **Step 6: Bump the version**

In `sim6502/sim6502.csproj`:

```xml
        <Version>4.0.0</Version>
        <AssemblyVersion>4.0.0.0</AssemblyVersion>
        <FileVersion>4.0.0.0</FileVersion>
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~LicenseTests"`
Expected: PASS — 5 passed.

- [ ] **Step 8: Full verification**

```bash
make grammar
dotnet build -c Release
dotnet test -c Release
dotnet run --project sim6502 -- --suitefile example/ultimate.suite --backend u64sim
echo "exit: $?"
```
Expected: build clean, every test green, `1 of 1 suites passed.`, `exit: 0`.

- [ ] **Step 9: Commit**

```bash
git add README.md CHANGELOG.md sim6502/sim6502.csproj sim6502tests/LicenseTests.cs
git commit -m "docs: document the u64sim backend and release 4.0.0

Backend table, the Ultimate 64 section covering the DSL and CLI flags, and the
reasoning behind the non-zero UCI latency default. Version bumps to 4.0.0
because the GPL-3.0 relicense and the new 'uci' reserved word are both
breaking."
```

---

## Verification

After Task 14:

```bash
make grammar
dotnet build -c Release
dotnet test -c Release
dotnet run --project sim6502 -- --suitefile example/ultimate.suite \
  --backend u64sim --u64sim-fs-root sim6502tests/Fixtures/usb0
echo "exit: $?"
```

All four must succeed. The existing `sim`, `vice`, `novavm`, and `verilator` suites
must stay green throughout — Task 2 touches the C64 read and write paths that every
C64 test uses, and Task 12 changes the grammar, so regressions in either would
surface across the whole suite rather than locally.

Expected new test counts by file:

| File | Tests |
|---|---|
| `LicenseTests` | 5 |
| `C64MemoryMapIoHandlerTests` | 6 |
| `UciConstantsTests` | 5 |
| `UciRegistersDecodeTests` | 12 |
| `UciRegistersDispatchTests` | 16 |
| `UltimateFileSystemTests` | 20 |
| `UltimateDosTargetNavigationTests` | 18 |
| `UltimateDosTargetFileTests` | 27 |
| `UltimateDosTargetInfoTests` | 37 |
| `ControlTargetTests` | 14 |
| `U64SimBackendTests` | 13 |
| `UltimateGrammarTests` | 11 |
| `UciClientProgramTests` | 8 |
| `BackendFactoryTests` (added) | 3 |

Roughly 195 new tests. Treat the counts as a guide, not a contract — a `[Theory]`
gaining a case changes them legitimately.

The load-bearing checks, the ones worth re-reading if anything downstream looks
wrong:

- `C64MemoryMapIoHandlerTests.RegisterIoHandler_WriteInRange_GoesToHandlerAndAlsoToRam`
  — writes under I/O must still reach RAM. Losing this silently breaks existing C64
  tests that rely on RAM under `$D000-$DFFF`.
- `UciRegistersDispatchTests.BusyState_IsHeldForTheConfiguredLatency` and
  `UciRegistersDispatchTests.MultiPartReply_StateIsDataMoreUntilFinalPart` — the two
  places the ported state machine is easiest to get subtly wrong.
- `UltimateFileSystemTests.ResolveToHostPath_TraversalAttempts_StayInsideTheRoot` —
  the trust boundary.
- `UciClientProgramTests.BrokenClient_WithNoBusyWait_ReadsNothingAtTheDefaultLatency`
  paired with `BrokenClient_PassesAtZeroLatency_WhichIsWhyTheDefaultIsNonZero` — the
  standing argument for the non-zero latency default. If someone later "simplifies"
  the latency away, these are what should stop them.

## Milestone 2 preview

Not in this plan. Recorded so the interfaces built here are not accidentally
narrowed:

- **REU** — `$DF00-$DF0A`, 16 MB, DMA engine, ported from
  `fpga/cart_slot/vhdl_source/reu.vhd` (483 lines). Registers via the same
  `IIOHandler` path Task 2 adds. `ControlTarget`'s `LOAD_REU`/`SAVE_REU` and
  `UltimateDosTarget`'s `$21`/`$22` stop returning their placeholder statuses.
- **Network target `$03`** — a new `ICommandTarget`, no protocol changes needed.
- **Drive emulation and mounting** — `MOUNT_DISK`/`UMOUNT_DISK`/`SWAP_DISK`.
- **`u64` hardware backend** — REST API (`machine:readmem/writemem/reset/pause/
  resume`, `runners:run_prg`, `drives/<n>:mount`) plus socket port 64 (`0xFF01` DMA,
  `0xFF02` DMARUN, `0xFF03` KEYB, `0xFF04` RESET, `0xFF06` DMAWRITE, `0xFF07`
  REU_WRITE, `0xFF09` DMAJUMP), and a resident 6502 stub for register readback and
  CIA-bracketed cycle counts. `python/sock.py` in the reference clone is the model
  for the socket half.
- **The differential check** — `example/ultimate.suite` run with `--backend u64`
  against physical hardware must produce identical results to `--backend u64sim`.
  Any divergence is a bug in `u64sim`, and that is the whole reason the simulator
  was built first.
