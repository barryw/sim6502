# Ultimate 64 Support — Milestone 2: the `u64` hardware backend

Date: 2026-07-30
Status: Approved

## Context

Milestone 1 delivered `u64sim`: a simulated Ultimate 64 with the UCI core at
`$DF1B-$DF1F`, two Ultimate DOS targets, and a control target. It is hermetic and
runs in CI, but every behaviour it models was verified against upstream *source*,
never against a real machine.

This milestone delivers `u64` — a backend that talks to a physical Ultimate 64 over
the network — so that `u64sim` can be checked against silicon. That differential
check is the whole reason `u64sim` was built first.

The backend is scoped as a **differential instrument**, not as a general-purpose
replacement for `sim` or `vice`. Its acceptance target is `example/ultimate.suite`,
which uses only `uci()`, `uci_status()`, `uci_data()`, `system(c64)` and
`ultimate(...)` — no `jsr()`, and no register, flag, cycle, or memory assertions.
That scope is what keeps the design small.

Milestone 2's original preview also bundled REU emulation and drive mounting. Those
are independent subsystems of comparable size to Milestone 1 and are **not** in this
spec; each becomes its own milestone, and each then inherits the differential check
built here as its acceptance test.

## What was established on real hardware

Everything in this section was measured during design against an Ultimate 64 Elite
(fw 3.14d, fpga 122, core 1.49). It is recorded here because it is load-bearing and
expensive to rediscover.

### The DMA route works — no resident 6502 stub is needed

The original plan assumed register readback and command issue would require a
resident 6502 stub triggered through the KERNAL keyboard buffer. That is
unnecessary for this milestone. `machine:readmem` and `machine:writemem` are
implemented as DMA cycles on the cartridge bus (`C64_DMA_RAW_WRITE` in
`software/api/route_machine.cc`), so they reach the UCI registers directly.

A full DOS `IDENTIFY` was driven end to end from the host with no 6502 code at all:

```
idle                    DF1B..DF1F = 0b 00 c9 00 00
PUT writemem df1d=01    command byte 0 (target $01)
PUT writemem df1d=01    command byte 1 (DOS IDENTIFY)
PUT writemem df1c=01    ControlPushCommand
readmem  df1c           E0 = RespAvail | StatusAvail | StateDataLast
readmem  df1e (xN)      "ULTIMATE-II DOS V1.2"
readmem  df1f (xN)      "00,OK"
PUT writemem df1c=02    ControlDataAccept
readmem  df1c           00 — clean idle
```

Note: this trace is a verbatim record of what was actually sent and is left as
measured. The implementation now writes `$09` (`ControlPushCommand |
ControlClearError`) rather than the `$01` shown above, so the error latch is
cleared in the same write that pushes the command — a set error bit on the
next read then unambiguously means *this* push was rejected, not a stale
latch from an earlier one. The combined write relies on upstream
`command_protocol.vhd` evaluating its clear-error clause before its
push-command clause (verified against upstream master). `$09` has **not**
been exercised on silicon yet — a hardware smoke test should confirm it
before relying on it in the field.

The FPGA treats a DMA write into the `$DF1D` command FIFO exactly like a CPU write,
and a DMA read pops the response and status FIFOs exactly like a CPU read.
`U64Backend.IssueUciCommand` is therefore a near-mirror of
`UciRegisters.IssueHostCommand`.

### Two traps that shape the implementation

1. **`$DF1D` is a FIFO port, but `writemem` writes an ascending span.** Writing two
   bytes at `$DF1D` would hit `$DF1D` *and* `$DF1E`. Every command byte needs its
   own single-byte request, and every reply byte its own single-byte read.
2. **Never poll status with a span covering `$DF1E`/`$DF1F`.** The read pops those
   FIFOs and silently eats the reply. Poll `$DF1C` with `length=1` only.

### The availability bit never clears — confirmed on silicon

`$DF1C` held `E0` across every read of a pending reply. This is exactly the upstream
wart Milestone 1 pinned by test and documented on `UciRegisters`: a reply exactly
filling its queue leaves the availability bit permanently set, so clients must count
expected bytes rather than read until the bit clears. All drains must be bounded.

### REST facts, confirmed from firmware source

| Endpoint | Semantics |
|---|---|
| `PUT /v1/machine:writemem?address=&data=` | hex param, **hard max 128 bytes**, DMA raw write, ascending span |
| `POST /v1/machine:writemem?address=` | binary attachment body, up to 65536 bytes — the bulk path |
| `GET /v1/machine:readmem?address=&length=` | DMA read on the cartridge bus, default length 256 |
| `PUT /v1/machine:reset` | C64 reset |

Measured latency: **51 ms per serialized round-trip**. One round-trip per UCI byte
puts `example/ultimate.suite` at roughly 30–40 s end to end, which is acceptable for
a validation run. Bulk transfer is the weak spot (a 512-byte DOS chunk would be
~26 s); the resident-stub route remains the documented future optimisation if bulk
throughput ever matters.

**Requests must be strictly serialized.** Concurrent requests can lock the machine
up. The backend enforces this itself rather than trusting callers.

### File provisioning is FTP, not REST

REST has no arbitrary-file-write endpoint — `route_files.cc` offers only
`files:info` and `create_d64/d71/d81/dnp`, and Gideon's published API reference
agrees. The Ultimate runs an **FTP server on port 21**, which is the fixture upload
path:

```
curl --ftp-create-dirs -T local.txt ftp://<host>/USB1/data/hello.txt
```

The real stick mounts as `/USB1`; `u64sim` hardcodes `/Usb0`. See "In-scope
corrections" below.

**Fixture provisioning is not backend functionality.** The backend never uploads
files. Provisioning the fixtures onto the machine is a documented operator step,
scripted as a `make` target, run once before a hardware differential run. Combined
with the configurable mount name (correction 3), this lets a single unmodified suite
file run against both backends: `u64sim` maps its mount to the local fixture
directory, and the same relative paths exist on the machine's stick.

## Divergences found

The differential check earned its keep before a line of backend code was written.

### 1. `OPEN_FILE` on a missing file — a Milestone 1 porting defect

`u64sim` returns `"82,FILE NOT FOUND"`. Real hardware returns `"FILE DOESN'T
EXIST"`, and upstream agrees with hardware:

- `software/filemanager/dos.cc:111-124` — `DOS_CMD_OPEN_FILE` returns
  `FileSystem::get_error_string(res)` on failure.
- `software/filesystem/file_system.cc:40` — `FR_NO_FILE` → `"FILE DOESN'T EXIST"`.
- `c_status_file_not_found` (`"82,FILE NOT FOUND"`) *is* used upstream, but only at
  `dos.cc:164, 189, 337, 596` — never for `OPEN_FILE`.

`UltimateDosTarget.cs:259-260` records the Milestone 1 decision that caused this:

> "Upstream surfaces FatFs error text here. Porting that table buys no test value,
> so failures map onto the documented DOS statuses."

Hardware falsified that judgement. Fixing it is in scope (below).

### 2. `LOAD_REU` wedges the real UCI — an upstream firmware bug

`uci($04, $08, "image.reu")` never completes on fw 3.14d. Status sticks at `$11`
(Busy | NewCommandSet) and remains wedged at `$15` afterwards. This is not a client
timeout artefact — it was reproduced under four conditions, each with a 30 s
BUSY-aware budget:

| REU setting | image file | filename offset | Result |
|---|---|---|---|
| Enabled, 16 MB | absent | 2 | hangs |
| Disabled | absent | 2 | hangs |
| Enabled, 16 MB | present | 2 | hangs |
| Enabled, 16 MB | present | 4 (correct framing) | hangs |

`Abort` + `ClearError` + `DataAccept` + strobes-low clears the error bit (`$1D` →
`$15`) but never releases Busy. **Only a power cycle recovers it**, and the wedge
takes down the whole command interface for every target, not just the control target.

Root cause, in `control_target.cc:296-325` (identical in every clone checked, at
`APPL_VERSION_NUMBER` 3.14e):

```c
retVal = reu_preloader->LoadREU((char *)data_message.message + 4);
```

The filename is read from `data_message` — the *reply* buffer, allocated separately
at `control_target.cc:42` — not from `command->message`. `parse_command()` has no
prologue copying the command into it, so `LoadREU()` receives uninitialised heap on
the first control-target command after boot, or the previous command's leftover
reply thereafter. The same file uses the correct idiom elsewhere
(`control_target.cc:466`: `fn = (const char *)command->message + 2;`).

Reported upstream as
[GideonZ/1541ultimate#740](https://github.com/GideonZ/1541ultimate/issues/740).

Consequences:

- `control-reu-absent` is excluded from the hardware differential run. It cannot be
  verified on silicon at all, because the firmware path that would produce
  `"84,REU NOT ENABLED"` is unreachable — even with the REU explicitly disabled.
- **`u64sim` is more correct than the shipped firmware here.** It returns
  `"84,REU NOT ENABLED"` promptly, which is what upstream's own status table
  specifies. Keep that behaviour; this is a firmware defect, not a `u64sim` one.
- The forthcoming REU milestone must treat `LOAD_REU`/`SAVE_REU` status strings as
  verified against source only, with no path to silicon confirmation until the
  firmware is fixed.

Note for that milestone: `SOCKET_CMD_REUWRITE` (`$FF07`) on TCP port 64 writes
directly into `REU_MEMORY_BASE` (24-bit LE offset, then payload), bypassing the UCI
entirely. It is a viable way to load REU contents for testing, but it produces no UCI
status and so cannot substitute for the `LOAD_REU` path in a differential check. The
socket requires `SOCKET_CMD_AUTHENTICATE` (`$FF1F`) first; authentication succeeds
trivially when no network password is configured.

## Architecture

Two narrow seams, both mirroring patterns already in the codebase.

```csharp
// What uci() actually needs. U64SimBackend already has this exact method,
// so it satisfies the interface without a behaviour change.
public interface IUltimateBackend
{
    (string Status, byte[] Data) IssueUciCommand(byte[] command);
}

// Transport seam, mirroring the existing IViceConnection / INovaVmConnection.
public interface IU64Connection : IDisposable
{
    byte ReadByte(int address);
    void WriteByte(int address, byte value);
    byte[] ReadBytes(int address, int length);
    void WriteBytes(int address, byte[] data);
}
```

`SimBaseListener.RequireU64SimBackend("uci()")` becomes `RequireUltimateBackend(...)`
returning `IUltimateBackend`, so both backends drive the same DSL functions.

`U64Backend` implements `IExecutionBackend` directly, as `ViceBackend` does.
`IMemoryMap`/`IIOHandler` do not apply — the machine is reached over the wire, not
through a modelled memory map.

### Supported and unsupported members

`ViceBackend` gets registers, breakpoints and cycle counts nearly free because VICE's
MCP server exposes them. The U64 REST API exposes none of that, so:

| Member | Behaviour |
|---|---|
| `ReadByte`, `ReadWord`, `WriteByte`, `WriteWord`, `WriteMemoryValue` | Real, via DMA `readmem`/`writemem` — **but see the banking caveat below** |
| `LoadBinary` | Real, via `POST machine:writemem` (bulk) |
| `Reset` | Real, via `PUT machine:reset` |
| `IssueUciCommand` | Real — the point of the backend |
| `GetRegister`, `SetRegister`, `GetFlag`, `SetFlag` | `NotSupportedException` |
| `ExecuteJsr`, `GetCycles` | `NotSupportedException` |
| `SaveSnapshot`, `RestoreSnapshot` | `NotSupportedException` |
| `SetWarpMode`, `LoadSymbols`, `ResetCycleCount` | Logged no-op — suites set these incidentally |
| `TraceEnabled`, `ClearTraceBuffer`, `GetTraceBuffer` | Trace unsupported; getter false, buffer empty |

Every `NotSupportedException` names the reason and the alternative (`uci()`, or the
resident-stub milestone), rather than failing blankly.

`ResetCycleCount` is a no-op rather than a throw even though `GetCycles` still
throws: `SimBaseListener.ResetTest()` calls `ResetCycleCount()` unconditionally
before *every* test on every non-`vice` backend, so a throw there kills the run
before a single test executes — the suite never even reaches the `test()` body
that would call `GetCycles()`. `GetCycles` is only ever reached by a suite that
deliberately asserts on a cycle count, so it can and should still fail loudly
and name `--backend u64sim` as the alternative.

### Memory access is not equivalent to `sim` — banking caveat

`readmem`/`writemem` perform DMA **through the PLA**, so they see whatever the C64's
current banking exposes, not raw RAM:

- `$D000-$DFFF` reaches **I/O**, not the RAM underneath. This is precisely why DMA
  writes to `$DF1D` drive the UCI at all, so the backend depends on it — but it also
  means a suite asserting RAM under I/O would silently read chip registers.
- `$A000-$BFFF` and `$E000-$FFFF` return **ROM** when banked in. Verified: reading
  `$E000` returns `85 56 20 0F BC …`, which is KERNAL ROM.

Upstream issue
[#674](https://github.com/GideonZ/1541ultimate/issues/674) added a `ramonly`
parameter to bypass this via the firmware's existing `C64_DMA_MEMONLY` register. It
is **closed as completed but not present in fw 3.14d** — the machine answers
`"Function readmem does not have parameter ramonly"` — and `route_machine.cc` in the
3.14e source does not expose it either.

Consequences for this milestone: none, because the acceptance suite performs no
memory assertions. But the divergence is real and must be documented on the backend,
and any future milestone that adds memory assertions against `u64` has to either
require a firmware with `ramonly` or restrict assertions to `$0000-$9FFF` /
`$C000-$CFFF`.

## Files

New, under `sim6502/Backend/`:

- `U64BackendConfig.cs` — `Host`, `Port` (80), `HttpTimeoutMs` (per request, exposed
  as `--u64-timeout`), `CommandBudgetMs` (how long a single UCI command may stay
  BUSY before the transaction gives up and recovers; these are deliberately separate,
  because a command can be legitimately busy far longer than one HTTP round-trip)
- `IU64Connection.cs` — transport seam
- `U64RestConnection.cs` — `HttpClient`, **serialization enforced internally**
- `U64Backend.cs` — `IExecutionBackend` + `IUltimateBackend`, and the UCI transaction

Modified:

- `BackendFactory.cs` — `case "u64"`
- `Sim6502CLI.cs` — `--u64-host`, `--u64-timeout`
- `SimBaseListener.cs` — `RequireUltimateBackend` returning `IUltimateBackend`
- `U64SimBackend.cs` — declare `IUltimateBackend`

If `U64Backend.cs` passes roughly 400 lines, the UCI transaction splits into its own
type. It is not split pre-emptively.

## The UCI transaction

```
IssueUciCommand(cmd):
  (connection serializes internally)
  try:
    foreach b in cmd: WriteByte($DF1D, b)      // one request per byte — FIFO port
    WriteByte($DF1C, ControlPushCommand | ControlClearError)
      // combined write clears any stale error latch first, so a set error bit
      // in the very next read unambiguously means *this* push was rejected —
      // ClearError's clause runs before PushCommand's in command_protocol.vhd

    wait:  ReadByte($DF1C) only, never a span
           StateBusy => keep waiting, not a wall-clock race
           until RespAvail | StatusAvail, or budget exhausted

    drain: while RespAvail   && n < cap: ReadByte($DF1E)
           while StatusAvail && n < cap: ReadByte($DF1F)
           StateDataMore => DataAccept, continue for the next part
  finally:
    WriteByte($DF1C, ControlDataAccept)
    on failure: Abort, ClearError, DataAccept, strobes low
```

Two rules are load-bearing and were learned by wedging real hardware:

- **`StateBusy` means keep waiting.** The first prototype polled for 2.5 s, gave up
  while `LOAD_REU` was legitimately busy, and returned without acknowledging or
  aborting — leaving the Ultimate mid-transaction and requiring a power cycle.
  `u64sim` never exposes this because `IssueHostCommand` completes synchronously.
- **Every exit path cleans up**, including exceptions and timeouts.

Drains are bounded because the availability bit never clears.

## Error handling

- Timeout throws with the last status byte included.
- If recovery leaves Busy set, the exception says so and states that a power cycle
  is required. This is a real, reachable condition — `LOAD_REU` reaches it.
- HTTP failures are wrapped with the endpoint and address that failed.
- Unlike `ViceBackend.Connect`, `U64Backend` connects lazily: construction does
  no network I/O, and an unreachable host only fails on the first command,
  wrapped with the configured host in the message. This is deliberate, not an
  oversight -- `U64ListenerTests` depends on constructing a `U64Backend`
  (including its real `U64RestConnection`) doing no I/O, because tests must
  never touch the network. An eager construction-time probe would break that
  guarantee for a diagnostic that a real run surfaces within one command
  anyway.

## Testing

**Hermetic** (runs in CI, no hardware):

- `FakeU64Connection` implements `IU64Connection` backed by `u64sim`'s own
  `UciRegisters`. This yields a high-fidelity in-process model of the real handshake
  almost free, including the never-clearing availability bit.
- Cases: full transaction; multi-part `StateDataMore` continuation; bounded drain
  against a stuck availability bit; timeout path; cleanup-on-exception; the
  serialization guarantee; each `NotSupportedException`; `--u64-host` plumbing
  through `BackendFactory`.

**Hardware** (opt-in, never in CI):

- A separate test category, skipped unless `SIM6502_U64_HOST` is set.
- The differential check: run `example/ultimate.suite` against `u64sim` and against
  `u64` and require identical results, excluding `control-reu-absent`.

Eight of the suite's ten tests were confirmed byte-identical on silicon during design
(`dos-identify`, `dos-change-directory`, `dos-change-directory-missing`,
`dos-open-and-read`, `dos-echo`, `control-identify`, `unknown-target`,
`unknown-command`). Of the remaining two, `dos-open-missing` diverged — that is
divergence 1 above — and `control-reu-absent` wedged the machine.

Among the eight, `READ_DATA` returned an **empty status** on the completing chunk:
the behaviour Milestone 1's Task 8 traced by hand against `get_more_data` and closed
as a coverage gap, now confirmed on hardware.

## In-scope corrections to `u64sim`

These are Milestone 1 defects that hardware exposed. Fixing them is what makes the
differential check pass honestly rather than by exclusion.

1. **FatFs error strings.** Port the table from `software/filesystem/file_system.cc`
   into a `FatFsStatus` helper. `OPEN_FILE` and the other paths `dos.cc` routes
   through `get_error_string` return it. `StatusFileNotFound` stays for the four
   `dos.cc` sites that genuinely use it. Update `example/ultimate.suite`'s
   `dos-open-missing` assertion to `"FILE DOESN'T EXIST"`.
2. **`UciRegisters.BusId`.** Default `0` → `11`, and make it configurable. Silicon
   reports `0x0B`, which is the SoftIEC "Soft Drive Bus ID" — a configured value, and
   no real Ultimate would report `0`.
3. **Configurable mount name.** `u64sim` hardcodes `/Usb0`; the real stick is
   `/USB1`. Make the mount name configurable, defaulting to `Usb0` for
   back-compatibility, so one suite file can run against both backends.

## Out of scope for this milestone

- The resident 6502 stub, and with it registers, flags, `ExecuteJsr`, and
  CIA-bracketed cycle counts. Revisit if a suite needs them or if bulk UCI
  throughput becomes a problem.
- `IHighLevelBackend` — achievable (screen via `readmem $0400`, keys via the
  `$0277`/`$C6` poke, `ColdStart` via `machine:reset`) but nothing needs it yet.
- REU emulation, network target, drive emulation and mounting — separate milestones.
- `control-reu-absent` in the hardware differential, until the firmware wedge is
  understood.
