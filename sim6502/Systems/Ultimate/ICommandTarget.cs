// Ported from GideonZ/1541ultimate (GPL-3.0):
//   software/io/command_interface/command_intf.h  (class CommandTarget)
// Original author: Gideon Zweijtzer. See NOTICE.

namespace sim6502.Systems.Ultimate;

/// <summary>
/// A UCI command target. Byte 0 of every command selects one of these by its low
/// nibble. Targets know what commands mean; UciRegisters knows only the protocol.
/// </summary>
public interface ICommandTarget
{
    /// <summary>
    /// Handle a complete command. <paramref name="command"/> includes byte 0
    /// (target) and byte 1 (command code).
    /// </summary>
    UciReply ParseCommand(byte[] command);

    /// <summary>
    /// Supply the next part after the C64 acknowledged a non-final reply.
    /// </summary>
    UciReply GetMoreData();

    /// <summary>
    /// The C64 aborted mid-transfer. <paramref name="bytesConsumed"/> is how many
    /// response bytes it had already read.
    /// </summary>
    void Abort(int bytesConsumed);
}

/// <summary>
/// Stand-in for unpopulated target slots. Answers IDENTIFY with "NO TARGET" and
/// rejects everything else, matching cmd_if_empty_target upstream.
/// </summary>
public sealed class EmptyCommandTarget : ICommandTarget
{
    public const byte CommandIdentify = 0x01;

    public UciReply ParseCommand(byte[] command)
    {
        if (command.Length > 1 && command[1] == CommandIdentify)
            return new UciReply(
                System.Text.Encoding.ASCII.GetBytes(UciConstants.MessageNoTarget),
                UciConstants.StatusOk,
                true);

        return UciReply.Empty(UciConstants.StatusUnknownCommand);
    }

    public UciReply GetMoreData() => UciReply.Empty(UciConstants.StatusOk);

    public void Abort(int bytesConsumed) { }
}
