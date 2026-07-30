# Ultimate 64 Support — Milestone 1: `u64sim` (UCI core + Ultimate DOS)

Date: 2026-07-29
Status: Approved

## Context

sim6502 has four execution backends (`sim`, `vice`, `novavm`, `verilator`), none of
which can exercise Ultimate 64 hardware features. Code that talks to the Ultimate
Command Interface (UCI), the REU, or Ultimate DOS has no test path at all — it can
only be verified by hand on a physical machine.

The goal is two new backends:

- **`u64sim`** — a simulated Ultimate 64. sim6502's existing 6510 core plus C#
  implementations of the Ultimate's peripherals. Hermetic, fast, runs in CI.
- **`u64`** — a real Ultimate 64 over the network, using the firmware's REST API
  and socket API plus a resident 6502 stub.

`u64sim` comes first because it needs no hardware, runs in CI, and gives `u64` a
differential oracle. `u64` is Milestone 2.

"Everything the Ultimate 64 does" spans REU, the UCI core, and the DOS, network,
and control targets, plus drive emulation and cartridge behaviour. That is several
independent subsystems and will not fit one spec. This document covers **only** the
UCI core, the Ultimate DOS targets, and a minimal control target. REU, network
target, and drive emulation are later milestones on the same interfaces.

## Reference sources

Ported from `github.com/GideonZ/1541ultimate` (GPL-3.0):

| File | Lines | Role |
|---|---|---|
| `fpga/io/command_interface/vhdl_source/command_protocol.vhd` | 313 | C64-side UCI register state machine — the primary port |
| `software/io/command_interface/command_intf.cc` | 226 | Target dispatch, `copy_result`, canonical status strings |
| `software/io/command_interface/command_intf.h` | 143 | Bit constants, queue sizes, `CommandTarget` shape |
| `software/filemanager/dos.cc` / `dos.h` | 834 | Ultimate DOS target behaviour and command codes |
| `roms/c64rom/kernal/uci.s` | 737 | Reference 6502-side UCI client — model for the functional test program |

Command tables cross-checked against `github.com/GideonZ/1541u-documentation`
(`uci/core_uci_architecture.rst`, `uci/ultimate_dos_target.rst`,
`uci/control_target.rst`).

## Licence change

sim6502 relicenses from BSD-2-Clause to **GPL-3.0** so the above can be ported
directly. This decision was made explicitly and is understood to be one-way:
existing releases stay BSD-2 for anyone already consuming them, but every future
release binds downstream users to GPL-3 and its source-disclosure obligation.

Required as part of implementation:

- New `LICENSE` file (GPL-3.0 full text).
- README licence section rewritten.
- Attribution to Gideon Zweijtzer for the ported behaviour.
- Aaron Mell's BSD-2 notice for the 6502 simulator core **retained** — BSD-2 is
  GPL-compatible, so the combined work is clean, but the notice must survive.
- Per-file origin headers on every ported file naming the upstream file it derives
  from.

## Architecture

```
U64SimBackend
  └─ SimulatorBackend            (existing 6510 core — unchanged)
     └─ C64MemoryMap
        └─ IIOHandler dispatch   (interface exists today but is never used)
           └─ $DF1B-$DF1F ─> UciRegisters
                              ├─ command queue   896 bytes
                              ├─ response queue  896 bytes
                              ├─ status queue    256 bytes
                              ├─ state machine   IDLE / PROCESSING / DATA_LAST / DATA_MORE
                              └─ dispatch on (command[0] & $0F)
                                   $01 ─> UltimateDosTarget (instance 1) ─┐
                                   $02 ─> UltimateDosTarget (instance 2) ─┤
                                   $04 ─> ControlTarget                   │
                                                                          ▼
                                                    UltimateFileSystem ─> host dir as /Usb0
```

Each unit has one job and a narrow interface:

- **`UciRegisters`** owns the register-level protocol and nothing else. It knows
  about queues, status bits, and state transitions. It does not know what any
  command means.
- **`ICommandTarget`** is the seam. `ParseCommand(command) -> (reply, status,
  lastPart)`, `GetMoreData() -> (reply, status, lastPart)`, `Abort(offset)`. Mirrors
  Gideon's `CommandTarget` so `$03` network and the rest drop in later without
  touching the core.
- **`UltimateDosTarget`** interprets DOS command codes against an
  `UltimateFileSystem`. Two independent instances with separate state, matching
  targets `$01` and `$02`.
- **`UltimateFileSystem`** maps a host directory to the Ultimate's `/Usb0` root and
  is the only component that touches the real disk.

### Register map ($DF1B-$DF1F)

| Address | Read | Write |
|---|---|---|
| `$DF1B` | SoftwareIEC bus ID | — |
| `$DF1C` | Status | Control |
| `$DF1D` | — | Command data |
| `$DF1E` | Response data | — |
| `$DF1F` | Status data | — |

Control bits (write `$DF1C`): bit 0 `PUSH_CMD`, bit 1 `DATA_ACC`, bit 2 `ABORT`,
bit 3 `CLR_ERR`, bit 5 `IRQ`, bit 6 `TRIGGER`, bit 7 `DMA`.

Status bits (read `$DF1C`): bits 0-1 command status, bit 2 `ABORT_P`, bit 3 `ERROR`,
bits 4-5 state (`00` idle, `01` busy, `10` data-last, `11` data-more), bit 6
`STAT_AV`, bit 7 `DATA_AV`.

### Timing fidelity

The real UCI is asynchronous: the C64 writes `PUSH_CMD`, then polls `$DF1C` while
state is `PROCESSING`. If the simulator answered instantly, a client with a broken
or absent busy-wait loop would pass in `u64sim` and fail on hardware — the simulator
would be hiding exactly the class of bug it exists to catch.

`UciRegisters` therefore holds `PROCESSING` for a configurable number of CPU cycles,
non-zero by default, before making the response readable.

## Files

New — `sim6502/Systems/Ultimate/`:

- `UciRegisters.cs` — `IIOHandler` for `$DF1B-$DF1F`. Port of `command_protocol.vhd`.
- `ICommandTarget.cs` — the target seam.
- `UltimateDosTarget.cs` — targets `$01`/`$02`. Port of `dos.cc`.
- `ControlTarget.cs` — target `$04`. `IDENTIFY`, `REBOOT`, `GET_HWINFO` only.
  Enough to prove multi-target dispatch; `LOAD_REU`/`SAVE_REU` arrive with the REU
  milestone.
- `UltimateFileSystem.cs` — host directory as `/Usb0`, with a copy-on-write overlay.

New — `sim6502/Backend/`:

- `U64SimBackend.cs` — composes `SimulatorBackend` with the peripherals; implements
  host-side UCI command issuing.
- `U64SimBackendConfig.cs` — `FsRoot`, `UciLatencyCycles`, `DosVersionString`.

Modified:

- `sim6502/Systems/C64MemoryMap.cs` — I/O handler dispatch. Read path at line 91
  (currently `return _ioRegisters[address - 0xD000]`), write path at line 128. The
  write path **must still fall through to the RAM write at line 134** — writes under
  I/O always reach RAM on a C64, and losing that would be a silent regression in
  existing tests.
- `sim6502/Systems/IMemoryMap.cs` — add `RegisterIoHandler(int start, int end,
  IIOHandler handler)`.
- `sim6502/Systems/IIOHandler.cs` — unchanged; it is declared but unused today and
  is the intended extension point.
- `sim6502/Backend/BackendFactory.cs` — `"u64sim"` case in the switch at line 18.
- `sim6502/Sim6502CLI.cs` — backend name in the help text at line 92, plus
  `--u64sim-fs-root` and `--u64sim-uci-latency`.
- `sim6502/Grammar/sim6502.g4` — four DSL additions.
- `LICENSE`, `README.md` — licence change.

## DSL

Four new constructs. Deliberately no transaction log — assertions are on C64-side
results and on the status of host-issued commands.

```
suite("ultimate dos") {
  system(c64)
  ultimate(fs_root = "tests/fixtures/usb0")     ; exposed to the C64 as /Usb0

  test("dos-open-read", "read a file through the UCI DOS target") {
    uci($01, $11, "/Usb0/data")                 ; host-side CHANGE_DIR
    assert(uci_status("00,OK"), "chdir succeeded")

    ; the code under test drives $DF1C-$DF1F itself
    jsr([load_via_uci], stop_on_rts = true, fail_on_brk = true)
    assert(peekbyte($c000) == $42, "first byte arrived")
  }
}
```

- `ultimate(fs_root = "...")` — suite-level, alongside `system(...)`.
- `uci(target, command, args...)` — issue a UCI command from the host. Args are
  expressions or string literals; strings are appended as raw bytes, matching the
  wire format.
- `uci_status("00,OK")` — predicate; true when the last `uci(...)` call's status
  string matches. A predicate rather than a string-returning accessor, because the
  DSL's comparison machinery is int- and bool-valued only (`_intValues` /
  `_boolValues` in `SimBaseListener`). Returning a string would mean a third value
  type and a new comparison LHS; a predicate reuses the existing `boolFunction`
  path exactly as `screen_contains(...)` does. The failure message reports the
  actual status string, so diagnosability is unchanged.
- `uci_data(n)` — byte `n` of the response data from the last `uci(...)` call.

Grammar work: keywords `ultimate`, `fs_root`, `uci`, `uci_status`, `uci_data`; an
`ultimateDeclaration` rule in `suite`; a `uciFunction` in `setupContents` and
`testContents`; `uci_data` into `intFunction`, `uci_status` into `boolFunction`.

## Error handling

- Unknown target → reply `NO TARGET`, status `21,UNKNOWN COMMAND` — byte-identical
  to `command_intf.cc` lines 223-226.
- Unknown command on a known target → same status, empty reply.
- Command longer than 896 bytes → the command pointer **saturates** at the buffer
  end and further bytes overwrite the last cell. No error is raised. This is what
  `command_protocol.vhd` line 145 actually does (`if command_pointer /=
  c_cmd_if_command_buffer_end then command_pointer <= command_pointer + 1`) and the
  port matches it rather than inventing a friendlier behaviour that hardware would
  not reproduce.
- `PUSH_CMD` while the state is not idle → set `ERROR` (status bit 3). This is the
  only path that raises `ERROR`, and it is cleared only by `CLR_ERR`. A reply larger
  than the 896-byte response queue or a status larger than the 256-byte status queue
  is truncated with a logged warning.
- `ABORT` mid-transfer → target's `Abort(offset)` is called with bytes already
  consumed, then state resets to idle, matching `command_intf.cc` lines 121-129.
- DOS errors use Gideon's exact strings: `83,NO SUCH DIRECTORY`,
  `84,NO FILE TO CLOSE`, `85,NO FILE OPEN`, `88,FILE NOT FOUND`,
  `89,NOT A DISK IMAGE`, `90,DRIVE NOT PRESENT`.
- **Path traversal is a trust boundary.** Any path that resolves outside `fs_root`
  — `..` segments, absolute paths, symlinks — is rejected as
  `83,NO SUCH DIRECTORY`. The real path is never opened. This is enforced in
  `UltimateFileSystem` by resolving to a canonical absolute path and confirming it
  is under the canonical root, not by string inspection of the input.
- The fixture tree is copied to a temporary directory at construction and the copy
  is deleted on dispose, so fixtures stay pristine and reruns are repeatable. A
  straight copy rather than a copy-on-write overlay: fixture trees are small, and
  the overlay would be more machinery for the same guarantee.

## Testing

Unit — `sim6502tests/Systems/Ultimate/`:

- `UciRegisters` state machine driven directly, no 6502: full command/response
  round trip, `DATA_MORE` chaining across multiple `DATA_ACC` cycles, queue
  overflow sets `ERROR`, `ABORT` mid-transfer, `PROCESSING` held for the configured
  cycle count.
- `UltimateDosTarget` per command code: `IDENTIFY`, `OPEN_FILE` with each attribute
  flag, `READ_DATA` including the 512-byte chunk boundary, `WRITE_DATA`,
  `FILE_SEEK`, `FILE_INFO`, `FILE_STAT`, `CHANGE_DIR` with `.` and `..`, `GET_PATH`,
  `OPEN_DIR`/`READ_DIR`, `DELETE_FILE`, `CREATE_DIR`, `ECHO`.
- `UltimateFileSystem` path traversal: `..`, absolute paths, and symlinks all
  rejected without touching the target.
- `C64MemoryMap` regression: a write to a handled I/O address still reaches RAM
  underneath.

Functional — `sim6502tests/TestPrograms/`:

- A 6502 UCI client (`IDENTIFY`, then `OPEN_FILE`/`READ_DATA`/`CLOSE_FILE`),
  modelled on `roms/c64rom/kernal/uci.s`, exercised through the whole stack under
  the `u64sim` backend against a fixture directory.

End-to-end:

```
dotnet test                                                    # unit + functional
dotnet run --project sim6502 -- --suitefile example/ultimate.suite \
  --backend u64sim --u64sim-fs-root tests/fixtures/usb0
```

Both must be green, and the `sim` / `vice` / `novavm` / `verilator` suites must
stay green — the `C64MemoryMap` change is on a path every C64 test uses.

Milestone 2 adds the differential check: the same `.suite` file run with
`--backend u64` against physical hardware must produce identical results. Any
divergence is a bug in `u64sim`.

## Out of scope for this milestone

REU (`$DF00-$DF0A`, 16 MB, DMA engine), network target `$03`, the control target's
REU and EasyFlash commands, drive emulation and disk mounting, the `u64` hardware
backend, and the resident 6502 stub that backend needs for register and cycle
readback.
