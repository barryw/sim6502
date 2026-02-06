# VICE MCP Backend for sim6502

**Date:** 2026-02-05
**Status:** Design Phase

## Summary

Add VICE as an optional execution backend for sim6502 test suites via the vice-mcp embedded MCP server. Existing test files run unmodified against a real Commodore emulator instead of the internal CPU simulator, providing hardware-accurate execution with full VIC-II, SID, CIA, and interrupt behavior.

## Motivation

The internal sim6502 processor simulates a bare 6502/6510/65C02 CPU. This is fast and sufficient for pure computational logic, but cannot reproduce hardware interactions: interrupt timing, CIA timer behavior, VIC-II raster effects, or memory banking. VICE provides cycle-accurate emulation of the complete machine. By connecting sim6502's test DSL to VICE via its embedded MCP server, tests gain hardware accuracy without changing the test language.

## Architecture

### Execution Backend Abstraction

Introduce an `IExecutionBackend` interface that abstracts code execution. `SimBaseListener` talks to the backend instead of directly to `Processor`.

```
IExecutionBackend
  SimulatorBackend   (wraps existing Processor — current behavior)
  ViceBackend        (HTTP client to vice-mcp)
```

### IExecutionBackend Interface

```csharp
public interface IExecutionBackend
{
    void LoadBinary(byte[] data, ushort address);
    void SetRegister(string name, int value);
    int GetRegister(string name);
    void WriteByte(ushort address, byte value);
    byte ReadByte(ushort address);
    ushort ReadWord(ushort address);
    ExecutionResult ExecuteJsr(ushort address, JsrOptions options);
    long GetCycles();
    void LoadSymbols(string path);
    void SaveSnapshot(string name);
    void RestoreSnapshot(string name);
    void Reset();
    void SetWarpMode(bool enabled);
    void Dispose();
}
```

`SimulatorBackend` wraps the existing `Processor` class. Snapshot and warp methods are no-ops. `LoadSymbols` is a no-op (simulator doesn't use them). Behavior is identical to today.

`ViceBackend` translates each call into HTTP JSON-RPC requests to VICE's MCP server.

### ViceBackend Method-to-MCP Mapping

| Backend Method | MCP Tool Call |
|---|---|
| `LoadBinary(data, addr)` | `vice.memory.write` |
| `SetRegister("A", 0xFF)` | `vice.registers.set` |
| `GetRegister("A")` | `vice.registers.get` |
| `WriteByte(addr, val)` | `vice.memory.write` |
| `ReadByte(addr)` | `vice.memory.read` (length 1) |
| `ReadWord(addr)` | `vice.memory.read` (length 2) |
| `ExecuteJsr(addr, opts)` | Push return addr on stack, set PC, set breakpoint, `vice.execution.run`, wait for breakpoint |
| `GetCycles()` | `vice.trace.cycles.get` |
| `LoadSymbols(path)` | VICE symbol loading MCP tool |
| `SaveSnapshot(name)` | `vice.snapshot.save` |
| `RestoreSnapshot(name)` | `vice.snapshot.load` |
| `Reset()` | `vice.execution.reset` |
| `SetWarpMode(enabled)` | `vice.config.set` (warp resource) |

### ExecuteJsr Implementation

The internal simulator tracks JSR depth and stops on the matching RTS. With VICE, the approach is:

1. Read current SP from VICE
2. Push a synthetic return address onto the stack (via memory writes to the stack page)
3. Set PC to the target subroutine address
4. Set a breakpoint at the return address
5. If `fail_on_brk` is set, set a breakpoint on BRK vector ($FFFE/$FFFF)
6. Call `vice.execution.run`
7. Wait for breakpoint hit (poll or SSE) with timeout
8. Read execution result (registers, cycles)
9. Clean up breakpoints

### ViceConnection

Low-level JSON-RPC 2.0 client using `HttpClient`. Handles:

- Request serialization (method, params, id)
- Response deserialization and error code extraction
- Connection health checks (`vice.ping`)
- Retry logic for transient failures

Separated from `ViceBackend` for independent unit testing.

## Test Execution Flow

### Suite Startup

1. Connect to VICE (or launch it with `--launch-vice`)
2. Verify connection with `vice.ping`
3. Pause execution with `vice.execution.pause`
4. Enable warp mode (default on; disable with `--no-vice-warp`)
5. Soft-reset the machine with `vice.execution.reset`
6. Load all binaries from `load()` directives via `vice.memory.write`
7. Load symbol files into both sim6502 (for expression evaluation) and VICE (for VICE debugging features)
8. Save snapshot: `vice.snapshot.save` with name `suite_{n}_baseline`

### Per-Test Execution

1. Restore snapshot: `vice.snapshot.load` with `suite_{n}_baseline`
2. Apply test setup (register assignments, memory writes via MCP)
3. Execute `jsr` (breakpoint-based approach described above)
4. Wait for execution to stop (breakpoint, BRK, or timeout)
5. Run assertions (read state back via MCP, evaluate expressions locally)
6. Report pass/fail

### Suite Teardown

- Disable warp mode (restore original state)
- Optionally clean up snapshots

## CLI Flags

| Flag | Default | Description |
|---|---|---|
| `--backend vice` | `sim` | Use VICE instead of internal simulator |
| `--vice-host` | `127.0.0.1` | MCP server host |
| `--vice-port` | `6510` | MCP server port |
| `--launch-vice` | off | Auto-launch VICE process |
| `--vice-warp` | on | Enable warp mode during tests |
| `--no-vice-warp` | — | Disable warp mode (watch execution) |
| `--vice-timeout` | `5000` | Milliseconds before test execution times out |

## Error Handling

### Connection Errors

- If VICE isn't reachable at startup, fail fast: "Could not connect to VICE MCP server at {host}:{port}. Is VICE running with -mcpserver?"
- If connection drops mid-suite, fail current test, attempt reconnection before next suite. If reconnection fails, abort the run.

### Execution Timeout

When `jsr` doesn't hit the breakpoint within `--vice-timeout`:
- Pause VICE with `vice.execution.pause`
- Read current PC and registers for diagnostics
- Clean up breakpoints
- Fail the test: "Execution timed out after 5000ms. PC=$C340, A=$00, X=$FF. Code may be in an infinite loop."

### Snapshot Failures

- If `vice.snapshot.save` fails during suite setup, skip the entire suite with an error.
- If `vice.snapshot.load` fails before a test, fail that test and attempt to re-save the baseline.

### BRK Handling

With `fail_on_brk = true`, set a breakpoint on the BRK vector. If BRK fires before the expected return, fail with the BRK location.

### Behavioral Differences

The internal simulator is a bare CPU with no IRQs, NMI, or CIA timers. VICE has all of these running. Tests may behave differently against VICE. This is a feature — it's why you'd want VICE as a backend. Timeout should be generous enough to account for interrupt-driven code.

### --launch-vice Process Management

- Start `x64sc -mcpserver -mcpserverport {port} +confirmexit`
- Poll with `vice.ping` until available
- On test run completion, kill the process

## Project Structure

### New Files

```
sim6502/
  Backend/
    IExecutionBackend.cs      — interface definition
    SimulatorBackend.cs       — wraps existing Processor
    ViceBackend.cs            — HTTP client to vice-mcp
    ViceConnection.cs         — JSON-RPC client
    ViceLauncher.cs           — process management for --launch-vice
    ExecutionResult.cs        — jsr execution result

sim6502tests/
  Backend/
    ViceConnectionTests.cs    — JSON-RPC request/response formatting
    ViceBackendTests.cs       — method-to-MCP mapping (mock HTTP)
    SimulatorBackendTests.cs  — existing behavior through interface
```

### Modified Files

```
sim6502/
  Grammar/SimBaseListener.cs  — use IExecutionBackend instead of Processor
  Sim6502CLI.cs               — new CLI flags
```

### Dependencies

None. `HttpClient` and `System.Text.Json` are in the .NET base class library.

## What Does Not Change

- ANTLR grammar (no DSL syntax changes)
- Expression evaluation logic
- Symbol file resolution for assertions
- Test filtering, tagging, reporting
- Error rendering and output formatting
- Existing test files (run unmodified)
