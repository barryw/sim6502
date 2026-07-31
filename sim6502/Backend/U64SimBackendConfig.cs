namespace sim6502.Backend;

/// <summary>Configuration for the simulated Ultimate 64 backend.</summary>
public class U64SimBackendConfig
{
    /// <summary>
    /// Host directory exposed to the C64 as the Ultimate's mount (see
    /// <see cref="MountName"/>). Required. The tree is copied to a temporary
    /// location, so the fixture is never mutated.
    /// </summary>
    public string FsRoot { get; set; } = "";

    /// <summary>
    /// CPU cycles the UCI holds the Busy state before a response becomes readable.
    ///
    /// Deliberately non-zero. The real UCI is asynchronous and a client must poll
    /// $DF1C while the state is Busy; if the simulator answered instantly, a client
    /// with a broken or missing busy-wait loop would pass here and fail on
    /// hardware. Set to 0 only when a test is specifically not about timing.
    /// </summary>
    public int UciLatencyCycles { get; set; } = 64;

    /// <summary>String the DOS targets return for IDENTIFY.</summary>
    public string DosVersion { get; set; } = "ULTIMATE-II DOS V1.2";

    /// <summary>String the control target returns for GET_HWINFO.</summary>
    public string ModelName { get; set; } = "Ultimate 64";

    /// <summary>
    /// SoftIEC bus ID reported at $DF1B. Real hardware reports 11 by default.
    /// </summary>
    public byte BusId { get; set; } = 11;

    /// <summary>
    /// Ultimate-side mount name for <see cref="FsRoot"/>, without the leading
    /// slash. Defaults to the historical "Usb0". Real hardware enumerates its
    /// stick as "USB1", so a suite meant to run against both backends should set
    /// this to match the machine.
    /// </summary>
    public string MountName { get; set; } = "Usb0";
}
