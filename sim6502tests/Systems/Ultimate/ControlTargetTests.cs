using System.Text;
using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class ControlTargetTests : IDisposable
{
    private readonly string _fixture;
    private readonly UltimateDosTarget _dos;
    private readonly ControlTarget _control;

    public ControlTargetTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64sim-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixture);
        File.WriteAllText(Path.Combine(_fixture, "data.bin"), "payload");

        _dos = new UltimateDosTarget(new UltimateFileSystem(_fixture));
        _control = new ControlTarget(new[] { _dos });
    }

    public void Dispose()
    {
        _dos.Dispose();
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    private static byte[] Cmd(byte code, params byte[] rest)
    {
        var bytes = new List<byte> { 0x04, code };
        bytes.AddRange(rest);
        return bytes.ToArray();
    }

    private static string Text(UciReply reply) => Encoding.ASCII.GetString(reply.Data);

    [Fact]
    public void Identify_ReturnsTheVersionString()
    {
        var reply = _control.ParseCommand(Cmd(ControlTarget.CmdIdentify));

        Text(reply).Should().Be("CONTROL TARGET V1.1");
        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void GetHwInfo_ReturnsTheModelName()
    {
        var reply = _control.ParseCommand(Cmd(ControlTarget.CmdGetHwInfo));

        Text(reply).Should().Be("Ultimate 64");
        reply.Status.Should().Be("00,OK");
    }

    [Fact]
    public void GetHwInfo_HonoursAConfiguredModelName()
    {
        var control = new ControlTarget(new[] { _dos }, modelName: "Ultimate-II+");
        Text(control.ParseCommand(Cmd(ControlTarget.CmdGetHwInfo))).Should().Be("Ultimate-II+");
    }

    [Fact]
    public void Reboot_ReportsOkAndCountsTheReboot()
    {
        var reply = _control.ParseCommand(Cmd(ControlTarget.CmdReboot));

        reply.Status.Should().Be("00,OK");
        reply.Data.Should().BeEmpty();
        _control.RebootCount.Should().Be(1);
    }

    [Fact]
    public void Reboot_ClearsDosTargetState()
    {
        var open = new List<byte> { 0x01, UltimateDosTarget.CmdOpenFile, UltimateDosTarget.FileAttributeRead };
        open.AddRange(Encoding.ASCII.GetBytes("data.bin"));
        _dos.ParseCommand(open.ToArray()).Status.Should().Be("00,OK");

        _control.ParseCommand(Cmd(ControlTarget.CmdReboot));

        _dos.ParseCommand(new byte[] { 0x01, UltimateDosTarget.CmdCloseFile })
            .Status.Should().Be("84,NO FILE TO CLOSE", "reboot must close any open file");
    }

    [Fact]
    public void Reboot_ResetsEveryRegisteredDosTarget()
    {
        var second = new UltimateDosTarget(new UltimateFileSystem(_fixture));
        try
        {
            var control = new ControlTarget(new[] { _dos, second });

            foreach (var target in new[] { _dos, second })
            {
                var open = new List<byte> { 0x01, UltimateDosTarget.CmdOpenFile, UltimateDosTarget.FileAttributeRead };
                open.AddRange(Encoding.ASCII.GetBytes("data.bin"));
                target.ParseCommand(open.ToArray()).Status.Should().Be("00,OK");
            }

            control.ParseCommand(Cmd(ControlTarget.CmdReboot));

            foreach (var target in new[] { _dos, second })
                target.ParseCommand(new byte[] { 0x01, UltimateDosTarget.CmdCloseFile })
                      .Status.Should().Be("84,NO FILE TO CLOSE");
        }
        finally
        {
            second.Dispose();
        }
    }

    [Theory]
    [InlineData(ControlTarget.CmdLoadReu)]
    [InlineData(ControlTarget.CmdSaveReu)]
    public void ReuCommands_ReportReuNotEnabled(byte code)
    {
        var reply = _control.ParseCommand(Cmd(code));

        reply.Status.Should().Be("84,REU NOT ENABLED",
            "the REU arrives in a later milestone and must say so plainly");
        reply.Data.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ControlTarget.CmdFinishCapture)]
    [InlineData(ControlTarget.CmdFreeze)]
    [InlineData(ControlTarget.CmdSaveMemory)]
    public void DeferredCommands_ReportNotImplemented(byte code)
    {
        _control.ParseCommand(Cmd(code)).Status.Should().Be("99,FUNCTION NOT IMPLEMENTED");
    }

    [Fact]
    public void UnknownCommand_IsRejected()
    {
        var reply = _control.ParseCommand(Cmd(0x7B));

        reply.Status.Should().Be("21,UNKNOWN COMMAND");
        reply.Data.Should().BeEmpty();
    }

    [Fact]
    public void ShortCommand_IsRejectedRatherThanThrowing()
    {
        _control.ParseCommand(new byte[] { 0x04 }).Status.Should().Be("21,UNKNOWN COMMAND");
    }

    [Fact]
    public void GetMoreData_IsAlwaysAFinalEmptyReply()
    {
        var reply = _control.GetMoreData();

        reply.Data.Should().BeEmpty();
        reply.LastPart.Should().BeTrue();
    }
}
