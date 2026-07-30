# u64sim Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `u64sim` execution backend that runs 6502 code against a simulated Ultimate 64 — the Ultimate Command Interface register block and the Ultimate DOS targets — so code that talks to the Ultimate can be unit tested with no hardware.

**Architecture:** sim6502's existing 6510 core and `C64MemoryMap` are reused unchanged except for adding I/O handler dispatch. A new `UciRegisters` class implements the C64-side protocol at `$DF1B-$DF1F` (ported from Gideon Zweijtzer's `command_protocol.vhd`) and dispatches parsed commands to `ICommandTarget` implementations. `UltimateDosTarget` (ported from `dos.cc`) serves targets `$01`/`$02` against a host directory exposed as `/Usb0`.

**Tech Stack:** C# / .NET 10, xunit + FluentAssertions, ANTLR 4.13.1 (regenerate with `make grammar`), NLog.

## Global Constraints

- **Licence: GPL-3.0.** sim6502 relicenses from BSD-2-Clause. Every file ported from `1541ultimate` carries an origin header naming the upstream file. Aaron Mell's BSD-2 notice for the simulator core is retained verbatim.
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

## Task 1: Relicence to GPL-3.0

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
        var map = new GenericMemoryMap();
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

## Remaining tasks

Tasks 9-14 are listed with their file sets, interfaces, and the behaviour each
must pin, pending expansion to full step-by-step form.

### Task 9: `UltimateDosTarget` — info, stat, delete, rename, copy, directory listing

**Files:** modify `UltimateDosTarget.cs`; test
`sim6502tests/Systems/Ultimate/UltimateDosTargetInfoTests.cs`.

Commands: `FILE_INFO 0x07`, `FILE_STAT 0x08`, `DELETE_FILE 0x09`,
`RENAME_FILE 0x0A` (old name at byte 2, new name after its NUL),
`COPY_FILE 0x0B` (same layout), `OPEN_DIR 0x13`, `READ_DIR 0x14`.

`FILE_INFO`/`FILE_STAT` reply layout, packed little-endian, total
`12 + strlen(filename)` bytes with no terminator: size `uint32`, date `uint16`,
time `uint16`, extension `char[3]` space-padded, attribute `uint8`, filename.
FAT encoding: `date = ((year - 1980) << 9) | (month << 5) | day`,
`time = (hour << 11) | (minute << 5) | (second / 2)`.

`READ_DIR` yields one entry per part — byte 0 the attribute, then the name with no
terminator — `LastPart` on the final entry with status `"00,OK"`, status `""`
before that. `OPEN_DIR` answers `"01,DIRECTORY EMPTY"` for an empty directory and
`"86,CAN'T READ DIRECTORY"` on failure.

Out of scope, and must answer `"99,FUNCTION NOT IMPLEMENTED"` rather than falling
through to unknown-command: `COPY_UI_PATH 0x15`, `COPY_HOME_PATH 0x17`,
`LOAD_REU 0x21`, `SAVE_REU 0x22`, `MOUNT_DISK 0x23`, `UMOUNT_DISK 0x24`,
`SWAP_DISK 0x25`, `GET_TIME 0x26`, `SET_TIME 0x27`. A test must assert this, so
the gap is explicit rather than silent.

### Task 10: `ControlTarget`

**Files:** create `sim6502/Systems/Ultimate/ControlTarget.cs`; test
`sim6502tests/Systems/Ultimate/ControlTargetTests.cs`.

Target `$04`. `IDENTIFY 0x01` → `"CONTROL TARGET V1.1"`; `REBOOT 0x06` →
no-reply, resets the DOS targets' state; `GET_HWINFO 0x28` → configurable model
name, default `"Ultimate 64"`. `LOAD_REU 0x08` and `SAVE_REU 0x09` answer
`"84,REU NOT ENABLED"` until the REU milestone lands.

### Task 11: `U64SimBackend`, config, factory, CLI

**Files:** create `sim6502/Backend/U64SimBackendConfig.cs` and
`sim6502/Backend/U64SimBackend.cs`; modify `sim6502/Backend/BackendFactory.cs:18`
and `sim6502/Sim6502CLI.cs:92`; test
`sim6502tests/Backend/U64SimBackendTests.cs` and add cases to
`sim6502tests/Backend/BackendFactoryTests.cs`.

`U64SimBackendConfig`: `string FsRoot`, `int UciLatencyCycles = 64`,
`string DosVersion = "ULTIMATE-II DOS V1.2"`, `string ModelName = "Ultimate 64"`.

`U64SimBackend : IExecutionBackend` delegates every member to an inner
`SimulatorBackend`, and additionally exposes
`(string Status, byte[] Data) IssueUciCommand(byte[] command)`. Construction wires
`UciRegisters.CycleCounter` to the processor's cycle count, sets
`ServiceEnabled = true`, registers targets 1, 2, and 4, and calls
`memoryMap.RegisterIoHandler(0xDF1B, 0xDF1F, uci)`.

`BackendFactory` gains a `u64sim` case with a `U64SimBackendConfig?` parameter and
must reject a non-`C64MemoryMap` map with a message naming `system(c64)`.

CLI: add `u64sim` to the `--backend` help text and add `--u64sim-fs-root` and
`--u64sim-uci-latency` (default 64).

### Task 12: Grammar and listener — `ultimate()`, `uci()`, `uci_status()`, `uci_data()`

**Files:** modify `sim6502/Grammar/sim6502.g4` and
`sim6502/Grammar/SimBaseListener.cs`; regenerate with `make grammar` and commit
`sim6502/Grammar/Generated/`; test
`sim6502tests/GrammarTests/UltimateGrammarTests.cs` and
`sim6502tests/Backend/U64SimListenerTests.cs`.

Grammar: `ultimateDeclaration : Ultimate LParen FsRoot Assign StringLiteral RParen`
added to `suite` after the system/processor declaration;
`uciFunction : Uci LParen expression Comma expression (Comma uciArg)* RParen` with
`uciArg : expression | StringLiteral`, added to both `testContents` and
`setupContents`; `uciDataFunction : UciData LParen expression RParen` added to
`intFunction`; `uciStatusFunction : UciStatus LParen StringLiteral RParen` added to
`boolFunction` and to `comparison` as `# uciStatusCheck`. New tokens `Ultimate`,
`FsRoot`, `Uci`, `UciStatus`, `UciData` placed with the other keywords, before
`Identifier`, with `UciStatus` and `UciData` ahead of `Uci` for readability.

Listener: `EnterSuite` reads `ultimateDeclaration` and overrides
`U64SimConfig.FsRoot` **before** `BackendFactory.Create` on line 391;
`RequireU64SimBackend(string command)` mirrors `RequireHighLevelBackend`;
`ExitUciFunction` assembles the command bytes (target, command, then each arg —
string literals as ASCII bytes, expressions as single bytes) and stores the result;
`ExitUciStatusCheck` and `ExitUciStatusFunctionValue` compare against the stored
status and report the actual string on failure; `ExitUciDataFunction` returns the
indexed response byte, and out-of-range indices fail the assertion rather than
throwing.

Note in the commit message that `uci` becomes a reserved word, so any existing
suite using it as a symbol name needs renaming.

### Task 13: Functional test — 6502 UCI client through the whole stack

**Files:** create `sim6502tests/Systems/Ultimate/UciClientProgramTests.cs`,
`example/ultimate.suite`, and the fixture tree under
`sim6502tests/Fixtures/usb0/`.

The 6502 client is a documented byte array in the test, not an assembled file —
there is no assembler in CI. The program, assembled by hand at `$C200`, performs
IDENTIFY against DOS target `$01`, copies the response to `$C000`, and stores the
length at `$C0FF`:

```
        CONTROL = $DF1C   COMMAND = $DF1D   RESULT = $DF1E

C200  A9 08        lda #$08          ; clear any error
C202  8D 1C DF     sta CONTROL
C205  A9 01        lda #$01          ; target 1 = Ultimate DOS
C207  8D 1D DF     sta COMMAND
C20A  A9 01        lda #$01          ; DOS_CMD_IDENTIFY
C20C  8D 1D DF     sta COMMAND
C20F  A9 01        lda #$01          ; CMD_PUSH_CMD
C211  8D 1C DF     sta CONTROL
C214  AD 1C DF     lda CONTROL       ; wait: poll while state == Busy
C217  29 30        and #$30
C219  C9 10        cmp #$10
C21B  F0 F7        beq $C214
C21D  A2 00        ldx #$00
C21F  AD 1C DF     lda CONTROL       ; rdloop: bit 7 = response available
C222  10 09        bpl $C22D
C224  AD 1E DF     lda RESULT
C227  9D 00 C0     sta $C000,x
C22A  E8           inx
C22B  D0 F2        bne $C21F
C22D  8E FF C0     stx $C0FF         ; done
C230  A9 02        lda #$02          ; CMD_NEXT_DATA
C232  8D 1C DF     sta CONTROL
C235  AD 1C DF     lda CONTROL       ; ack: wait for the Ultimate to clear bit 1
C238  29 02        and #$02
C23A  D0 F9        bne $C235
C23C  60           rts
```

61 bytes:
`A9 08 8D 1C DF A9 01 8D 1D DF A9 01 8D 1D DF A9 01 8D 1C DF AD 1C DF 29 30 C9 10 F0 F7 A2 00 AD 1C DF 10 09 AD 1E DF 9D 00 C0 E8 D0 F2 8E FF C0 A9 02 8D 1C DF AD 1C DF 29 02 D0 F9 60`

The test loads it at `$C200`, runs `ExecuteJsr(0xC200, 0, stopOnRts: true,
failOnBrk: true)`, and asserts `$C0FF == 20` and `$C000..$C013 ==
"ULTIMATE-II DOS V1.2"`. Because the CPU really executes the polling loop, this is
also the end-to-end proof that the Busy latency is survivable by a correct client
— run it with the default latency of 64, not 0.

A second test in the same file runs the identical program with a deliberately
broken client (the `wait` loop replaced by two `nop`s) and asserts it reads
nothing, proving the latency actually catches the missing busy-wait.

`example/ultimate.suite` exercises the DSL host-side path with no assembly:
`ultimate(fs_root = ...)`, a `uci($01, $01)` IDENTIFY with
`assert(uci_status("00,OK"), ...)` and `assert(uci_data(0) == $55, ...)`, and a
`uci($01, $11, "/Usb0/data")` CHANGE_DIR. Add it to
`sim6502tests/Backend/IntegrationSuiteParseTests.cs` so it is at minimum
parse-checked in CI.

### Task 14: Documentation

**Files:** modify `README.md` (backend table near line 20, plus a new Ultimate 64
section), `CHANGELOG.md`, `sim6502/sim6502.csproj` (version to `4.0.0`).

The backend table gains `u64sim` — "Ultimate 64 feature tests against a simulated
UCI and Ultimate DOS". Document the DSL additions, the `--u64sim-*` flags, the
`system(c64)` requirement, the UCI latency knob and why it defaults non-zero, and
that REU, the network target, drive mounting, and the real-hardware `u64` backend
are the next milestone. Version bumps to `4.0.0` because the licence change is
breaking.

---

## Verification

After Task 14:

```bash
make grammar
dotnet build -c Release
dotnet test -c Release
dotnet run --project sim6502 -- --suitefile example/ultimate.suite \
  --backend u64sim --u64sim-fs-root sim6502tests/Fixtures/usb0
```

All four must succeed. The existing `sim`, `vice`, `novavm`, and `verilator`
suites must stay green throughout — Task 2 touches the C64 read and write paths
that every C64 test uses, so a regression there would surface immediately.

Milestone 2 adds the differential check: the same suite run with `--backend u64`
against physical hardware must produce identical results.
