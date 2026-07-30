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

## Remaining tasks

Tasks 6-14 follow the same shape. They are listed here with their file sets,
interfaces, and the behaviour each must pin, and will be expanded to full
step-by-step form before execution reaches them.

### Task 6: `UltimateFileSystem`

**Files:** create `sim6502/Systems/Ultimate/UltimateFileSystem.cs`; test
`sim6502tests/Systems/Ultimate/UltimateFileSystemTests.cs`.

**Produces:** `sealed class UltimateFileSystem : IDisposable` with
`UltimateFileSystem(string hostRoot, string mountName = "Usb0")`,
`string CurrentPath { get; }`, `bool ChangeDirectory(string path)`,
`string? ResolveToHostPath(string ultimatePath)`,
`IReadOnlyList<UltimateDirEntry> ListCurrentDirectory()`, and
`readonly record struct UltimateDirEntry(string Name, byte Attributes, long Size, DateTime Modified)`.

Must pin: the fixture tree is copied to a temp directory at construction and
deleted on dispose, so tests never mutate fixtures; symlinks are **not** copied
(removing the symlink escape surface entirely) and a warning is logged for each
one skipped; `ResolveToHostPath` returns null for any path that escapes the root,
verified by canonicalising with `Path.GetFullPath` and checking the root prefix,
never by inspecting the input string; `..` at the root is a no-op rather than an
escape; `CurrentPath` starts at `/Usb0`; directory attribute is `0x10`, file
attribute `0x20`.

### Task 7: `UltimateDosTarget` — identity, navigation, echo

**Files:** create `sim6502/Systems/Ultimate/UltimateDosTarget.cs`; test
`sim6502tests/Systems/Ultimate/UltimateDosTargetNavigationTests.cs`.

**Produces:** `sealed class UltimateDosTarget : ICommandTarget` with
`UltimateDosTarget(UltimateFileSystem fs, string version = "ULTIMATE-II DOS V1.2")`.

Commands: `IDENTIFY 0x01`, `CHANGE_DIR 0x11`, `GET_PATH 0x12`,
`CREATE_DIR 0x16`, `ECHO 0xF0`, plus the unknown-command fallback.
Status strings verbatim: `"00,OK"`, `"83,NO SUCH DIRECTORY"`,
`"21,UNKNOWN COMMAND"`. `CHANGE_DIR` restores the previous path on failure.
`ECHO` replies with the whole command including bytes 0 and 1.

### Task 8: `UltimateDosTarget` — file open, read, write, seek, close

**Files:** modify `UltimateDosTarget.cs`; test
`sim6502tests/Systems/Ultimate/UltimateDosTargetFileTests.cs`.

Commands: `OPEN_FILE 0x02` (attribute at byte 2, filename from byte 3;
`FA_READ 0x01`, `FA_WRITE 0x02`, `FA_CREATE_NEW 0x04`, `FA_CREATE_ALWAYS 0x08`),
`CLOSE_FILE 0x03`, `READ_DATA 0x04` (length little-endian at bytes 2-3, 512-byte
chunks via `GetMoreData`, `LastPart` when a short read occurs or `remaining`
reaches zero, status `""` on every successful chunk), `WRITE_DATA 0x05` (payload
from byte 4; bytes 2 and 3 are dummies), `FILE_SEEK 0x06` (little-endian 32-bit
position at bytes 2-5).

Deliberate deviation to record in the code: `dos.cc get_more_data` assigns
`*status` only on the success path, leaving it unset when a read fails. That is an
uninitialised-pointer read upstream; the port assigns the error status explicitly.

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
