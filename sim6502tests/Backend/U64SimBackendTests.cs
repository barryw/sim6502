using System.Text;
using FluentAssertions;
using sim6502.Backend;
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

        // Deliberately larger than UltimateDosTarget.ReadChunkSize (512), so a read
        // of it genuinely spans continuation parts rather than completing in one.
        File.WriteAllBytes(Path.Combine(_fixture, "data", "big.bin"), BigPayload);
    }

    /// <summary>1300 bytes — three 512-byte chunks, the last one partial.</summary>
    private static readonly byte[] BigPayload =
        Enumerable.Range(0, 1300).Select(i => (byte)(i & 0xFF)).ToArray();

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
        open.AddRange(Encoding.ASCII.GetBytes("big.bin"));
        backend.IssueUciCommand(open.ToArray()).Status.Should().Be("00,OK");

        // 1300 bytes at a 512-byte chunk size means IssueHostCommand must walk two
        // continuations to assemble the reply. A fixture under the chunk size would
        // complete on the first part and never reach GetMoreData at all.
        var length = BigPayload.Length;
        var read = backend.IssueUciCommand(new byte[]
        {
            0x01, UltimateDosTarget.CmdReadData, (byte)(length & 0xFF), (byte)((length >> 8) & 0xFF)
        });

        read.Data.Should().HaveCount(length, "the reply must be reassembled from every part");
        read.Data.Should().Equal(BigPayload);
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
}
