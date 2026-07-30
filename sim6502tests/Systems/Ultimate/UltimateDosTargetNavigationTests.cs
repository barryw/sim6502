using System.Text;
using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class UltimateDosTargetNavigationTests : IDisposable
{
    private readonly string _fixture;
    private readonly UltimateFileSystem _fs;
    private readonly UltimateDosTarget _dos;

    public UltimateDosTargetNavigationTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64sim-dosnav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_fixture, "data", "nested"));
        File.WriteAllText(Path.Combine(_fixture, "hello.txt"), "hello");

        _fs = new UltimateFileSystem(_fixture);
        _dos = new UltimateDosTarget(_fs);
    }

    public void Dispose()
    {
        _fs.Dispose();
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    /// <summary>Build a command: target byte, command byte, then an ASCII argument.</summary>
    private static byte[] Cmd(byte code, string? argument = null)
    {
        var bytes = new List<byte> { 0x01, code };
        if (argument != null) bytes.AddRange(Encoding.ASCII.GetBytes(argument));
        return bytes.ToArray();
    }

    private static string Text(UciReply reply) => Encoding.ASCII.GetString(reply.Data);

    [Fact]
    public void Identify_ReturnsTheVersionStringWithOkStatus()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdIdentify));

        Text(reply).Should().Be("ULTIMATE-II DOS V1.2");
        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void Identify_HonoursAConfiguredVersion()
    {
        var dos = new UltimateDosTarget(_fs, "ULTIMATE-II DOS V1.1");
        Text(dos.ParseCommand(Cmd(UltimateDosTarget.CmdIdentify)))
            .Should().Be("ULTIMATE-II DOS V1.1");
    }

    [Fact]
    public void UnknownCommand_IsRejectedWithNoData()
    {
        var reply = _dos.ParseCommand(Cmd(0x7E));

        reply.Data.Should().BeEmpty();
        reply.Status.Should().Be("21,UNKNOWN COMMAND");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void ChangeDir_Relative_MovesAndReportsOk()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "data"));

        reply.Status.Should().Be("00,OK");
        reply.Data.Should().BeEmpty();
        _fs.CurrentPath.Should().Be("/Usb0/data");
    }

    [Fact]
    public void ChangeDir_Absolute_Moves()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "/Usb0/data/nested"))
            .Status.Should().Be("00,OK");
        _fs.CurrentPath.Should().Be("/Usb0/data/nested");
    }

    [Fact]
    public void ChangeDir_DotAndDotDot_Work()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "data/nested"));
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, ".")).Status.Should().Be("00,OK");
        _fs.CurrentPath.Should().Be("/Usb0/data/nested");

        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "..")).Status.Should().Be("00,OK");
        _fs.CurrentPath.Should().Be("/Usb0/data");
    }

    [Fact]
    public void ChangeDir_Nonexistent_FailsAndLeavesThePathAlone()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "data"));

        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "nope"));

        reply.Status.Should().Be("83,NO SUCH DIRECTORY");
        reply.Data.Should().BeEmpty();
        _fs.CurrentPath.Should().Be("/Usb0/data", "a failed cd must not move the path");
    }

    [Fact]
    public void ChangeDir_IntoAFile_Fails()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "hello.txt"))
            .Status.Should().Be("83,NO SUCH DIRECTORY");
        _fs.CurrentPath.Should().Be("/Usb0");
    }

    [Fact]
    public void ChangeDir_TraversalAttempt_CannotEscapeTheMount()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "../../.."));
        _fs.CurrentPath.Should().Be("/Usb0");
    }

    [Fact]
    public void GetPath_ReturnsTheCurrentPath()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "data"));

        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdGetPath));

        Text(reply).Should().Be("/Usb0/data");
        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void GetPath_AtRoot_ReturnsTheMountRoot()
    {
        Text(_dos.ParseCommand(Cmd(UltimateDosTarget.CmdGetPath))).Should().Be("/Usb0");
    }

    [Fact]
    public void CreateDir_MakesTheDirectory()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCreateDir, "fresh"));

        reply.Status.Should().Be("00,OK");
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "fresh")).Status.Should().Be("00,OK");
        _fs.CurrentPath.Should().Be("/Usb0/fresh");
    }

    [Fact]
    public void CreateDir_AlreadyExisting_ReportsAnError()
    {
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCreateDir, "twice")).Status.Should().Be("00,OK");
        _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCreateDir, "twice"))
            .Status.Should().Be("87,INTERNAL ERROR");
    }

    [Fact]
    public void CreateDir_OutsideTheMount_IsRejected()
    {
        var reply = _dos.ParseCommand(Cmd(UltimateDosTarget.CmdCreateDir, "/SdCard/evil"));
        reply.Status.Should().Be("83,NO SUCH DIRECTORY");
        Directory.Exists(Path.Combine(_fixture, "..", "SdCard")).Should().BeFalse();
    }

    [Fact]
    public void Echo_ReturnsTheWholeCommandIncludingTheHeaderBytes()
    {
        var command = new byte[] { 0x01, UltimateDosTarget.CmdEcho, 0xDE, 0xAD, 0xBE, 0xEF };

        var reply = _dos.ParseCommand(command);

        reply.Data.Should().Equal(command);
        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void GetMoreData_WhenIdle_ReportsNotInDataMode()
    {
        var reply = _dos.GetMoreData();

        reply.Data.Should().BeEmpty();
        reply.Status.Should().Be("81,NOT IN DATA MODE");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void TwoTargets_HaveIndependentPaths()
    {
        var second = new UltimateDosTarget(new UltimateFileSystem(_fixture));
        try
        {
            _dos.ParseCommand(Cmd(UltimateDosTarget.CmdChangeDir, "data"));

            Text(second.ParseCommand(Cmd(UltimateDosTarget.CmdGetPath))).Should().Be("/Usb0");
            Text(_dos.ParseCommand(Cmd(UltimateDosTarget.CmdGetPath))).Should().Be("/Usb0/data");
        }
        finally
        {
            second.Dispose();
        }
    }

    [Fact]
    public void ShortCommand_IsRejectedRatherThanThrowing()
    {
        var reply = _dos.ParseCommand(new byte[] { 0x01 });
        reply.Status.Should().Be("21,UNKNOWN COMMAND");
    }
}
