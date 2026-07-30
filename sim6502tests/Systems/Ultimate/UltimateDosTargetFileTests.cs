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
