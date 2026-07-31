using System.Diagnostics;
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

    /// <summary>An 8-byte payload with embedded $00 bytes, like a real C64 PRG header.</summary>
    private static readonly byte[] BinaryPayload =
        { 0x01, 0x08, 0x00, 0x0C, 0x08, 0x0A, 0x00, 0x99 };

    /// <summary>
    /// 1300 bytes against UltimateDosTarget.ReadChunkSize (512) walks three
    /// continuation parts (512 + 512 + 276) in a single read.
    /// </summary>
    private static readonly byte[] LargeFilePayload = BuildLargeFilePayload();

    private static byte[] BuildLargeFilePayload()
    {
        var data = new byte[1300];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)i;
        return data;
    }

    public U64BackendTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64backend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_fixture, "data"));
        File.WriteAllText(Path.Combine(_fixture, "data", "hello.txt"), "HELLO FROM USB0");
        // UltimateFileSystem snapshots this directory at construction time (see
        // below), so every fixture file must exist before that call, not inside
        // an individual test method.
        File.WriteAllBytes(Path.Combine(_fixture, "data", "binary.prg"), BinaryPayload);
        File.WriteAllBytes(Path.Combine(_fixture, "data", "large.bin"), LargeFilePayload);

        _fs = new UltimateFileSystem(_fixture);
        _dos = new UltimateDosTarget(_fs, "ULTIMATE-II DOS V1.2");
    }

    // Parameter order is (out, then optional): an out parameter can't carry a
    // default value, so it must come before latency to satisfy CS1737
    // (optional parameters must appear after all required ones).
    private U64Backend Build(out FakeU64Connection connection, int latency = 0)
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
        // multi-byte write (e.g. WriteBytes(CommandAddress, command)) would
        // land bytes 1+ on $DF1E, $DF1F, etc. instead of $DF1D. Asserting only
        // a bare write count can't catch that: FakeU64Connection.WriteBytes
        // loops over WriteByte too, so the count is identical either way.
        // Every command byte must land on CommandAddress specifically.
        var command = new byte[] { 0x01, 0x01 };
        using var backend = Build(out var conn);

        backend.IssueUciCommand(command);

        conn.WrittenAddresses.Take(command.Length)
            .Should().OnlyContain(address => address == UciConstants.CommandAddress);
    }

    [Fact]
    public void IssueUciCommand_SurvivesBusyLatency()
    {
        // A non-zero latency means the client must poll $DF1C while BUSY. If the
        // wait treated BUSY as a wall-clock race this would fail or hang.
        using var backend = Build(out _, latency: 64);

        var (status, _) = backend.IssueUciCommand(new byte[] { 0x01, 0x01 });

        status.Should().Be("00,OK");
    }

    [Fact]
    public void IssueUciCommand_ReadsAFileAcrossContinuationParts()
    {
        // large.bin is 1300 bytes against a 512-byte ReadChunkSize, so this
        // genuinely walks three continuation parts (512 + 512 + 276) inside a
        // single IssueUciCommand call. (A 15-byte fixture here previously made
        // this test's name a lie: it never reached a second part.)
        using var backend = Build(out _);

        backend.IssueUciCommand(BuildCommand(0x01, 0x11, "/Usb0/data"));
        backend.IssueUciCommand(BuildCommand(0x01, 0x02, 0x01, "large.bin"));
        var (_, data) = backend.IssueUciCommand(new byte[]
        {
            0x01, 0x04,
            (byte)(LargeFilePayload.Length & 0xFF),
            (byte)(LargeFilePayload.Length >> 8)
        });

        data.Should().Equal(LargeFilePayload);
    }

    [Fact]
    public void IssueUciCommand_ReadsBinaryDataWithEmbeddedZeroBytes()
    {
        // Regression for the DrainInto zero-check that used to treat $00 as
        // end-of-data. Every C64 binary contains $00 bytes; the pre-fix drain
        // truncated this 8-byte payload to just the first two bytes (01 08).
        using var backend = Build(out _);

        backend.IssueUciCommand(BuildCommand(0x01, 0x11, "/Usb0/data"));
        backend.IssueUciCommand(BuildCommand(0x01, 0x02, 0x01, "binary.prg"));
        var (_, data) = backend.IssueUciCommand(
            new byte[] { 0x01, 0x04, (byte)BinaryPayload.Length, 0x00 });

        data.Should().Equal(BinaryPayload);
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

    [Fact]
    public void IssueUciCommand_ZeroLengthRead_ReturnsPromptlyInsteadOfBurningTheBudget()
    {
        // A zero-length DOS read answers with empty data AND empty status, so
        // neither availability bit is ever set. Before the fix, WaitForReply
        // only returned on an availability bit, so this burned the entire
        // CommandBudgetMs before wrongly declaring a hang.
        using var backend = Build(out _);

        backend.IssueUciCommand(BuildCommand(0x01, 0x11, "/Usb0/data"));
        backend.IssueUciCommand(BuildCommand(0x01, 0x02, 0x01, "hello.txt"));

        var sw = Stopwatch.StartNew();
        var (status, data) = backend.IssueUciCommand(new byte[] { 0x01, 0x04, 0x00, 0x00 });
        sw.Stop();

        status.Should().BeEmpty();
        data.Should().BeEmpty();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            "a completed zero-length read must not wait out CommandBudgetMs");
    }

    [Fact]
    public void IssueUciCommand_NoReplyFlagAtLatency_DoesNotDesyncTheFollowingCommand()
    {
        // At latency 0 (the test above) PUSH_CMD's ServicePending call services
        // the command synchronously inside the same write, so the command
        // pointer is already reset by the time IssueUciCommand returns -- that
        // configuration cannot see this bug. At a real latency, PUSH_CMD sets
        // BUSY synchronously but the command pointer only resets once the
        // firmware's HANDSHAKE_ACCEPT_COMMAND runs, later. Returning the
        // instant the strobe is written -- without waiting for BUSY to clear --
        // left the next command's bytes appended onto this one's still-unread
        // command buffer, and the concatenated buffer inherited byte 0's
        // NoReplyFlag, so the *second* command's real reply was discarded and
        // the client spun for the whole budget before throwing ERROR_BUSY.
        using var backend = new U64Backend(
            new U64BackendConfig { Host = "fake", CommandBudgetMs = 500 },
            new FakeU64Connection(64, (1, _dos)));

        backend.IssueUciCommand(new byte[] { 0x81, 0x01 }); // no-reply, target 1

        var (status, data) = backend.IssueUciCommand(new byte[] { 0x01, 0x01 }); // identify

        status.Should().Be("00,OK");
        Encoding.ASCII.GetString(data).Should().Be("ULTIMATE-II DOS V1.2");
    }

    [Fact]
    public void IssueUciCommand_NoReplyFlag_ReturnsPromptlyWithoutWaiting()
    {
        // Bit 7 of command byte 0 means "don't reply". Before the fix there was
        // nothing that recognised this locally, so the backend raced the
        // Ultimate's state machine (or the default config's 30s budget) instead
        // of returning immediately.
        using var backend = Build(out _);

        var sw = Stopwatch.StartNew();
        var (status, data) = backend.IssueUciCommand(new byte[] { 0x81, 0x01 });
        sw.Stop();

        status.Should().BeEmpty();
        data.Should().BeEmpty();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            "a NoReplyFlag command must not wait for a reply that will never come");
    }

    private sealed class FixedStatusTarget : ICommandTarget
    {
        private readonly string _status;
        public FixedStatusTarget(string status) => _status = status;
        public UciReply ParseCommand(byte[] command) => UciReply.Empty(_status);
        public UciReply GetMoreData() => UciReply.Empty(UciConstants.StatusOk);
        public void Abort(int bytesConsumed) { }
    }

    [Fact]
    public void IssueUciCommand_StatusExactlyFillsQueue_ReturnsStatusBufferSizeNotResponseBufferSize()
    {
        // A status that exactly fills its 256-byte queue leaves the availability
        // bit stuck (pointer saturation, same mechanism as UciRegisters' doc
        // comment). Before the fix, the status drain used the *response*
        // buffer's 896-byte bound, so it kept reading the stuck bit and
        // returned 896 bytes: 256 real ones plus 640 repeats of the last byte.
        var status256 = new string('X', UciConstants.StatusBufferSize);
        var connection = new FakeU64Connection(0, (2, new FixedStatusTarget(status256)));
        using var backend = new U64Backend(new U64BackendConfig { Host = "fake" }, connection);

        var (status, _) = backend.IssueUciCommand(new byte[] { 0x02, 0x01 });

        status.Length.Should().Be(UciConstants.StatusBufferSize);
        status.Should().Be(status256);
    }

    private sealed class StuckContinuationConnection : IU64Connection
    {
        // Every reply part reports StateDataMore with the response available,
        // so a client that never bounds its continuation loop spins forever.
        public byte ReadByte(int address) =>
            address == UciConstants.ControlAddress
                ? (byte)(UciConstants.StatusResponseAvailable | UciConstants.StateDataMore)
                : (byte)0x41;

        public void WriteByte(int address, byte value) { }
        public byte[] ReadBytes(int address, int length) => new byte[length];
        public void WriteBytes(int address, byte[] data) { }
        public void ResetMachine() { }
        public void Dispose() { }
    }

    [Fact]
    public void IssueUciCommand_UnboundedContinuation_ThrowsInsteadOfHanging()
    {
        using var backend = new U64Backend(
            new U64BackendConfig { Host = "fake" }, new StuckContinuationConnection());

        var sw = Stopwatch.StartNew();
        var act = () => backend.IssueUciCommand(new byte[] { 0x01, 0x01 });
        act.Should().Throw<U64UciException>().WithMessage("*4096*");
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            "the continuation-part guard must trip long before any wall-clock budget");
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
        public void ResetMachine() { }
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

    private sealed class AlwaysIdleConnection : IU64Connection
    {
        // Never sets an availability bit and never leaves Idle, so WaitForReply
        // has no choice but to spin out CommandBudgetMs and throw -- this
        // exercises the finally's cleanup path without ever taking the
        // continuation-loop's own ControlDataAccept write, which would
        // otherwise be indistinguishable from the cleanup's.
        public byte ReadByte(int address) => 0x00;
        public void WriteByte(int address, byte value) { }
        public byte[] ReadBytes(int address, int length) => new byte[length];
        public void WriteBytes(int address, byte[] data) { }
        public void ResetMachine() { }
        public void Dispose() { }
    }

    /// <summary>Records writes, but throws once on the first ControlDataAccept.</summary>
    private sealed class FailingAckConnection : IU64Connection
    {
        private readonly IU64Connection _inner;
        private bool _thrown;

        public FailingAckConnection(IU64Connection inner) => _inner = inner;

        public List<(int Address, byte Value)> Writes { get; } = new();

        public byte ReadByte(int address) => _inner.ReadByte(address);

        public void WriteByte(int address, byte value)
        {
            if (!_thrown && address == UciConstants.ControlAddress &&
                value == UciConstants.ControlDataAccept)
            {
                _thrown = true;
                throw new InvalidOperationException("simulated transport failure on acknowledge");
            }

            // Only writes issued after the simulated failure are recorded, so
            // this proves what ran *during recovery*, not the command push
            // that preceded it.
            if (_thrown) Writes.Add((address, value));
            _inner.WriteByte(address, value);
        }

        public byte[] ReadBytes(int address, int length) => _inner.ReadBytes(address, length);
        public void WriteBytes(int address, byte[] data) => _inner.WriteBytes(address, data);
        public void ResetMachine() => _inner.ResetMachine();
        public void Dispose() => _inner.Dispose();
    }

    [Fact]
    public void IssueUciCommand_CleanupAcknowledgeFails_StillAttemptsRecovery()
    {
        // Before the fix, one try/catch wrapped both the finally's
        // ControlDataAccept acknowledge and the Recover() call. A thrown
        // acknowledge jumped straight to the catch, so Recover() never ran --
        // leaving the interface parked mid-transaction for the next command.
        // The acknowledge and the recovery must be independent: one failing
        // must not stop the other from being attempted.
        var recorder = new FailingAckConnection(new AlwaysIdleConnection());
        using var backend = new U64Backend(
            new U64BackendConfig { Host = "fake", CommandBudgetMs = 50 }, recorder);

        var act = () => backend.IssueUciCommand(new byte[] { 0x01, 0x01 });

        act.Should().Throw<U64UciException>(); // the real failure: no reply within budget
        recorder.Writes.Should().Equal(
            (UciConstants.ControlAddress, UciConstants.ControlAbort),
            (UciConstants.ControlAddress, UciConstants.ControlClearError),
            (UciConstants.ControlAddress, UciConstants.ControlDataAccept),
            (UciConstants.ControlAddress, (byte)0x00));
    }

    [Fact]
    public void IssueUciCommand_SuccessfulTransactionAcknowledgeFails_StillAttemptsRecovery()
    {
        // IMPORTANT-2 (third review round): recovery was gated on `!completed`
        // alone, so a throwing cleanup acknowledge on an otherwise *successful*
        // transaction was logged and nothing recovered. That leaves the
        // interface parked in DataLast: the next push hits ERROR_BUSY,
        // WaitForReply reads the stale DataLast status and returns immediately,
        // and the next command silently returns empty status and empty data --
        // a false mismatch in a differential instrument, not a visible error.
        // Recovery must run whenever the transaction failed OR the acknowledge
        // itself failed.
        var inner = new FakeU64Connection(0, (1, _dos));
        var recorder = new FailingAckConnection(inner);
        using var backend = new U64Backend(new U64BackendConfig { Host = "fake" }, recorder);

        var (status, data) = backend.IssueUciCommand(new byte[] { 0x01, 0x01 }); // identify

        status.Should().Be("00,OK");
        Encoding.ASCII.GetString(data).Should().Be("ULTIMATE-II DOS V1.2");
        recorder.Writes.Should().Equal(
            (UciConstants.ControlAddress, UciConstants.ControlAbort),
            (UciConstants.ControlAddress, UciConstants.ControlClearError),
            (UciConstants.ControlAddress, UciConstants.ControlDataAccept),
            (UciConstants.ControlAddress, (byte)0x00));
    }

    private sealed class AlwaysBusyConnection : IU64Connection
    {
        // Never leaves Busy, so the no-reply settle loop can never observe an
        // exit condition -- this exercises its timeout path deliberately.
        public List<(int Address, byte Value)> Writes { get; } = new();
        public byte ReadByte(int address) => UciConstants.StateBusy;
        public void WriteByte(int address, byte value) => Writes.Add((address, value));
        public byte[] ReadBytes(int address, int length) => new byte[length];
        public void WriteBytes(int address, byte[] data) { }
        public void ResetMachine() { }
        public void Dispose() { }
    }

    [Fact]
    public void IssueUciCommand_NoReplyFlagNeverLeavesBusy_ThrowsInsteadOfReportingSuccess()
    {
        // IMPORTANT-1 (third review round): on timeout the settle loop fell
        // through, set completed = true and returned success, suppressing
        // Recover() -- while the interface was still Busy with the command
        // pointer parked past this command's bytes, exactly the state the loop
        // exists to prevent. It must throw on timeout, like WaitForReply does,
        // so Recover() gets a chance to run.
        var connection = new AlwaysBusyConnection();
        using var backend = new U64Backend(
            new U64BackendConfig { Host = "fake", CommandBudgetMs = 50 }, connection);

        var act = () => backend.IssueUciCommand(new byte[] { 0x81, 0x01 }); // no-reply

        act.Should().Throw<U64UciException>();
        // MINOR-1 (fourth review round): the throw alone doesn't prove
        // Recover() ran -- it would pass even if the call were deleted. Assert
        // the four writes Recover() makes actually landed.
        connection.Writes.Should().EndWith(new (int Address, byte Value)[]
        {
            (UciConstants.ControlAddress, UciConstants.ControlAbort),
            (UciConstants.ControlAddress, UciConstants.ControlClearError),
            (UciConstants.ControlAddress, UciConstants.ControlDataAccept),
            (UciConstants.ControlAddress, (byte)0x00),
        });
    }

    private sealed class ErrorLatchConnection : IU64Connection
    {
        // Models a push strobe rejected by the ERROR_BUSY latch: $DF1C reports
        // StatusError with an otherwise-idle state immediately after the push,
        // before the caller has had any chance to observe BUSY or wait for a
        // reply.
        public List<(int Address, byte Value)> Writes { get; } = new();
        public byte ReadByte(int address) =>
            address == UciConstants.ControlAddress ? UciConstants.StatusError : (byte)0x00;
        public void WriteByte(int address, byte value) => Writes.Add((address, value));
        public byte[] ReadBytes(int address, int length) => new byte[length];
        public void WriteBytes(int address, byte[] data) { }
        public void ResetMachine() { }
        public void Dispose() { }
    }

    [Fact]
    public void IssueUciCommand_ErrorLatchOnPush_ThrowsInsteadOfSilentSuccess()
    {
        // MINOR-1: before the fix, nothing inspected StatusError ($08). On the
        // no-reply path a non-Idle leftover state fails the settle loop's
        // `== StateBusy` test on its very first read, so a push the Ultimate
        // rejected was reported as an instant, silent success.
        var connection = new ErrorLatchConnection();
        using var backend = new U64Backend(
            new U64BackendConfig { Host = "fake" }, connection);

        var act = () => backend.IssueUciCommand(new byte[] { 0x81, 0x01 }); // no-reply

        act.Should().Throw<U64UciException>().WithMessage("*$08*");
        // MINOR-1 (fourth review round): the throw alone doesn't prove
        // Recover() ran -- it would pass even if the call were deleted. Assert
        // the four writes Recover() makes actually landed.
        connection.Writes.Should().EndWith(new (int Address, byte Value)[]
        {
            (UciConstants.ControlAddress, UciConstants.ControlAbort),
            (UciConstants.ControlAddress, UciConstants.ControlClearError),
            (UciConstants.ControlAddress, UciConstants.ControlDataAccept),
            (UciConstants.ControlAddress, (byte)0x00),
        });
    }

    [Fact]
    public void IssueUciCommand_PushRejectedWhileGenuinelyBusy_Throws()
    {
        var conn = new FakeU64Connection(64, (1, _dos));
        using var backend = new U64Backend(
            new U64BackendConfig { Host = "fake", CommandBudgetMs = 200 }, conn);

        conn.WriteByte(UciConstants.CommandAddress, 0x01);
        conn.WriteByte(UciConstants.CommandAddress, 0x01);
        conn.WriteByte(UciConstants.ControlAddress, UciConstants.ControlPushCommand);
        (conn.ReadByte(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateBusy);

        var act = () => backend.IssueUciCommand(new byte[] { 0x01, 0x01 });

        act.Should().Throw<U64UciException>().WithMessage("*error latch*");
    }

    [Fact]
    public void IssueUciCommand_StaleErrorLatchButIdle_SucceedsAndReturnsFullReply()
    {
        // Fix pass 4: _errorBusy is a pure latch (UciRegisters.cs:206-215) --
        // set only when a push arrives while the state isn't Idle, and cleared
        // only by an explicit ControlClearError write. ControlAbort returns the
        // state to Idle via HandshakeOut(0x87) WITHOUT clearing it, so "Idle
        // with the latch still set" is a stable, reachable state. Before the
        // fix, the round-3 StatusError check treated that stale bit as *this*
        // push having been rejected: it threw away an already-serviced reply
        // and then fired ControlAbort at a perfectly healthy machine.
        using var backend = Build(out var conn, latency: 64);

        // Push #1: latency keeps the state Busy until serviced.
        conn.WriteByte(UciConstants.CommandAddress, 0x01);
        conn.WriteByte(UciConstants.CommandAddress, 0x01);
        conn.WriteByte(UciConstants.ControlAddress, UciConstants.ControlPushCommand);

        // Push #2 while genuinely Busy: latches ERROR_BUSY without touching state.
        conn.WriteByte(UciConstants.ControlAddress, UciConstants.ControlPushCommand);

        // Let push #1 finish servicing, then abort it back to Idle -- ControlAbort
        // does not clear the latch. Bounded rather than unbounded: this relies on
        // FakeU64Connection advancing 8 cycles per access to leave BUSY at all --
        // if that ever stopped being true, an unbounded loop here would hang the
        // whole test run with no diagnostic instead of failing an assertion.
        byte pollStatus = 0;
        for (var i = 0; i < 64; i++)
        {
            pollStatus = conn.ReadByte(UciConstants.ControlAddress);
            if ((pollStatus & UciConstants.StatusStateMask) != UciConstants.StateBusy) break;
        }
        (pollStatus & UciConstants.StatusStateMask).Should().NotBe(UciConstants.StateBusy,
            "the fake should have left BUSY within 64 accesses");

        conn.WriteByte(UciConstants.ControlAddress, UciConstants.ControlAbort);

        for (var i = 0; i < 64; i++)
        {
            pollStatus = conn.ReadByte(UciConstants.ControlAddress);
            if ((pollStatus & UciConstants.StatusStateMask) == UciConstants.StateIdle) break;
        }
        (pollStatus & UciConstants.StatusStateMask).Should().Be(UciConstants.StateIdle,
            "the fake should have reached Idle within 64 accesses");

        // Confirm the setup: Idle, but the error bit is still latched.
        var setup = conn.ReadByte(UciConstants.ControlAddress);
        (setup & UciConstants.StatusStateMask).Should().Be(UciConstants.StateIdle);
        (setup & UciConstants.StatusError).Should().NotBe(0);

        var (status, data) = backend.IssueUciCommand(new byte[] { 0x01, 0x01 }); // identify

        status.Should().Be("00,OK");
        Encoding.ASCII.GetString(data).Should().Be("ULTIMATE-II DOS V1.2");
    }

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

    public void Dispose()
    {
        _dos.Dispose();
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, true);
    }
}
