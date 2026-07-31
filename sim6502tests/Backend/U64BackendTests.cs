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

    public U64BackendTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64backend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_fixture, "data"));
        File.WriteAllText(Path.Combine(_fixture, "data", "hello.txt"), "HELLO FROM USB0");
        // UltimateFileSystem snapshots this directory at construction time (see
        // below), so every fixture file must exist before that call, not inside
        // an individual test method.
        File.WriteAllBytes(Path.Combine(_fixture, "data", "binary.prg"), BinaryPayload);

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
        using var backend = Build(out _, latency: 64);

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

    public void Dispose()
    {
        _dos.Dispose();
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, true);
    }
}
