namespace sim6502;

/// <summary>
/// A file a suite needs in memory before its tests run — the program under test, and any
/// ROMs alongside it.
/// </summary>
/// <remarks>
/// The processor is reset between tests, so these are reloaded each time rather than
/// loaded once. It lives outside <c>Proc/</c> because that directory went when the
/// simulator moved onto SixtyFiveXX; nothing about this type was ever processor code.
/// </remarks>
public class LoadableResource
{
    /// <summary>The file to load.</summary>
    public string Filename { get; set; }

    /// <summary>
    /// Where to load it. For a <c>.prg</c> the address is the file's own first two bytes,
    /// in which case set <see cref="StripHeader"/> so those bytes are not loaded as data.
    /// </summary>
    public int LoadAddress { get; set; }

    /// <summary>
    /// Whether to drop the first two bytes. True for a <c>.prg</c>, whose first two bytes
    /// are its load address; false for a raw ROM image such as KERNAL or BASIC.
    /// </summary>
    public bool StripHeader { get; set; } = false;
}
