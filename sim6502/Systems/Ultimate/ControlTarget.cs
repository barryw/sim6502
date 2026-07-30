// Ported from GideonZ/1541ultimate (GPL-3.0):
//   software/io/command_interface/control_target.h   (command codes)
//   software/io/command_interface/control_target.cc  (identification and status strings)
//   software/system/product.cc                       (the model name)
// Also documented in GideonZ/1541u-documentation uci/control_target.rst.
// Original author: Gideon Zweijtzer. See NOTICE.

using System.Text;
using NLog;

namespace sim6502.Systems.Ultimate;

/// <summary>
/// The UCI control target, served at target $04. Only the commands that are
/// meaningful without an REU or a real machine are implemented; the rest report
/// their absence explicitly rather than looking like unrecognised requests.
/// </summary>
public sealed class ControlTarget : ICommandTarget
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    public const byte CmdIdentify      = 0x01;
    public const byte CmdFinishCapture = 0x03;
    public const byte CmdFreeze        = 0x05;
    public const byte CmdReboot        = 0x06;
    public const byte CmdLoadReu       = 0x08;
    public const byte CmdSaveReu       = 0x09;
    public const byte CmdSaveMemory    = 0x0F;
    public const byte CmdGetHwInfo     = 0x28;

    public const string StatusReuNotEnabled = "84,REU NOT ENABLED";

    private readonly UltimateDosTarget[] _dosTargets;
    private readonly string _modelName;
    private readonly string _version;

    public ControlTarget(
        IEnumerable<UltimateDosTarget> dosTargets,
        string modelName = "Ultimate 64",
        string version = "CONTROL TARGET V1.1")
    {
        _dosTargets = (dosTargets ?? throw new ArgumentNullException(nameof(dosTargets))).ToArray();
        _modelName = modelName;
        _version = version;
    }

    /// <summary>How many REBOOT commands have been handled.</summary>
    public int RebootCount { get; private set; }

    public UciReply ParseCommand(byte[] command)
    {
        if (command.Length < 2)
        {
            Logger.Warn("Control: command shorter than two bytes");
            return UciReply.Empty(UciConstants.StatusUnknownCommand);
        }

        return command[1] switch
        {
            CmdIdentify  => UciReply.Ok(Encoding.ASCII.GetBytes(_version)),
            CmdGetHwInfo => UciReply.Ok(Encoding.ASCII.GetBytes(_modelName)),
            CmdReboot    => Reboot(),

            // The REU is a later milestone. Answering with the documented
            // "not enabled" status is what real hardware reports when no REU is
            // configured, so client code takes the same path it would there.
            CmdLoadReu or CmdSaveReu => UciReply.Empty(StatusReuNotEnabled),

            CmdFinishCapture or CmdFreeze or CmdSaveMemory
                => UciReply.Empty(UciConstants.StatusNotImplemented),

            _ => UciReply.Empty(UciConstants.StatusUnknownCommand)
        };
    }

    public UciReply GetMoreData() => UciReply.Empty(UciConstants.StatusOk);

    public void Abort(int bytesConsumed) { }

    private UciReply Reboot()
    {
        RebootCount++;
        Logger.Info($"Control: reboot ({RebootCount})");

        foreach (var dos in _dosTargets)
            dos.ResetState();

        return UciReply.Empty(UciConstants.StatusOk);
    }
}
