# u64 Hardware Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `u64` execution backend that drives a physical Ultimate 64 over its REST API, so `example/ultimate.suite` can be run against real silicon and differentially compared with `u64sim`.

**Architecture:** Two narrow seams. `IUltimateBackend` is what the DSL's `uci()` actually needs, satisfied by both `U64SimBackend` and the new `U64Backend`. `IU64Connection` abstracts the REST transport, mirroring the existing `IViceConnection` pattern, so every hermetic test runs against a fake backed by `u64sim`'s own `UciRegisters`. `U64Backend` implements `IExecutionBackend` directly the way `ViceBackend` does — no `IMemoryMap`, because the machine is reached over the wire.

**Tech Stack:** C# / .NET 10, xunit, FluentAssertions, CommandLineParser, NLog. No new dependencies.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-30-u64-hardware-backend-design.md`. Read it before starting.
- Target framework is `net10.0`. Do not add NuGet packages.
- Tests use xunit + FluentAssertions, matching existing files under `sim6502tests/`.
- Ported constants and strings must be byte-exact and carry an origin comment naming the upstream file, per the convention established in `sim6502/Systems/Ultimate/UciConstants.cs`.
- Upstream reference clone: `/private/tmp/1541ultimate-master` (`APPL_VERSION_NUMBER` 3.14e). Re-clone with `git clone --depth 1 https://github.com/GideonZ/1541ultimate.git` if absent.
- **No hardware is required to complete any task in this plan.** Every test is hermetic. Task 9 adds an operator-run differential script that needs a machine, but the script is not executed by CI or by the test suite.
- The full suite must stay green. Baseline before starting: **1676 passed in `sim6502tests`, 26 in `sim6502-lsp-tests`, 0 failed.**
- `$DF1B-$DF1F` register semantics, the FIFO-per-byte rule, and the never-clearing availability bit are described in the spec. Re-read them before Task 6.

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `sim6502/Systems/Ultimate/FatFsStatus.cs` | FatFs result-code → status-string table ported from upstream |
| `sim6502/Backend/IUltimateBackend.cs` | The `uci()` seam |
| `sim6502/Backend/IU64Connection.cs` | REST transport seam |
| `sim6502/Backend/U64RestConnection.cs` | `HttpClient` implementation, serialized internally |
| `sim6502/Backend/U64BackendConfig.cs` | Host, port, timeouts |
| `sim6502/Backend/U64Backend.cs` | `IExecutionBackend` + `IUltimateBackend`, owns the UCI transaction |
| `sim6502tests/Backend/FakeU64Connection.cs` | `IU64Connection` backed by a real `UciRegisters` |
| `sim6502tests/Backend/U64BackendTests.cs` | UCI transaction and `IExecutionBackend` behaviour |
| `sim6502tests/Backend/U64RestConnectionTests.cs` | URL construction and serialization |
| `sim6502tests/Systems/Ultimate/FatFsStatusTests.cs` | The ported table |
| `scripts/differential.sh` | Operator-run hardware differential |

**Modified:** `UltimateDosTarget.cs`, `UciRegisters.cs`, `U64SimBackend.cs`, `U64SimBackendConfig.cs`, `BackendFactory.cs`, `Sim6502CLI.cs`, `SimBaseListener.cs`, `example/ultimate.suite`, `Makefile`, `README.md`.

---

### Task 1: Port the FatFs status table and fix `OPEN_FILE`

Hardware returns `"FILE DOESN'T EXIST"` where `u64sim` returns `"82,FILE NOT FOUND"`. Upstream `dos.cc:111-124` confirms hardware: `DOS_CMD_OPEN_FILE` returns `FileSystem::get_error_string(res)`, never `c_status_file_not_found`. See spec "Divergences found / 1".

**Files:**
- Create: `sim6502/Systems/Ultimate/FatFsStatus.cs`
- Create: `sim6502tests/Systems/Ultimate/FatFsStatusTests.cs`
- Modify: `sim6502/Systems/Ultimate/UltimateDosTarget.cs:244-263`
- Modify: `example/ultimate.suite:50-53`

**Interfaces:**
- Consumes: nothing.
- Produces: `public static class FatFsStatus` with `public const string FileDoesntExist`, `PathDoesntExist`, `InvalidName`, `AccessDenied`, `FileExists`, `WriteProtected`, `DirectoryNotEmpty`, `DiskFull`.

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/Systems/Ultimate/FatFsStatusTests.cs`:

```csharp
using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class FatFsStatusTests
{
    // Values are byte-exact from upstream software/filesystem/file_system.cc
    // get_error_string(). Confirmed on real hardware (fw 3.14d): OPEN_FILE on a
    // missing file answers "FILE DOESN'T EXIST", not "82,FILE NOT FOUND".
    [Theory]
    [InlineData("FILE DOESN'T EXIST")]
    [InlineData("PATH DOESN'T EXIST")]
    [InlineData("INVALID NAME")]
    [InlineData("ACCESS DENIED")]
    [InlineData("FILE EXISTS")]
    [InlineData("WRITE PROTECTED")]
    [InlineData("DIRECTORY NOT EMPTY")]
    [InlineData("DISK IS FULL")]
    public void Table_ContainsUpstreamString(string expected)
    {
        var all = new[]
        {
            FatFsStatus.FileDoesntExist, FatFsStatus.PathDoesntExist,
            FatFsStatus.InvalidName, FatFsStatus.AccessDenied,
            FatFsStatus.FileExists, FatFsStatus.WriteProtected,
            FatFsStatus.DirectoryNotEmpty, FatFsStatus.DiskFull
        };
        all.Should().Contain(expected);
    }

    [Fact]
    public void FileDoesntExist_HasNoNumericPrefix()
    {
        // The DOS status strings carry a "NN," prefix; the FatFs strings do not.
        // Getting this wrong is exactly the bug this task fixes.
        FatFsStatus.FileDoesntExist.Should().Be("FILE DOESN'T EXIST");
        FatFsStatus.FileDoesntExist.Should().NotContain(",");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~FatFsStatusTests`
Expected: FAIL — build error, `FatFsStatus` does not exist.

- [ ] **Step 3: Create the table**

Create `sim6502/Systems/Ultimate/FatFsStatus.cs`:

```csharp
// Ported from GideonZ/1541ultimate (GPL-3.0):
//   software/filesystem/file_system.cc  FileSystem::get_error_string
// Original author: Gideon Zweijtzer. See NOTICE.

namespace sim6502.Systems.Ultimate;

/// <summary>
/// FatFs result strings, as the Ultimate's DOS surfaces them.
///
/// These are NOT the "NN,MESSAGE" DOS status strings in
/// <see cref="UltimateDosTarget"/>. Upstream dos.cc returns FatFs text on some
/// paths and a DOS status on others, and which one applies is per-command.
/// DOS_CMD_OPEN_FILE (dos.cc:111-124) uses these; confirmed on hardware
/// running fw 3.14d, which answers "FILE DOESN'T EXIST" for a missing file.
/// </summary>
public static class FatFsStatus
{
    public const string FileDoesntExist   = "FILE DOESN'T EXIST";
    public const string PathDoesntExist   = "PATH DOESN'T EXIST";
    public const string InvalidName       = "INVALID NAME";
    public const string AccessDenied      = "ACCESS DENIED";
    public const string FileExists        = "FILE EXISTS";
    public const string WriteProtected    = "WRITE PROTECTED";
    public const string DirectoryNotEmpty = "DIRECTORY NOT EMPTY";
    public const string DiskFull          = "DISK IS FULL";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~FatFsStatusTests`
Expected: PASS, 9 tests.

- [ ] **Step 5: Write the failing test for the OPEN_FILE fix**

Append to `sim6502tests/Systems/Ultimate/UltimateDosTargetFileTests.cs`, inside the existing class:

```csharp
    [Fact]
    public void OpenFile_Missing_ReturnsFatFsStringNotDosStatus()
    {
        // Upstream dos.cc:111-124 returns FileSystem::get_error_string(res) here,
        // never c_status_file_not_found. Verified against fw 3.14d on real
        // hardware, which answers "FILE DOESN'T EXIST".
        var reply = _dos.ParseCommand(Command(0x01, 0x02, 0x01, "no-such-file.prg"));

        reply.Status.Should().Be(FatFsStatus.FileDoesntExist);
        reply.Status.Should().NotBe(UltimateDosTarget.StatusFileNotFound);
    }
```

If the existing file has no `Command(...)` helper building a target/command/attribute/name byte array, add this private helper to the class:

```csharp
    private static byte[] Command(byte target, byte cmd, byte attr, string name)
    {
        var bytes = new List<byte> { target, cmd, attr };
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(name));
        return bytes.ToArray();
    }
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~OpenFile_Missing_ReturnsFatFsStringNotDosStatus`
Expected: FAIL — actual `"82,FILE NOT FOUND"`, expected `"FILE DOESN'T EXIST"`.

- [ ] **Step 7: Fix the implementation**

In `sim6502/Systems/Ultimate/UltimateDosTarget.cs`, replace the `catch` block at lines 249-263:

```csharp
        catch (FileNotFoundException)
        {
            return UciReply.Empty(FatFsStatus.FileDoesntExist);
        }
        catch (DirectoryNotFoundException)
        {
            return UciReply.Empty(FatFsStatus.PathDoesntExist);
        }
        catch (UnauthorizedAccessException)
        {
            return UciReply.Empty(FatFsStatus.AccessDenied);
        }
        catch (IOException) when (mode == FileMode.CreateNew)
        {
            return UciReply.Empty(FatFsStatus.FileExists);
        }
        catch (Exception ex)
        {
            // Upstream maps every FatFs result through get_error_string; the
            // cases above cover the ones .NET distinguishes. Anything else is a
            // genuine internal failure.
            Logger.Warn($"DOS: could not open '{name}': {ex.Message}");
            return UciReply.Empty(StatusInternalError);
        }
```

Also change the out-of-mount rejection at line 219 from `StatusFileNotFound` to `FatFsStatus.FileDoesntExist`, so a path outside the mount is indistinguishable from a missing file — which is what upstream does, since FatFs simply fails to find it.

- [ ] **Step 8: Run the full DOS suites**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~UltimateDosTarget`
Expected: PASS. If a pre-existing test asserted `"82,FILE NOT FOUND"` for a *missing file on open*, update it to `FatFsStatus.FileDoesntExist`. Do **not** change assertions for the other four upstream `c_status_file_not_found` sites (`dos.cc:164, 189, 337, 596`) — `StatusFileNotFound` stays correct there.

- [ ] **Step 9: Update the example suite**

In `example/ultimate.suite`, change the `dos-open-missing` test:

```
    test("dos-open-missing", "opening a missing file is reported") {
      uci($01, $02, $01, "no-such-file.prg")
      assert(uci_status("FILE DOESN'T EXIST"), "OPEN_FILE on a missing file failed")
    }
```

- [ ] **Step 10: Run the example suite end to end**

Run:
```bash
dotnet run --project sim6502 -- --suitefile example/ultimate.suite \
  --backend u64sim --u64sim-fs-root sim6502tests/Fixtures/usb0
```
Expected: 10/10 pass, exit 0.

- [ ] **Step 11: Commit**

```bash
git add sim6502/Systems/Ultimate/FatFsStatus.cs \
        sim6502tests/Systems/Ultimate/FatFsStatusTests.cs \
        sim6502/Systems/Ultimate/UltimateDosTarget.cs \
        sim6502tests/Systems/Ultimate/UltimateDosTargetFileTests.cs \
        example/ultimate.suite
git commit -m "fix(ultimate): return FatFs error text from OPEN_FILE, as hardware does

Real hardware running fw 3.14d answers \"FILE DOESN'T EXIST\" for a missing
file. Upstream dos.cc:111-124 agrees: DOS_CMD_OPEN_FILE returns
FileSystem::get_error_string(res), and c_status_file_not_found is used only at
dos.cc:164, 189, 337 and 596. Milestone 1 deliberately skipped porting the
FatFs table on the grounds that it bought no test value; the differential check
against silicon falsified that."
```

---

### Task 2: Default `UciRegisters.BusId` to 11 and make it configurable

Silicon reports `$DF1B = 0x0B`. `/v1/configs` shows this is the SoftIEC "Soft Drive Bus ID", a configured value. A default of `0` is a value no real Ultimate would report.

**Files:**
- Modify: `sim6502/Systems/Ultimate/UciRegisters.cs:94`
- Modify: `sim6502/Backend/U64SimBackendConfig.cs`
- Modify: `sim6502/Backend/U64SimBackend.cs:45-51`
- Test: `sim6502tests/Systems/Ultimate/UciRegistersDecodeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `U64SimBackendConfig.BusId` (`byte`, default `11`).

- [ ] **Step 1: Write the failing test**

Append to `sim6502tests/Systems/Ultimate/UciRegistersDecodeTests.cs`:

```csharp
    [Fact]
    public void BusId_DefaultsToEleven()
    {
        // Real hardware reports 0x0B at $DF1B -- the SoftIEC "Soft Drive Bus ID".
        // 0 is not a value any real Ultimate reports.
        var uci = new UciRegisters();
        uci.Read(UciConstants.BusIdAddress).Should().Be(0x0B);
    }

    [Fact]
    public void BusId_IsConfigurable()
    {
        var uci = new UciRegisters { BusId = 9 };
        uci.Read(UciConstants.BusIdAddress).Should().Be(9);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~BusId_DefaultsToEleven`
Expected: FAIL — actual `0`, expected `11`.

- [ ] **Step 3: Change the default**

In `sim6502/Systems/Ultimate/UciRegisters.cs`, replace line 94:

```csharp
    /// <summary>
    /// Value returned from $DF1B. This is the SoftIEC bus ID, a user-configured
    /// setting on real hardware -- an Ultimate 64 Elite on fw 3.14d reports 0x0B
    /// (device 11), which is the firmware default. Valid IEC device numbers are
    /// 8-30; 0 is not a value real hardware reports.
    /// </summary>
    public byte BusId { get; set; } = 11;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~BusId`
Expected: PASS.

- [ ] **Step 5: Expose it through config**

In `sim6502/Backend/U64SimBackendConfig.cs`, add:

```csharp
    /// <summary>
    /// SoftIEC bus ID reported at $DF1B. Real hardware reports 11 by default.
    /// </summary>
    public byte BusId { get; set; } = 11;
```

In `sim6502/Backend/U64SimBackend.cs`, add `BusId = config.BusId,` to the `UciRegisters` initializer alongside `ServiceEnabled = true`.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: all pass. If a pre-existing test asserted `$DF1B` reads `0`, update it to `11` and note in the commit that the old expectation encoded a value hardware never produces.

- [ ] **Step 7: Commit**

```bash
git add sim6502/Systems/Ultimate/UciRegisters.cs \
        sim6502/Backend/U64SimBackendConfig.cs \
        sim6502/Backend/U64SimBackend.cs \
        sim6502tests/Systems/Ultimate/UciRegistersDecodeTests.cs
git commit -m "fix(ultimate): default the UCI bus ID to 11, as hardware reports

\$DF1B carries the SoftIEC bus ID. An Ultimate 64 Elite on fw 3.14d reports
0x0B, and /v1/configs shows it as the configurable \"Soft Drive Bus ID\". The
previous default of 0 is not a value any real Ultimate reports."
```

---

### Task 3: Make the u64sim mount name configurable

`UltimateFileSystem` already accepts a `mountName` (default `"Usb0"`); it is simply not plumbed through config. The real stick mounts as `/USB1`, so one suite file can only run against both backends if the name is settable.

**Files:**
- Modify: `sim6502/Backend/U64SimBackendConfig.cs`
- Modify: `sim6502/Backend/U64SimBackend.cs:39-42`
- Modify: `sim6502/Sim6502CLI.cs` (options block and `BuildBackendConfigs`)
- Test: `sim6502tests/Backend/U64SimBackendTests.cs`

**Interfaces:**
- Consumes: `U64SimBackendConfig` from Task 2.
- Produces: `U64SimBackendConfig.MountName` (`string`, default `"Usb0"`); CLI flag `--u64sim-mount`.

- [ ] **Step 1: Write the failing test**

Append to `sim6502tests/Backend/U64SimBackendTests.cs`:

```csharp
    [Fact]
    public void MountName_IsConfigurable_SoOneSuiteRunsAgainstBothBackends()
    {
        // The real stick mounts as /USB1; u64sim defaults to /Usb0. Without this
        // the same suite file cannot address both backends.
        var config = new U64SimBackendConfig { FsRoot = _fixture, MountName = "USB1" };
        using var backend = new U64SimBackend(config, new C64MemoryMap());

        // CHANGE_DIR ($11) on DOS target $01. The leading command bytes matter:
        // passing the bare path would make byte 0 ('/' = $2F) the target
        // selector, which resolves to unregistered target $0F and answers
        // "NO TARGET"/"00,OK" -- a test that would pass for the wrong reason.
        var (status, _) = backend.IssueUciCommand(Chdir("/USB1"));

        status.Should().Be("00,OK");
    }

    [Fact]
    public void MountName_DefaultRemainsUsb0()
    {
        // Negative control. With the default mount, /USB1 must NOT resolve --
        // otherwise the test above would still pass if MountName were ignored.
        var config = new U64SimBackendConfig { FsRoot = _fixture };
        using var backend = new U64SimBackend(config, new C64MemoryMap());

        var (status, _) = backend.IssueUciCommand(Chdir("/USB1"));

        status.Should().Be("83,NO SUCH DIRECTORY");
    }

    private static byte[] Chdir(string path)
    {
        var bytes = new List<byte> { 0x01, 0x11 };   // DOS target, CHANGE_DIR
        bytes.AddRange(Encoding.ASCII.GetBytes(path));
        return bytes.ToArray();
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~MountName_IsConfigurable`
Expected: FAIL — build error, `MountName` does not exist.

- [ ] **Step 3: Add the config property**

In `sim6502/Backend/U64SimBackendConfig.cs`:

```csharp
    /// <summary>
    /// Ultimate-side mount name for <see cref="FsRoot"/>, without the leading
    /// slash. Defaults to the historical "Usb0". Real hardware enumerates its
    /// stick as "USB1", so a suite meant to run against both backends should set
    /// this to match the machine.
    /// </summary>
    public string MountName { get; set; } = "Usb0";
```

- [ ] **Step 4: Plumb it through the backend**

In `sim6502/Backend/U64SimBackend.cs`, change the two filesystem constructions:

```csharp
        var dosFileSystemOne = new UltimateFileSystem(config.FsRoot, config.MountName);
        var dosFileSystemTwo = new UltimateFileSystem(config.FsRoot, config.MountName);
```

And update the log line to report the configured mount:

```csharp
        Logger.Info($"u64sim ready: /{config.MountName} -> '{config.FsRoot}', " +
                    $"UCI latency {config.UciLatencyCycles} cycles");
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~MountName_IsConfigurable`
Expected: PASS.

- [ ] **Step 6: Add the CLI flag**

In `sim6502/Sim6502CLI.cs`, after the `u64sim-uci-latency` option:

```csharp
            [Option("u64sim-mount", Required = false, Default = "Usb0",
                HelpText = "Ultimate-side mount name for the u64sim filesystem root. " +
                           "Real hardware usually enumerates its stick as USB1")]
            public string U64SimMount { get; set; } = "Usb0";
```

In `BuildBackendConfigs`, add to the `u64sim` config initializer:

```csharp
                    MountName = opts.U64SimMount,
```

- [ ] **Step 7: Run the full suite and the example suite**

Run: `dotnet test`
Expected: all pass.

Run:
```bash
dotnet run --project sim6502 -- --suitefile example/ultimate.suite \
  --backend u64sim --u64sim-fs-root sim6502tests/Fixtures/usb0
```
Expected: 10/10 pass, exit 0 (default mount unchanged).

- [ ] **Step 8: Commit**

```bash
git add sim6502/Backend/U64SimBackendConfig.cs sim6502/Backend/U64SimBackend.cs \
        sim6502/Sim6502CLI.cs sim6502tests/Backend/U64SimBackendTests.cs
git commit -m "feat(u64sim): make the Ultimate mount name configurable

UltimateFileSystem already took a mount name; it was never plumbed through
config. Real hardware enumerates its stick as /USB1 while u64sim defaulted to
/Usb0, so a single suite file could not address both backends."
```

---

### Task 4: Extract the `IUltimateBackend` seam

`uci()` currently demands the concrete `U64SimBackend`. Both backends must satisfy it.

**Files:**
- Create: `sim6502/Backend/IUltimateBackend.cs`
- Modify: `sim6502/Backend/U64SimBackend.cs:14`
- Modify: `sim6502/Grammar/SimBaseListener.cs:1615-1629`
- Test: `sim6502tests/Backend/U64SimListenerTests.cs`

**Interfaces:**
- Consumes: `U64SimBackend.IssueUciCommand(byte[]) -> (string Status, byte[] Data)`.
- Produces: `IUltimateBackend` with `(string Status, byte[] Data) IssueUciCommand(byte[] command)`; `SimBaseListener.RequireUltimateBackend(string command) -> IUltimateBackend`.

- [ ] **Step 1: Write the failing test**

Append to `sim6502tests/Backend/U64SimListenerTests.cs`:

```csharp
    [Fact]
    public void U64SimBackend_SatisfiesTheUltimateSeam()
    {
        // uci() must work against any Ultimate-capable backend, not just u64sim.
        var config = new U64SimBackendConfig { FsRoot = _fixture };
        using var backend = new U64SimBackend(config, new C64MemoryMap());

        IUltimateBackend seam = backend;
        var (status, data) = seam.IssueUciCommand(new byte[] { 0x01, 0x01 });

        status.Should().Be("00,OK");
        data.Should().StartWith(new byte[] { 0x55, 0x4c });  // "UL"
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~SatisfiesTheUltimateSeam`
Expected: FAIL — build error, `IUltimateBackend` does not exist.

- [ ] **Step 3: Create the interface**

Create `sim6502/Backend/IUltimateBackend.cs`:

```csharp
namespace sim6502.Backend;

/// <summary>
/// A backend that can carry Ultimate Command Interface traffic, whether the UCI
/// is simulated in-process or reached over the wire on real hardware.
///
/// This is deliberately narrow: it is exactly what the DSL's uci(), uci_status()
/// and uci_data() need, and nothing more. Widening it would couple the grammar to
/// backend internals that only one implementation has.
/// </summary>
public interface IUltimateBackend
{
    /// <summary>
    /// Run one UCI command to completion, walking every continuation part, and
    /// return the reply payload and status string.
    /// </summary>
    (string Status, byte[] Data) IssueUciCommand(byte[] command);
}
```

- [ ] **Step 4: Declare it on `U64SimBackend`**

In `sim6502/Backend/U64SimBackend.cs`, change the class declaration:

```csharp
public class U64SimBackend : IExecutionBackend, IUltimateBackend
```

No other change — `IssueUciCommand` already has the right signature.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~SatisfiesTheUltimateSeam`
Expected: PASS.

- [ ] **Step 6: Widen the listener**

In `sim6502/Grammar/SimBaseListener.cs`, replace `RequireU64SimBackend` (lines 1615-1622):

```csharp
        private IUltimateBackend RequireUltimateBackend(string command)
        {
            if (Backend is IUltimateBackend ultimate)
                return ultimate;

            throw new InvalidOperationException(
                $"'{command}' requires an Ultimate-capable backend " +
                $"(u64sim or u64). Current backend: {BackendType}");
        }
```

Then update every call site. Find them with:

```bash
grep -n "RequireU64SimBackend" sim6502/Grammar/SimBaseListener.cs
```

Replace each `RequireU64SimBackend(` with `RequireUltimateBackend(`.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: all pass. If a test asserted the old message text `"requires the u64sim backend"`, update it to match the new wording.

- [ ] **Step 8: Commit**

```bash
git add sim6502/Backend/IUltimateBackend.cs sim6502/Backend/U64SimBackend.cs \
        sim6502/Grammar/SimBaseListener.cs sim6502tests/Backend/U64SimListenerTests.cs
git commit -m "refactor(grammar): let uci() target any Ultimate-capable backend

Extracts IUltimateBackend from the concrete U64SimBackend dependency so the
forthcoming u64 hardware backend drives the same DSL functions. No behaviour
change: U64SimBackend already had the required signature."
```

---

### Task 5: REST transport — `IU64Connection` and `U64RestConnection`

**Files:**
- Create: `sim6502/Backend/IU64Connection.cs`
- Create: `sim6502/Backend/U64RestConnection.cs`
- Create: `sim6502/Backend/U64BackendConfig.cs`
- Create: `sim6502tests/Backend/U64RestConnectionTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `IU64Connection : IDisposable` with `byte ReadByte(int address)`, `void WriteByte(int address, byte value)`, `byte[] ReadBytes(int address, int length)`, `void WriteBytes(int address, byte[] data)`.
  - `U64BackendConfig` with `string Host`, `int Port` (80), `int HttpTimeoutMs` (5000), `int CommandBudgetMs` (30000).
  - `U64RestConnection(U64BackendConfig)` and `internal U64RestConnection(U64BackendConfig, HttpMessageHandler)`.

- [ ] **Step 1: Write the failing test**

Create `sim6502tests/Backend/U64RestConnectionTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using sim6502.Backend;
using Xunit;

namespace sim6502tests.Backend;

public class U64RestConnectionTests
{
    /// <summary>Records every request and replies with canned bytes.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        public byte[] Body = { 0x00 };

        protected override HttpResponseMessage Send(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Urls) Urls.Add($"{request.Method} {request.RequestUri}");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Body)
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
    }

    private static U64BackendConfig Config() =>
        new() { Host = "10.0.0.5", Port = 80 };

    [Fact]
    public void ReadByte_RequestsLengthOneAtHexAddress()
    {
        // length=1 is load-bearing: a span covering $DF1E/$DF1F POPS those FIFOs
        // on real hardware and silently eats the reply.
        var handler = new RecordingHandler { Body = new byte[] { 0xC9 } };
        using var conn = new U64RestConnection(Config(), handler);

        conn.ReadByte(0xDF1D).Should().Be(0xC9);
        handler.Urls.Should().ContainSingle()
            .Which.Should().Be("GET http://10.0.0.5:80/v1/machine:readmem?address=df1d&length=1");
    }

    [Fact]
    public void WriteByte_PutsTwoDigitHexData()
    {
        var handler = new RecordingHandler();
        using var conn = new U64RestConnection(Config(), handler);

        conn.WriteByte(0xDF1C, 0x01);
        handler.Urls.Should().ContainSingle()
            .Which.Should().Be("PUT http://10.0.0.5:80/v1/machine:writemem?address=df1c&data=01");
    }

    [Fact]
    public void WriteBytes_ChunksToTheFirmwareLimitOf128()
    {
        // PUT machine:writemem rejects more than 128 bytes
        // (route_machine.cc: "Maximum length of 128 bytes exceeded").
        var handler = new RecordingHandler();
        using var conn = new U64RestConnection(Config(), handler);

        conn.WriteBytes(0x1000, new byte[300]);

        handler.Urls.Should().HaveCount(3);
        handler.Urls[0].Should().Contain("address=1000");
        handler.Urls[1].Should().Contain("address=1080");
        handler.Urls[2].Should().Contain("address=1100");
    }

    [Fact]
    public void Requests_AreSerialized()
    {
        // Concurrent requests can lock the machine up, so the connection must
        // serialize internally rather than trusting callers.
        var handler = new SlowHandler();
        using var conn = new U64RestConnection(Config(), handler);

        Parallel.For(0, 8, _ => conn.ReadByte(0xDF1C));

        handler.MaxConcurrent.Should().Be(1);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        private int _current;
        public int MaxConcurrent;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref _current);
            InterlockedMax(ref MaxConcurrent, now);
            Thread.Sleep(20);
            Interlocked.Decrement(ref _current);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0 })
            });
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int seen;
            do
            {
                seen = Volatile.Read(ref target);
                if (value <= seen) return;
            } while (Interlocked.CompareExchange(ref target, value, seen) != seen);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~U64RestConnectionTests`
Expected: FAIL — build error, types do not exist.

- [ ] **Step 3: Create the config**

Create `sim6502/Backend/U64BackendConfig.cs`:

```csharp
namespace sim6502.Backend;

/// <summary>Configuration for the real Ultimate 64 hardware backend.</summary>
public class U64BackendConfig
{
    /// <summary>Hostname or IP of the Ultimate. Required.</summary>
    public string Host { get; set; } = "";

    /// <summary>HTTP port of the firmware's REST API.</summary>
    public int Port { get; set; } = 80;

    /// <summary>Timeout for a single HTTP round-trip.</summary>
    public int HttpTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// How long one UCI command may remain BUSY before the transaction gives up
    /// and runs its recovery sequence. Deliberately separate from
    /// <see cref="HttpTimeoutMs"/>: a command can legitimately stay busy for far
    /// longer than a single round-trip, and treating that as a wall-clock race is
    /// exactly what wedges the interface.
    /// </summary>
    public int CommandBudgetMs { get; set; } = 30000;
}
```

- [ ] **Step 4: Create the interface**

Create `sim6502/Backend/IU64Connection.cs`:

```csharp
namespace sim6502.Backend;

/// <summary>
/// Transport to an Ultimate 64's REST API, mirroring the IViceConnection seam so
/// the backend can be tested without hardware.
///
/// Implementations MUST serialize requests. Concurrent requests can lock a real
/// machine up, and that is not something callers can be relied on to respect.
/// </summary>
public interface IU64Connection : IDisposable
{
    /// <summary>
    /// Read exactly one byte. Single-byte reads are mandatory around the UCI
    /// registers: a read spanning $DF1E/$DF1F pops those FIFOs.
    /// </summary>
    byte ReadByte(int address);

    /// <summary>Write exactly one byte.</summary>
    void WriteByte(int address, byte value);

    /// <summary>Read an ascending span. Not safe across FIFO ports.</summary>
    byte[] ReadBytes(int address, int length);

    /// <summary>Write an ascending span, chunked to the firmware's limit.</summary>
    void WriteBytes(int address, byte[] data);
}
```

- [ ] **Step 5: Implement the connection**

Create `sim6502/Backend/U64RestConnection.cs`:

```csharp
using NLog;

namespace sim6502.Backend;

/// <summary>
/// REST transport to a real Ultimate 64.
///
/// machine:readmem and machine:writemem are DMA cycles on the cartridge bus
/// (route_machine.cc uses C64_DMA_RAW_WRITE), which is why writes to $DF1D reach
/// the UCI command FIFO exactly as a CPU write would.
///
/// Two firmware facts shape this class:
///   - PUT machine:writemem accepts at most 128 bytes of hex payload.
///   - Both endpoints address an ASCENDING SPAN. Writing N bytes at $DF1D would
///     land on $DF1E and beyond, so FIFO traffic must go one byte per request.
/// </summary>
public sealed class U64RestConnection : IU64Connection
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>Firmware limit for PUT machine:writemem.</summary>
    private const int MaxWriteChunk = 128;

    private readonly HttpClient _http;
    private readonly string _base;
    private readonly object _gate = new();
    private bool _disposed;

    public U64RestConnection(U64BackendConfig config)
        : this(config, new HttpClientHandler())
    {
    }

    internal U64RestConnection(U64BackendConfig config, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.Host))
            throw new ArgumentException(
                "The u64 backend needs a host. Set --u64-host.", nameof(config));

        _base = $"http://{config.Host}:{config.Port}/v1";
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(config.HttpTimeoutMs)
        };
    }

    public byte ReadByte(int address)
    {
        var body = ReadBytes(address, 1);
        if (body.Length < 1)
            throw new InvalidOperationException(
                $"Ultimate returned no data reading ${address:X4}");
        return body[0];
    }

    public byte[] ReadBytes(int address, int length)
    {
        lock (_gate)
        {
            var url = $"{_base}/machine:readmem?address={address:x}&length={length}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = Send(req, url);
            return resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        }
    }

    public void WriteByte(int address, byte value)
    {
        lock (_gate)
        {
            var url = $"{_base}/machine:writemem?address={address:x}&data={value:x2}";
            using var req = new HttpRequestMessage(HttpMethod.Put, url);
            using var resp = Send(req, url);
        }
    }

    public void WriteBytes(int address, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        for (var offset = 0; offset < data.Length; offset += MaxWriteChunk)
        {
            var take = Math.Min(MaxWriteChunk, data.Length - offset);
            var hex = Convert.ToHexString(data, offset, take).ToLowerInvariant();
            var target = address + offset;

            lock (_gate)
            {
                var url = $"{_base}/machine:writemem?address={target:x}&data={hex}";
                using var req = new HttpRequestMessage(HttpMethod.Put, url);
                using var resp = Send(req, url);
            }
        }
    }

    private HttpResponseMessage Send(HttpRequestMessage request, string url)
    {
        HttpResponseMessage response;
        try
        {
            response = _http.Send(request);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Ultimate request failed: {request.Method} {url}. {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new InvalidOperationException(
                $"Ultimate returned {(int)status} for {request.Method} {url}");
        }

        return response;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~U64RestConnectionTests`
Expected: PASS, 4 tests.

- [ ] **Step 7: Commit**

```bash
git add sim6502/Backend/IU64Connection.cs sim6502/Backend/U64RestConnection.cs \
        sim6502/Backend/U64BackendConfig.cs sim6502tests/Backend/U64RestConnectionTests.cs
git commit -m "feat(backend): REST transport for the Ultimate 64

readmem/writemem are DMA cycles on the cartridge bus, so they reach the UCI
registers directly. Two firmware constraints are encoded here: PUT writemem
caps at 128 bytes, and both endpoints address an ascending span, so FIFO ports
must be driven one byte per request. Requests are serialized inside the
connection because concurrent requests can lock a real machine up."
```

---

### Task 6: The UCI transaction

> **CORRECTION (post-implementation).** The code blocks in this task shipped six
> real defects, found by review and fixed in commit `53a6268`. The implemented
> source is authoritative; do not re-apply the code below verbatim.
>
> - **Critical.** `while (true)` had no continuation bound, so a device stuck on
>   `StateDataMore` looped forever. `UciRegisters` already guards this with
>   `MaxContinuationParts = 4096`; the sibling walk must not disagree.
> - **Critical.** `if (b == 0) break;` in the drain truncated every binary
>   payload — an 8-byte PRG returned 2 bytes. The premise was wrong: the
>   availability bit does *not* never-clear in general. `ResponseValid` is
>   `(pointer - start) < length` and clears normally; it sticks only when a reply
>   exactly fills its queue, which `MaxDrain` already covers. With this check the
>   backend returned different bytes than u64sim, defeating its whole purpose.
> - **Important.** The status drain used `ResponseBufferSize` (896) as its bound
>   instead of `StatusBufferSize` (256), so a full status returned 640 repeats of
>   its last byte. The bound is now a parameter.
> - **Important.** A reply with empty data *and* empty status sets no
>   availability bit, so a zero-length read or a `NoReplyFlag` command burned the
>   whole budget and then fired `ControlAbort` at healthy hardware. `WaitForReply`
>   now also returns once the state settles on `StateDataLast`/`StateDataMore`,
>   and `NoReplyFlag` is handled explicitly rather than by racing the state.
> - **Minor.** Dead `continue`; undocumented per-part budget semantics; two doc
>   comments repeating the false never-clears claim; `U64UciException` not sealed
>   and unable to chain an inner exception.


The heart of the backend. Read the spec's "The UCI transaction" section before starting.

**Files:**
- Create: `sim6502/Backend/U64Backend.cs`
- Create: `sim6502tests/Backend/FakeU64Connection.cs`
- Create: `sim6502tests/Backend/U64BackendTests.cs`

**Interfaces:**
- Consumes: `IU64Connection`, `U64BackendConfig` (Task 5); `IUltimateBackend` (Task 4); `UciConstants` (existing).
- Produces: `U64Backend(U64BackendConfig, IU64Connection)` implementing `IUltimateBackend`; `U64UciException : InvalidOperationException`.

- [ ] **Step 1: Write the fake**

Create `sim6502tests/Backend/FakeU64Connection.cs`:

```csharp
using sim6502.Backend;
using sim6502.Systems.Ultimate;

namespace sim6502tests.Backend;

/// <summary>
/// An IU64Connection backed by u64sim's own UciRegisters.
///
/// This gives a high-fidelity model of the real handshake for free, including
/// the upstream wart where the availability bit never clears -- the behaviour
/// that forces every drain loop to be bounded.
/// </summary>
public sealed class FakeU64Connection : IU64Connection
{
    private readonly UciRegisters _uci;
    private long _cycles;

    public int ReadCount { get; private set; }
    public int WriteCount { get; private set; }

    /// <summary>Addresses outside the UCI block, so plain memory ops work.</summary>
    private readonly Dictionary<int, byte> _memory = new();

    public FakeU64Connection(int latencyCycles = 0, params (int Target, ICommandTarget Impl)[] targets)
    {
        _uci = new UciRegisters(latencyCycles)
        {
            ServiceEnabled = true,
            // Every access advances the clock, so a busy-wait loop makes progress
            // exactly as it would against a running CPU.
            CycleCounter = () => _cycles
        };

        foreach (var (id, impl) in targets)
            _uci.RegisterTarget(id, impl);
    }

    private static bool IsUci(int address) =>
        address >= UciConstants.BusIdAddress && address <= UciConstants.StatusAddress;

    public byte ReadByte(int address)
    {
        ReadCount++;
        _cycles += 8;
        if (IsUci(address)) return _uci.Read(address);
        return _memory.TryGetValue(address, out var v) ? v : (byte)0;
    }

    public void WriteByte(int address, byte value)
    {
        WriteCount++;
        _cycles += 8;
        if (IsUci(address)) _uci.Write(address, value);
        else _memory[address] = value;
    }

    public byte[] ReadBytes(int address, int length)
    {
        var result = new byte[length];
        for (var i = 0; i < length; i++) result[i] = ReadByte(address + i);
        return result;
    }

    public void WriteBytes(int address, byte[] data)
    {
        for (var i = 0; i < data.Length; i++) WriteByte(address + i, data[i]);
    }

    public void Dispose() { }
}
```

- [ ] **Step 2: Write the failing test**

Create `sim6502tests/Backend/U64BackendTests.cs`:

```csharp
using System.Text;
using FluentAssertions;
using sim6502.Backend;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Backend;

public class U64BackendTests : IDisposable
{
    private readonly string _fixture;
    private readonly UltimateFileSystem _fs;
    private readonly UltimateDosTarget _dos;

    public U64BackendTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64backend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_fixture, "data"));
        File.WriteAllText(Path.Combine(_fixture, "data", "hello.txt"), "HELLO FROM USB0");

        _fs = new UltimateFileSystem(_fixture);
        _dos = new UltimateDosTarget(_fs, "ULTIMATE-II DOS V1.2");
    }

    private U64Backend Build(int latency = 0, out FakeU64Connection connection)
    {
        connection = new FakeU64Connection(latency, (1, _dos));
        return new U64Backend(new U64BackendConfig { Host = "fake" }, connection);
    }

    [Fact]
    public void IssueUciCommand_Identify_ReturnsReplyAndStatus()
    {
        using var backend = Build(out var conn);

        var (status, data) = backend.IssueUciCommand(new byte[] { 0x01, 0x01 });

        status.Should().Be("00,OK");
        Encoding.ASCII.GetString(data).Should().Be("ULTIMATE-II DOS V1.2");
    }

    [Fact]
    public void IssueUciCommand_PushesOneRequestPerCommandByte()
    {
        // $DF1D is a FIFO port but writemem addresses an ascending span, so a
        // multi-byte write would land on $DF1E. One request per byte is required.
        using var backend = Build(out var conn);

        backend.IssueUciCommand(new byte[] { 0x01, 0x01 });

        // 2 command bytes + push + at least one data-accept
        conn.WriteCount.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void IssueUciCommand_SurvivesBusyLatency()
    {
        // A non-zero latency means the client must poll $DF1C while BUSY. If the
        // wait treated BUSY as a wall-clock race this would fail or hang.
        using var backend = Build(latency: 64, out _);

        var (status, _) = backend.IssueUciCommand(new byte[] { 0x01, 0x01 });

        status.Should().Be("00,OK");
    }

    [Fact]
    public void IssueUciCommand_ReadsAFileAcrossContinuationParts()
    {
        using var backend = Build(out _);

        backend.IssueUciCommand(BuildCommand(0x01, 0x11, "/Usb0/data"));
        backend.IssueUciCommand(BuildCommand(0x01, 0x02, 0x01, "hello.txt"));
        var (_, data) = backend.IssueUciCommand(new byte[] { 0x01, 0x04, 0x0f, 0x00 });

        Encoding.ASCII.GetString(data).Should().Be("HELLO FROM USB0");
    }

    [Fact]
    public void IssueUciCommand_MissingFile_ReportsTheFatFsString()
    {
        using var backend = Build(out _);

        var (status, _) = backend.IssueUciCommand(
            BuildCommand(0x01, 0x02, 0x01, "no-such-file.prg"));

        status.Should().Be(FatFsStatus.FileDoesntExist);
    }

    [Fact]
    public void IssueUciCommand_NullCommand_Throws()
    {
        using var backend = Build(out _);
        var act = () => backend.IssueUciCommand(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IssueUciCommand_EmptyCommand_Throws()
    {
        using var backend = Build(out _);
        var act = () => backend.IssueUciCommand(Array.Empty<byte>());
        act.Should().Throw<ArgumentException>();
    }

    private static byte[] BuildCommand(byte target, byte cmd, string text)
    {
        var bytes = new List<byte> { target, cmd };
        bytes.AddRange(Encoding.ASCII.GetBytes(text));
        return bytes.ToArray();
    }

    private static byte[] BuildCommand(byte target, byte cmd, byte attr, string text)
    {
        var bytes = new List<byte> { target, cmd, attr };
        bytes.AddRange(Encoding.ASCII.GetBytes(text));
        return bytes.ToArray();
    }

    public void Dispose()
    {
        _dos.Dispose();
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, true);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~U64BackendTests`
Expected: FAIL — build error, `U64Backend` does not exist.

- [ ] **Step 4: Implement the transaction**

Create `sim6502/Backend/U64Backend.cs` with the UCI half only (the `IExecutionBackend` members come in Task 7, so the class will not compile as `IExecutionBackend` yet — declare only `IUltimateBackend` and `IDisposable` for now):

```csharp
using System.Diagnostics;
using System.Text;
using NLog;
using sim6502.Systems.Ultimate;

namespace sim6502.Backend;

/// <summary>Raised when a UCI transaction cannot be completed or recovered.</summary>
public class U64UciException : InvalidOperationException
{
    public U64UciException(string message) : base(message) { }
}

/// <summary>
/// A real Ultimate 64 reached over its REST API.
///
/// Scoped as a differential instrument for u64sim rather than a general
/// execution backend: it carries UCI traffic, reads and writes memory by DMA,
/// and resets the machine. Registers, flags, cycle counts and ExecuteJsr have no
/// REST equivalent and are not emulated -- see the spec's "Supported and
/// unsupported members".
/// </summary>
public sealed partial class U64Backend : IUltimateBackend, IDisposable
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Upper bound on a single reply or status drain.
    ///
    /// Bounded on purpose. The availability bit in $DF1C never clears once set --
    /// an upstream wart pinned by u64sim's tests and confirmed on silicon -- so a
    /// "read until the bit drops" loop would spin forever. Sized to the UCI's own
    /// response buffer.
    /// </summary>
    private const int MaxDrain = UciConstants.ResponseBufferSize;

    private readonly IU64Connection _connection;
    private readonly U64BackendConfig _config;
    private bool _disposed;

    public U64Backend(U64BackendConfig config, IU64Connection connection)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public U64Backend(U64BackendConfig config)
        : this(config, new U64RestConnection(config))
    {
    }

    public (string Status, byte[] Data) IssueUciCommand(byte[] command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Length == 0)
            throw new ArgumentException("A UCI command needs at least a target byte",
                nameof(command));

        var completed = false;
        try
        {
            foreach (var b in command)
                _connection.WriteByte(UciConstants.CommandAddress, b);

            _connection.WriteByte(UciConstants.ControlAddress,
                UciConstants.ControlPushCommand);

            var data = new List<byte>();
            var status = new List<byte>();

            while (true)
            {
                var state = WaitForReply();

                DrainInto(data, UciConstants.ResponseAddress,
                    UciConstants.StatusResponseAvailable);
                DrainInto(status, UciConstants.StatusAddress,
                    UciConstants.StatusStatusAvailable);

                if ((state & UciConstants.StatusStateMask) != UciConstants.StateDataMore)
                    break;

                // More parts follow: acknowledge and go round again.
                _connection.WriteByte(UciConstants.ControlAddress,
                    UciConstants.ControlDataAccept);
            }

            completed = true;
            return (Encoding.ASCII.GetString(status.ToArray()), data.ToArray());
        }
        finally
        {
            try
            {
                _connection.WriteByte(UciConstants.ControlAddress,
                    UciConstants.ControlDataAccept);
                if (!completed) Recover();
            }
            catch (Exception ex)
            {
                // Never let cleanup mask the original failure.
                Logger.Warn($"UCI cleanup failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Wait for a reply, treating BUSY as progress rather than as a deadline.
    ///
    /// Getting this wrong is not a theoretical risk: a 2.5s wall-clock timeout
    /// against a legitimately-busy command left a real machine mid-transaction
    /// and needed a power cycle.
    /// </summary>
    private byte WaitForReply()
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.ElapsedMilliseconds < _config.CommandBudgetMs)
        {
            var status = _connection.ReadByte(UciConstants.ControlAddress);

            if ((status & (UciConstants.StatusResponseAvailable |
                           UciConstants.StatusStatusAvailable)) != 0)
                return status;

            if ((status & UciConstants.StatusStateMask) == UciConstants.StateBusy)
                continue;
        }

        var last = _connection.ReadByte(UciConstants.ControlAddress);
        throw new U64UciException(
            $"The Ultimate did not answer within {_config.CommandBudgetMs}ms. " +
            $"Last status ${last:X2}. If the interface stays busy, only a power " +
            "cycle clears it -- see GideonZ/1541ultimate#740 for one command " +
            "known to wedge it.");
    }

    /// <summary>Drain one FIFO, bounded because the availability bit never clears.</summary>
    private void DrainInto(List<byte> sink, int address, byte availableBit)
    {
        var taken = 0;
        while (taken < MaxDrain &&
               (_connection.ReadByte(UciConstants.ControlAddress) & availableBit) != 0)
        {
            var b = _connection.ReadByte(address);
            if (b == 0) break;
            sink.Add(b);
            taken++;
        }
    }

    /// <summary>
    /// Release a stuck transaction. Safe when already idle.
    ///
    /// This is best-effort: on real firmware some commands leave Busy latched and
    /// no write to $DF1C clears it.
    /// </summary>
    private void Recover()
    {
        _connection.WriteByte(UciConstants.ControlAddress, UciConstants.ControlAbort);
        _connection.WriteByte(UciConstants.ControlAddress, UciConstants.ControlClearError);
        _connection.WriteByte(UciConstants.ControlAddress, UciConstants.ControlDataAccept);
        _connection.WriteByte(UciConstants.ControlAddress, 0x00);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~U64BackendTests`
Expected: PASS, 7 tests.

- [ ] **Step 6: Prove the drain bound actually bounds**

Append to `sim6502tests/Backend/U64BackendTests.cs`:

```csharp
    private sealed class StuckAvailabilityConnection : IU64Connection
    {
        // Models the upstream wart at its worst: the availability bit is set
        // forever and the response port always yields a non-zero byte. An
        // unbounded drain would never return.
        public byte ReadByte(int address) =>
            address == UciConstants.ControlAddress
                ? (byte)(UciConstants.StatusResponseAvailable | UciConstants.StateDataLast)
                : (byte)0x41;

        public void WriteByte(int address, byte value) { }
        public byte[] ReadBytes(int address, int length) => new byte[length];
        public void WriteBytes(int address, byte[] data) { }
        public void Dispose() { }
    }

    [Fact]
    public void IssueUciCommand_StuckAvailabilityBit_TerminatesInsteadOfHanging()
    {
        using var backend = new U64Backend(
            new U64BackendConfig { Host = "fake" }, new StuckAvailabilityConnection());

        var (_, data) = backend.IssueUciCommand(new byte[] { 0x01, 0x01 });

        data.Length.Should().Be(UciConstants.ResponseBufferSize);
    }
```

- [ ] **Step 7: Run it**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~StuckAvailabilityBit`
Expected: PASS, and it must complete in well under a second. If it hangs, `MaxDrain` is not being enforced.

- [ ] **Step 8: Commit**

```bash
git add sim6502/Backend/U64Backend.cs sim6502tests/Backend/FakeU64Connection.cs \
        sim6502tests/Backend/U64BackendTests.cs
git commit -m "feat(backend): UCI transaction against real Ultimate 64 hardware

Pushes command bytes one per request into the \$DF1D FIFO, polls \$DF1C alone
(a span across \$DF1E/\$DF1F would pop those FIFOs), drains both queues under a
bound, and walks continuation parts.

Two behaviours are load-bearing and were learned by wedging real hardware: BUSY
means keep waiting rather than race a wall clock, and every exit path must
acknowledge and recover. Tests run against a fake backed by u64sim's own
UciRegisters, so the never-clearing availability bit is modelled faithfully."
```

---

### Task 7: `IExecutionBackend` members

**Files:**
- Modify: `sim6502/Backend/U64Backend.cs`
- Modify: `sim6502tests/Backend/U64BackendTests.cs`

**Interfaces:**
- Consumes: everything from Task 6.
- Produces: `U64Backend : IExecutionBackend, IUltimateBackend`.

- [ ] **Step 1: Write the failing test**

Append to `sim6502tests/Backend/U64BackendTests.cs`:

```csharp
    [Fact]
    public void MemoryOperations_GoOverTheWire()
    {
        using var backend = Build(out var conn);

        backend.WriteByte(0xC000, 0x42);
        backend.ReadByte(0xC000).Should().Be(0x42);
    }

    [Fact]
    public void WriteWord_IsLittleEndian()
    {
        using var backend = Build(out _);

        backend.WriteWord(0xC000, 0x1234);

        backend.ReadByte(0xC000).Should().Be(0x34);
        backend.ReadByte(0xC001).Should().Be(0x12);
        backend.ReadWord(0xC000).Should().Be(0x1234);
    }

    [Theory]
    [InlineData("GetRegister")]
    [InlineData("SetRegister")]
    [InlineData("GetFlag")]
    [InlineData("SetFlag")]
    [InlineData("ExecuteJsr")]
    [InlineData("GetCycles")]
    [InlineData("ResetCycleCount")]
    [InlineData("SaveSnapshot")]
    [InlineData("RestoreSnapshot")]
    public void UnsupportedMembers_ThrowWithAnActionableMessage(string member)
    {
        using var backend = Build(out _);

        Action act = member switch
        {
            "GetRegister"     => () => backend.GetRegister("A"),
            "SetRegister"     => () => backend.SetRegister("A", 1),
            "GetFlag"         => () => backend.GetFlag("C"),
            "SetFlag"         => () => backend.SetFlag("C", true),
            "ExecuteJsr"      => () => backend.ExecuteJsr(0xC000, 0, true, true),
            "GetCycles"       => () => backend.GetCycles(),
            "ResetCycleCount" => () => backend.ResetCycleCount(),
            "SaveSnapshot"    => () => backend.SaveSnapshot("s"),
            _                 => () => backend.RestoreSnapshot("s")
        };

        act.Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("u64");
    }

    [Fact]
    public void IncidentalMembers_AreNoOpsRatherThanThrows()
    {
        // Suites set these without caring; throwing would break otherwise-valid
        // runs for no benefit.
        using var backend = Build(out _);

        backend.Invoking(b => b.SetWarpMode(true)).Should().NotThrow();
        backend.Invoking(b => b.LoadSymbols("x.sym")).Should().NotThrow();
        backend.TraceEnabled.Should().BeFalse();
        backend.GetTraceBuffer().Should().BeEmpty();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~U64BackendTests`
Expected: FAIL — build error, members do not exist.

- [ ] **Step 3: Implement the members**

In `sim6502/Backend/U64Backend.cs`, change the declaration to:

```csharp
public sealed partial class U64Backend : IExecutionBackend, IUltimateBackend
```

Remove the now-redundant `IDisposable` (it comes via `IExecutionBackend`). Add before `Dispose()`:

```csharp
    // ── Memory: real, by DMA on the cartridge bus ──
    //
    // CAVEAT: readmem/writemem go THROUGH THE PLA, so this is not raw RAM.
    // $D000-$DFFF reaches I/O (which is exactly why UCI traffic works at all),
    // and $A000-$BFFF / $E000-$FFFF return ROM when banked in. Upstream added a
    // `ramonly` parameter for this (GideonZ/1541ultimate#674) but it is not
    // present in fw 3.14d. Assertions about RAM under ROM or I/O will not agree
    // with the sim backend.

    public byte ReadByte(int address) => _connection.ReadByte(address);

    public void WriteByte(int address, byte value) => _connection.WriteByte(address, value);

    public int ReadWord(int address)
    {
        var bytes = _connection.ReadBytes(address, 2);
        return bytes[0] | (bytes[1] << 8);
    }

    public void WriteWord(int address, int value)
    {
        _connection.WriteBytes(address, new[]
        {
            (byte)(value & 0xFF),
            (byte)((value >> 8) & 0xFF)
        });
    }

    public void WriteMemoryValue(int address, int value)
    {
        if (value > 0xFF) WriteWord(address, value);
        else WriteByte(address, (byte)value);
    }

    public void LoadBinary(byte[] data, int address)
    {
        ArgumentNullException.ThrowIfNull(data);
        Logger.Info($"Loading {data.Length} bytes to ${address:X4} over REST");
        _connection.WriteBytes(address, data);
    }

    public void Reset()
    {
        // PUT machine:reset resets the C64. It does not restart the Ultimate's
        // own command-interface task, so it will not clear a wedged UCI.
        _connection.WriteBytes(ResetSentinelAddress, Array.Empty<byte>());
        Logger.Info("Reset requested");
    }

    // ── Not reachable over REST ──

    private const int ResetSentinelAddress = 0;

    private static NotSupportedException Unsupported(string member, string alternative) =>
        new($"'{member}' is not available on the u64 backend: the Ultimate's REST " +
            $"API exposes no equivalent. {alternative}");

    public int GetRegister(string name) =>
        throw Unsupported(nameof(GetRegister),
            "Reading CPU registers would need a resident 6502 stub, which this " +
            "milestone deliberately omits. Use --backend u64sim for register assertions.");

    public void SetRegister(string name, int value) =>
        throw Unsupported(nameof(SetRegister),
            "Use --backend u64sim for register assertions.");

    public bool GetFlag(string name) =>
        throw Unsupported(nameof(GetFlag),
            "Use --backend u64sim for flag assertions.");

    public void SetFlag(string name, bool value) =>
        throw Unsupported(nameof(SetFlag),
            "Use --backend u64sim for flag assertions.");

    public ExecutionResult ExecuteJsr(int address, int stopOnAddress, bool stopOnRts, bool failOnBrk) =>
        throw Unsupported(nameof(ExecuteJsr),
            "There is no breakpoint mechanism in the REST API. Drive the machine " +
            "with uci(), or use --backend u64sim to run 6502 code.");

    public int GetCycles() =>
        throw Unsupported(nameof(GetCycles),
            "Cycle counting would need CIA bracketing around a resident stub.");

    public void ResetCycleCount() =>
        throw Unsupported(nameof(ResetCycleCount),
            "Cycle counting would need CIA bracketing around a resident stub.");

    public void SaveSnapshot(string name) =>
        throw Unsupported(nameof(SaveSnapshot), "Snapshots have no REST equivalent.");

    public void RestoreSnapshot(string name) =>
        throw Unsupported(nameof(RestoreSnapshot), "Snapshots have no REST equivalent.");

    // ── Accepted and ignored ──
    //
    // Suites set these incidentally. Throwing would fail runs that are otherwise
    // perfectly valid, so they are logged no-ops instead.

    public void SetWarpMode(bool enabled) =>
        Logger.Debug($"SetWarpMode({enabled}) ignored: real hardware runs at 1MHz");

    public void LoadSymbols(string path) =>
        Logger.Debug($"LoadSymbols('{path}') ignored: symbols stay host-side on u64");

    public bool TraceEnabled
    {
        get => false;
        set
        {
            if (value) Logger.Warn("Tracing is not available on the u64 backend; ignoring");
        }
    }

    public void ClearTraceBuffer() { }

    public List<string> GetTraceBuffer() => new();
```

Replace the `Reset()` body above with a real REST call by adding this to `IU64Connection` and `U64RestConnection` instead of the sentinel hack:

In `sim6502/Backend/IU64Connection.cs`, add:

```csharp
    /// <summary>Reset the C64. Does not restart the Ultimate's own firmware tasks.</summary>
    void ResetMachine();
```

In `sim6502/Backend/U64RestConnection.cs`, add:

```csharp
    public void ResetMachine()
    {
        lock (_gate)
        {
            var url = $"{_base}/machine:reset";
            using var req = new HttpRequestMessage(HttpMethod.Put, url);
            using var resp = Send(req, url);
        }
    }
```

In `sim6502tests/Backend/FakeU64Connection.cs`, add:

```csharp
    public int ResetCount { get; private set; }
    public void ResetMachine() => ResetCount++;
```

In `sim6502tests/Backend/U64BackendTests.cs`, add to `StuckAvailabilityConnection`:

```csharp
        public void ResetMachine() { }
```

And simplify `U64Backend.Reset()` to:

```csharp
    public void Reset()
    {
        // PUT machine:reset resets the C64. It does not restart the Ultimate's
        // own command-interface task, so it will not clear a wedged UCI.
        _connection.ResetMachine();
        Logger.Info("C64 reset requested");
    }
```

Delete the `ResetSentinelAddress` constant.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~U64BackendTests`
Expected: PASS, 13 tests.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add sim6502/Backend/U64Backend.cs sim6502/Backend/IU64Connection.cs \
        sim6502/Backend/U64RestConnection.cs sim6502tests/Backend/FakeU64Connection.cs \
        sim6502tests/Backend/U64BackendTests.cs
git commit -m "feat(backend): IExecutionBackend surface for the u64 backend

Memory operations and reset are real. Registers, flags, ExecuteJsr, cycles and
snapshots have no REST equivalent and throw with a message naming the reason and
the alternative. SetWarpMode and LoadSymbols are logged no-ops because suites
set them incidentally and failing those runs would buy nothing.

Memory access is documented as non-equivalent to sim: DMA goes through the PLA,
so \$D000-\$DFFF reaches I/O and banked ROM shadows RAM."
```

---

### Task 8: Factory and CLI wiring

**Files:**
- Modify: `sim6502/Backend/BackendFactory.cs`
- Modify: `sim6502/Sim6502CLI.cs`
- Test: `sim6502tests/Backend/BackendFactoryTests.cs`, `sim6502tests/Sim6502CliTests.cs`

**Interfaces:**
- Consumes: `U64Backend`, `U64BackendConfig`.
- Produces: `BackendFactory.Create(..., U64BackendConfig? u64Config = null)`; CLI flags `--u64-host`, `--u64-port`, `--u64-timeout`.

- [ ] **Step 1: Write the failing test**

Append to `sim6502tests/Backend/BackendFactoryTests.cs`:

```csharp
    [Fact]
    public void Create_U64_WithoutHost_ThrowsWithTheFix()
    {
        var act = () => BackendFactory.Create(
            "u64", ProcessorType.MOS6510, new C64MemoryMap(),
            u64Config: new U64BackendConfig { Host = "" });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*--u64-host*");
    }

    [Fact]
    public void Create_UnknownBackend_ListsU64AmongValidOptions()
    {
        var act = () => BackendFactory.Create(
            "nonsense", ProcessorType.MOS6510, new C64MemoryMap());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*u64*");
    }
```

Append to `sim6502tests/Sim6502CliTests.cs`:

```csharp
    [Fact]
    public void BuildBackendConfigs_U64_PopulatesHostAndTimeouts()
    {
        var opts = new Sim6502CLI.Options
        {
            Backend = "u64",
            U64Host = "192.168.1.62",
            U64Port = 80,
            U64Timeout = 7000
        };

        var configs = Sim6502CLI.BuildBackendConfigs(opts);

        configs.U64.Should().NotBeNull();
        configs.U64!.Host.Should().Be("192.168.1.62");
        configs.U64.Port.Should().Be(80);
        configs.U64.HttpTimeoutMs.Should().Be(7000);
    }

    [Fact]
    public void BuildBackendConfigs_NonU64_LeavesU64ConfigNull()
    {
        var opts = new Sim6502CLI.Options { Backend = "sim" };
        Sim6502CLI.BuildBackendConfigs(opts).U64.Should().BeNull();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~U64`
Expected: FAIL — build errors.

- [ ] **Step 3: Add the factory case**

In `sim6502/Backend/BackendFactory.cs`, add the parameter to the signature:

```csharp
        U64SimBackendConfig? u64SimConfig = null,
        U64BackendConfig? u64Config = null)
```

Add the case before `default:`:

```csharp
            case "u64":
                u64Config ??= new U64BackendConfig();

                if (string.IsNullOrWhiteSpace(u64Config.Host))
                    throw new ArgumentException(
                        "The 'u64' backend needs the address of a real Ultimate 64. " +
                        "Set --u64-host.");

                Logger.Info($"Connecting to Ultimate 64 at {u64Config.Host}:{u64Config.Port}");
                return new U64Backend(u64Config);
```

Update the `default:` message:

```csharp
                throw new ArgumentException(
                    $"Unknown backend type: {backendType}. " +
                    "Valid options: sim, vice, novavm, verilator, u64sim, u64");
```

- [ ] **Step 4: Add the CLI options**

In `sim6502/Sim6502CLI.cs`, update the `--backend` help text to mention `u64`, and add after the `u64sim-mount` option:

```csharp
            [Option("u64-host", Required = false,
                HelpText = "Hostname or IP of a real Ultimate 64 (u64 backend)")]
            public string? U64Host { get; set; }

            [Option("u64-port", Required = false, Default = 80,
                HelpText = "HTTP port of the Ultimate's REST API")]
            public int U64Port { get; set; } = 80;

            [Option("u64-timeout", Required = false, Default = 5000,
                HelpText = "Timeout in ms for a single REST request to the Ultimate")]
            public int U64Timeout { get; set; } = 5000;
```

Change `BuildBackendConfigs` to return a four-tuple:

```csharp
        internal static (ViceBackendConfig? Vice, NovaVmBackendConfig? NovaVm,
                         U64SimBackendConfig? U64Sim, U64BackendConfig? U64)
            BuildBackendConfigs(Options opts)
```

and add as the fourth element:

```csharp
                opts.Backend == "u64" ? new U64BackendConfig
                {
                    Host = opts.U64Host ?? "",
                    Port = opts.U64Port,
                    HttpTimeoutMs = opts.U64Timeout
                } : null
```

Update the `BackendFactory.Create(...)` call site to pass the new config. Find it with:

```bash
grep -n "BuildBackendConfigs\|BackendFactory.Create" sim6502/Sim6502CLI.cs
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test sim6502tests --filter FullyQualifiedName~U64`
Expected: PASS.

- [ ] **Step 6: Verify the CLI rejects a missing host**

Run:
```bash
dotnet run --project sim6502 -- --suitefile example/ultimate.suite --backend u64
```
Expected: a clear error naming `--u64-host`, non-zero exit. It must NOT throw a raw `NullReferenceException`.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add sim6502/Backend/BackendFactory.cs sim6502/Sim6502CLI.cs \
        sim6502tests/Backend/BackendFactoryTests.cs sim6502tests/Sim6502CliTests.cs
git commit -m "feat(cli): add the u64 backend and --u64-host

Selecting u64 without a host fails with the flag to set rather than a null
reference later."
```

---

### Task 9: Differential script and documentation

The hardware differential is an operator-run script, not a test. It needs a physical machine, so CI must never invoke it.

**Files:**
- Create: `scripts/differential.sh`
- Modify: `Makefile`
- Modify: `README.md`

**Interfaces:**
- Consumes: the `u64` and `u64sim` backends.
- Produces: `make differential U64_HOST=<ip>`.

- [ ] **Step 1: Write the script**

Create `scripts/differential.sh`:

```bash
#!/usr/bin/env bash
#
# Run example/ultimate.suite against both the simulated and the real Ultimate 64
# and require identical results. This is the check u64sim exists to satisfy.
#
# Requires a physical machine. Never run in CI.
#
# Before the first run, provision the fixtures onto the machine's stick over FTP
# (the REST API has no arbitrary file-write endpoint):
#
#   curl --ftp-create-dirs -T sim6502tests/Fixtures/usb0/data/hello.txt \
#        ftp://$U64_HOST/USB1/data/hello.txt
#   curl -T sim6502tests/Fixtures/usb0/readme.txt \
#        ftp://$U64_HOST/USB1/readme.txt
#
set -euo pipefail

if [ -z "${U64_HOST:-}" ]; then
    echo "U64_HOST is not set. Usage: make differential U64_HOST=192.168.1.62" >&2
    exit 2
fi

MOUNT="${U64_MOUNT:-USB1}"
SUITE="${U64_SUITE:-example/ultimate.suite}"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

echo "==> Checking the Ultimate is reachable and idle at $U64_HOST"
IDLE=$(curl -sf --max-time 8 \
    "http://$U64_HOST/v1/machine:readmem?address=df1b&length=5" | xxd -p)
echo "    \$DF1B-\$DF1F = $IDLE"
case "$IDLE" in
    ??00*) ;;
    *) echo "    UCI is not idle (\$DF1C != 00). Power-cycle the machine and retry." >&2
       exit 1 ;;
esac

echo "==> Running $SUITE against u64sim"
dotnet run --project sim6502 -- --suitefile "$SUITE" \
    --backend u64sim \
    --u64sim-fs-root sim6502tests/Fixtures/usb0 \
    --u64sim-mount "$MOUNT" > "$OUT/u64sim.txt" 2>&1 || true

echo "==> Running $SUITE against real hardware at $U64_HOST"
dotnet run --project sim6502 -- --suitefile "$SUITE" \
    --backend u64 --u64-host "$U64_HOST" > "$OUT/u64.txt" 2>&1 || true

echo "==> Comparing"
# Strip the backend banner lines, which legitimately differ.
sed -E 's/^.*(u64sim ready|Connecting to Ultimate).*$//' "$OUT/u64sim.txt" > "$OUT/a.txt"
sed -E 's/^.*(u64sim ready|Connecting to Ultimate).*$//' "$OUT/u64.txt"    > "$OUT/b.txt"

if diff -u "$OUT/a.txt" "$OUT/b.txt"; then
    echo "==> IDENTICAL. u64sim matches silicon for $SUITE."
else
    echo "==> DIVERGENCE. Each difference is either a u64sim bug or a firmware bug." >&2
    echo "    Investigate before assuming u64sim is wrong -- see" >&2
    echo "    GideonZ/1541ultimate#740 for a case where the firmware was at fault." >&2
    exit 1
fi
```

Make it executable:

```bash
chmod +x scripts/differential.sh
```

- [ ] **Step 2: Add the Makefile target**

In `Makefile`, add:

```make
differential:
	@./scripts/differential.sh
```

Add `differential` to the `.PHONY` line if one exists.

- [ ] **Step 3: Verify the script fails cleanly without a host**

Run: `make differential`
Expected: `U64_HOST is not set. Usage: make differential U64_HOST=192.168.1.62`, exit 2.

- [ ] **Step 4: Confirm CI does not run it**

Run: `grep -n "differential" .woodpecker.yml || echo "not referenced in CI - correct"`
Expected: `not referenced in CI - correct`.

- [ ] **Step 5: Document the backend**

In `README.md`, in the backend section alongside the existing `u64sim` documentation, add:

````markdown
### `u64` — a real Ultimate 64

Runs UCI traffic against physical hardware over the firmware's REST API.

```bash
dotnet run --project sim6502 -- --suitefile example/ultimate.suite \
  --backend u64 --u64-host 192.168.1.62
```

This backend is a **differential instrument** for `u64sim`, not a general
execution backend. It carries `uci()` traffic, reads and writes memory by DMA,
and resets the machine. `jsr()`, register, flag and cycle assertions are not
available — they have no REST equivalent — and fail with a message naming the
alternative.

**Memory access is not equivalent to `sim`.** DMA goes through the PLA, so
`$D000-$DFFF` reads I/O rather than the RAM beneath it, and `$A000-$BFFF` /
`$E000-$FFFF` return ROM when banked in.

**Provision fixtures over FTP** before a differential run — the REST API has no
arbitrary file-write endpoint:

```bash
curl --ftp-create-dirs -T sim6502tests/Fixtures/usb0/data/hello.txt \
     ftp://192.168.1.62/USB1/data/hello.txt
```

Then compare both backends:

```bash
make differential U64_HOST=192.168.1.62
```

**Known firmware issue:** `uci($04, $08, ...)` (`LOAD_REU`) never returns on
fw 3.14d and leaves the command interface wedged until a power cycle. Reported
as [GideonZ/1541ultimate#740](https://github.com/GideonZ/1541ultimate/issues/740).
`example/ultimate.suite` keeps its `control-reu-absent` test because `u64sim`
returns the status upstream specifies; that one test is expected to fail against
current hardware.
````

- [ ] **Step 6: Run the full suite one final time**

Run: `dotnet test`
Expected: all pass, and the total should be the 1676 baseline plus every test added by Tasks 1-8.

- [ ] **Step 7: Verify the example suite still passes on u64sim**

Run:
```bash
dotnet run --project sim6502 -- --suitefile example/ultimate.suite \
  --backend u64sim --u64sim-fs-root sim6502tests/Fixtures/usb0
```
Expected: 10/10 pass, exit 0.

- [ ] **Step 8: Commit**

```bash
git add scripts/differential.sh Makefile README.md
git commit -m "feat: hardware differential check and u64 backend docs

make differential runs example/ultimate.suite against both backends and
requires identical output. It needs a physical machine, so it is a Makefile
target rather than a test and CI never invokes it. It refuses to start unless
the UCI is idle, since a wedged interface would otherwise produce a misleading
wall of failures."
```

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| `IUltimateBackend` seam | 4 |
| `IU64Connection` / `U64RestConnection`, serialization | 5 |
| `U64BackendConfig` (split HTTP vs command budget) | 5 |
| UCI transaction, BUSY-aware wait, bounded drain, cleanup | 6 |
| Continuation parts (`StateDataMore`) | 6 |
| Supported/unsupported member table | 7 |
| PLA banking caveat documented | 7 (code comment), 9 (README) |
| `BackendFactory` + `--u64-host` | 8 |
| Hermetic tests via `UciRegisters`-backed fake | 6, 7 |
| Hardware differential, opt-in, never in CI | 9 |
| FatFs error strings correction | 1 |
| `BusId` default 11 | 2 |
| Configurable mount name | 3 |
| Fixture provisioning as operator step | 9 |
| `control-reu-absent` excluded / documented | 9 |

**Deliberate deviation from the spec:** the spec said `LoadBinary` would use `POST machine:writemem` for bulk. `POST` takes a *multipart* attachment (`attachment_writer` in `route_machine.cc`), which is materially more code for no benefit this milestone — nothing in the acceptance suite loads a binary. Task 5 implements `WriteBytes` as chunked 128-byte `PUT`s instead. Throughput is ~2.5 KB/s, adequate for small payloads; if bulk loading ever matters, switch that one method to multipart `POST`.

**Type consistency:** `IssueUciCommand(byte[]) -> (string Status, byte[] Data)` is identical in `IUltimateBackend` (Task 4), `U64SimBackend` (existing) and `U64Backend` (Task 6). `IU64Connection` gains `ResetMachine()` in Task 7 — all three implementors (`U64RestConnection`, `FakeU64Connection`, `StuckAvailabilityConnection`) are updated in that same task. `BuildBackendConfigs` becomes a four-tuple in Task 8, with its sole call site updated in the same step.

**Placeholder scan:** none. Every code step carries complete code; every run step carries an exact command and expected result.
