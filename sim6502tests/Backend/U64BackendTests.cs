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
