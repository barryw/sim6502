using System.Text;
using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class UltimateDosTargetInfoTests : IDisposable
{
    private readonly string _fixture;
    private readonly UltimateFileSystem _fs;
    private readonly UltimateDosTarget _dos;

    private static readonly DateTime KnownStamp = new(2024, 3, 17, 14, 25, 36, DateTimeKind.Local);

    public UltimateDosTargetInfoTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64sim-dosinfo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_fixture, "sub"));
        File.WriteAllBytes(Path.Combine(_fixture, "game.prg"), new byte[321]);
        File.WriteAllText(Path.Combine(_fixture, "notes.txt"), "notes");
        File.WriteAllText(Path.Combine(_fixture, "noext"), "x");
        File.SetLastWriteTime(Path.Combine(_fixture, "game.prg"), KnownStamp);

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

    private static byte[] Cmd(byte code, string argument)
    {
        var bytes = new List<byte> { 0x01, code };
        bytes.AddRange(Encoding.ASCII.GetBytes(argument));
        return bytes.ToArray();
    }

    /// <summary>Two NUL-separated names, as RENAME_FILE and COPY_FILE expect.</summary>
    private static byte[] CmdPair(byte code, string first, string second)
    {
        var bytes = new List<byte> { 0x01, code };
        bytes.AddRange(Encoding.ASCII.GetBytes(first));
        bytes.Add(0x00);
        bytes.AddRange(Encoding.ASCII.GetBytes(second));
        return bytes.ToArray();
    }

    private static byte[] OpenCmd(byte attributes, string name)
    {
        var bytes = new List<byte> { 0x01, UltimateDosTarget.CmdOpenFile, attributes };
        bytes.AddRange(Encoding.ASCII.GetBytes(name));
        return bytes.ToArray();
    }

    // ── FILE_STAT ──

    [Fact]
    public void FileStat_ReportsSizeDateTimeExtensionAttributeAndName()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileStat, "game.prg"));

        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
        reply.Data.Should().HaveCount(12 + "game.prg".Length);

        BitConverter.ToUInt32(reply.Data, 0).Should().Be(321);
        BitConverter.ToUInt16(reply.Data, 4).Should().Be(UltimateDosTarget.FatDate(KnownStamp));
        BitConverter.ToUInt16(reply.Data, 6).Should().Be(UltimateDosTarget.FatTime(KnownStamp));
        Encoding.ASCII.GetString(reply.Data, 8, 3).Should().Be("PRG");
        reply.Data[11].Should().Be(UltimateFileSystem.AttributeArchive);
        Encoding.ASCII.GetString(reply.Data, 12, reply.Data.Length - 12).Should().Be("game.prg");
    }

    [Fact]
    public void FileStat_ShortExtension_IsSpacePadded()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileStat, "notes.txt"));
        Encoding.ASCII.GetString(reply.Data, 8, 3).Should().Be("TXT");
    }

    [Fact]
    public void FileStat_NoExtension_IsAllSpaces()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileStat, "noext"));
        Encoding.ASCII.GetString(reply.Data, 8, 3).Should().Be("   ");
    }

    [Fact]
    public void FileStat_Directory_ReportsTheDirectoryAttribute()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileStat, "sub"));

        reply.Status.Should().Be("00,OK");
        reply.Data[11].Should().Be(UltimateFileSystem.AttributeDirectory);
        BitConverter.ToUInt32(reply.Data, 0).Should().Be(0);
    }

    [Fact]
    public void FileStat_Missing_ReportsFileNotFound()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileStat, "nope.txt"));

        reply.Status.Should().Be("82,FILE NOT FOUND");
        reply.Data.Should().BeEmpty();
    }

    [Fact]
    public void FileStat_OutsideTheMount_ReportsFileNotFound()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileStat, "/SdCard/x"))
            .Status.Should().Be("82,FILE NOT FOUND");
    }

    // ── FILE_INFO ──

    [Fact]
    public void FileInfo_DescribesTheOpenFile()
    {
        _dos.ParseCommand(OpenCmd(UltimateDosTarget.FileAttributeRead, "game.prg"));

        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileInfo));

        reply.Status.Should().Be("00,OK");
        BitConverter.ToUInt32(reply.Data, 0).Should().Be(321);
        Encoding.ASCII.GetString(reply.Data, 12, reply.Data.Length - 12).Should().Be("game.prg");
    }

    [Fact]
    public void FileInfo_WithNoOpenFile_ReportsNoFileOpen()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdFileInfo));

        reply.Status.Should().Be("85,NO FILE OPEN");
        reply.Data.Should().BeEmpty();
    }

    // ── FAT date and time encoding ──

    [Fact]
    public void FatDate_PacksYearMonthDay()
    {
        var expected = (ushort)(((2024 - 1980) << 9) | (3 << 5) | 17);
        UltimateDosTarget.FatDate(KnownStamp).Should().Be(expected);
    }

    [Fact]
    public void FatTime_PacksHourMinuteAndTwoSecondUnits()
    {
        var expected = (ushort)((14 << 11) | (25 << 5) | (36 / 2));
        UltimateDosTarget.FatTime(KnownStamp).Should().Be(expected);
    }

    [Fact]
    public void FatDate_BeforeTheFatEpoch_ClampsToZero()
    {
        UltimateDosTarget.FatDate(new DateTime(1970, 1, 1)).Should().Be(0);
    }

    // ── DELETE_FILE ──

    [Fact]
    public void DeleteFile_RemovesIt()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdDeleteFile, "notes.txt"))
            .Status.Should().Be("00,OK");

        File.Exists(_fs.ResolveToHostPath("notes.txt")!).Should().BeFalse();
    }

    [Fact]
    public void DeleteFile_Missing_ReportsFileNotFound()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdDeleteFile, "nope.txt"))
            .Status.Should().Be("82,FILE NOT FOUND");
    }

    [Fact]
    public void DeleteFile_EmptyDirectory_Succeeds()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdDeleteFile, "sub"))
            .Status.Should().Be("00,OK");
        Directory.Exists(_fs.ResolveToHostPath("sub")!).Should().BeFalse();
    }

    [Fact]
    public void DeleteFile_OutsideTheMount_IsRejectedAndTouchesNothing()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdDeleteFile, "/SdCard/x"))
            .Status.Should().Be("82,FILE NOT FOUND");
        File.Exists(Path.Combine(_fixture, "notes.txt")).Should().BeTrue();
    }

    // ── RENAME_FILE ──

    [Fact]
    public void RenameFile_MovesTheName()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdRenameFile, "notes.txt", "memo.txt"))
            .Status.Should().Be("00,OK");

        File.Exists(_fs.ResolveToHostPath("notes.txt")!).Should().BeFalse();
        File.ReadAllText(_fs.ResolveToHostPath("memo.txt")!).Should().Be("notes");
    }

    [Fact]
    public void RenameFile_MissingSource_ReportsFileNotFound()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdRenameFile, "nope.txt", "memo.txt"))
            .Status.Should().Be("82,FILE NOT FOUND");
    }

    [Fact]
    public void RenameFile_OntoAnExistingName_ReportsAnError()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdRenameFile, "notes.txt", "game.prg"))
            .Status.Should().Be("87,INTERNAL ERROR");
        File.Exists(_fs.ResolveToHostPath("notes.txt")!).Should().BeTrue();
    }

    [Fact]
    public void RenameFile_MissingSecondName_ReportsAnError()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdRenameFile, "notes.txt"))
            .Status.Should().Be("87,INTERNAL ERROR");
    }

    [Fact]
    public void RenameFile_DestinationOutsideTheMount_IsRejected()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdRenameFile, "notes.txt", "/SdCard/x"))
            .Status.Should().Be("87,INTERNAL ERROR");
        File.Exists(_fs.ResolveToHostPath("notes.txt")!).Should().BeTrue();
    }

    // ── COPY_FILE ──

    [Fact]
    public void CopyFile_DuplicatesTheContent()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdCopyFile, "notes.txt", "copy.txt"))
            .Status.Should().Be("00,OK");

        File.ReadAllText(_fs.ResolveToHostPath("notes.txt")!).Should().Be("notes");
        File.ReadAllText(_fs.ResolveToHostPath("copy.txt")!).Should().Be("notes");
    }

    [Fact]
    public void CopyFile_MissingSource_ReportsFileNotFound()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdCopyFile, "nope.txt", "copy.txt"))
            .Status.Should().Be("82,FILE NOT FOUND");
    }

    [Fact]
    public void CopyFile_OntoAnExistingName_ReportsAnError()
    {
        _dos.ParseCommand(CmdPair(UltimateDosTarget.CmdCopyFile, "notes.txt", "game.prg"))
            .Status.Should().Be("87,INTERNAL ERROR");
        new FileInfo(_fs.ResolveToHostPath("game.prg")!).Length.Should().Be(321);
    }

    // ── OPEN_DIR and READ_DIR ──

    [Fact]
    public void OpenDir_ReportsOkForAPopulatedDirectory()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdOpenDir));

        reply.Status.Should().Be("00,OK");
        reply.Data.Should().BeEmpty();
    }

    [Fact]
    public void OpenDir_EmptyDirectory_ReportsDirectoryEmpty()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "sub"));

        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdOpenDir))
            .Status.Should().Be("01,DIRECTORY EMPTY");
    }

    [Fact]
    public void ReadDir_YieldsOneEntryPerPartDirectoriesFirst()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdOpenDir));

        var names = new List<string>();
        var attributes = new List<byte>();

        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdReadDir));
        var guard = 0;
        while (true)
        {
            if (++guard > 32) throw new InvalidOperationException("directory read never terminated");
            attributes.Add(reply.Data[0]);
            names.Add(Encoding.ASCII.GetString(reply.Data, 1, reply.Data.Length - 1));
            if (reply.LastPart) break;
            reply = _dos.GetMoreData();
        }

        names.Should().Equal("sub", "game.prg", "noext", "notes.txt");
        attributes[0].Should().Be(UltimateFileSystem.AttributeDirectory);
        attributes.Skip(1).Should().AllBeEquivalentTo(UltimateFileSystem.AttributeArchive);
    }

    [Fact]
    public void ReadDir_NonFinalPartsCarryNoStatusFinalPartCarriesOk()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdOpenDir));

        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdReadDir));
        reply.LastPart.Should().BeFalse();
        reply.Status.Should().BeEmpty();

        var guard = 0;
        while (!reply.LastPart)
        {
            if (++guard > 32) throw new InvalidOperationException("directory read never terminated");
            reply = _dos.GetMoreData();
        }
        reply.Status.Should().Be("00,OK");
    }

    [Fact]
    public void ReadDir_WithoutOpenDir_ReportsCannotReadDirectory()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdReadDir))
            .Status.Should().Be("86,CAN'T READ DIRECTORY");
    }

    [Fact]
    public void ReadDir_AfterCompletion_LeavesDataMode()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdOpenDir));
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdReadDir));
        var guard = 0;
        while (!reply.LastPart)
        {
            if (++guard > 32) throw new InvalidOperationException("directory read never terminated");
            reply = _dos.GetMoreData();
        }

        _dos.GetMoreData().Status.Should().Be("81,NOT IN DATA MODE");
    }

    [Fact]
    public void ReadDir_SingleEntryDirectory_IsImmediatelyTheLastPart()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "sub"));
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCreateDir, "only"));
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdOpenDir)).Status.Should().Be("00,OK");

        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdReadDir));

        reply.LastPart.Should().BeTrue();
        reply.Status.Should().Be("00,OK");
        Encoding.ASCII.GetString(reply.Data, 1, reply.Data.Length - 1).Should().Be("only");
    }

    // ── Commands deferred to later milestones ──

    [Theory]
    [InlineData(UltimateDosTarget.CmdCopyUiPath)]
    [InlineData(UltimateDosTarget.CmdCopyHomePath)]
    [InlineData(UltimateDosTarget.CmdLoadReu)]
    [InlineData(UltimateDosTarget.CmdSaveReu)]
    [InlineData(UltimateDosTarget.CmdMountDisk)]
    [InlineData(UltimateDosTarget.CmdUnmountDisk)]
    [InlineData(UltimateDosTarget.CmdSwapDisk)]
    [InlineData(UltimateDosTarget.CmdGetTime)]
    [InlineData(UltimateDosTarget.CmdSetTime)]
    public void DeferredCommands_ReportNotImplementedRatherThanUnknown(byte code)
    {
        var reply = _dos.ParseCommand(Cmd(code));

        reply.Status.Should().Be("99,FUNCTION NOT IMPLEMENTED",
            "a recognised-but-deferred command must not look like a typo");
        reply.Data.Should().BeEmpty();
        reply.LastPart.Should().BeTrue();
    }
}
