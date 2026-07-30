namespace sim6502.Systems.Ultimate;

/// <summary>
/// A command target's answer: response data, an ASCII status string, and whether
/// this is the final part. Mirrors the (reply, status) pair plus Message.last_part
/// in Gideon's CommandTarget interface.
/// </summary>
/// <param name="Data">Response bytes. Empty for commands with no data reply.</param>
/// <param name="Status">ASCII status string. Empty string means "no status".</param>
/// <param name="LastPart">False means the C64 should acknowledge and ask again.</param>
public readonly record struct UciReply(byte[] Data, string Status, bool LastPart)
{
    private static readonly byte[] NoData = Array.Empty<byte>();

    /// <summary>A reply carrying only a status, with no response data.</summary>
    public static UciReply Empty(string status) => new(NoData, status, true);

    /// <summary>A final reply carrying data with an OK status.</summary>
    public static UciReply Ok(byte[] data) => new(data, UciConstants.StatusOk, true);

    /// <summary>A non-final reply; the C64 must acknowledge to get the rest.</summary>
    public static UciReply More(byte[] data, string status) => new(data, status, false);
}
