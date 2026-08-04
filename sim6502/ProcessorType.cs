namespace sim6502;

/// <summary>
/// The processors a test suite can ask for, as spelled in the DSL's
/// <c>processor(...)</c> and <c>system(...)</c> declarations.
/// </summary>
/// <remarks>
/// <para>
/// This is sim6502's own vocabulary, deliberately not SixtyFiveXX's <c>CpuVariant</c>.
/// The DSL offers three processors; the emulator core offers five, and coupling a
/// language's surface to a dependency's enum would mean every core added there became a
/// silent addition to the language here.
/// </para>
/// <para>
/// It lives outside <c>Proc/</c> because that directory was deleted when the simulator
/// moved onto SixtyFiveXX, and this type outlived it. It carried the Aaron Mell BSD
/// header until then, but that header arrived by copy-paste: the file was added in
/// <c>639a149</c> and names processors the original 6502-only simulator had no concept of.
/// </para>
/// </remarks>
public enum ProcessorType
{
    /// <summary>MOS Technology 6502 — the original NMOS part, and the default.</summary>
    MOS6502 = 0,

    /// <summary>
    /// MOS Technology 6510 — the 6502 with the on-chip <c>$00</c>/<c>$01</c> port.
    /// The instruction set is the 6502's; the port belongs to the memory map.
    /// </summary>
    MOS6510 = 1,

    /// <summary>WDC 65C02 — the CMOS part, with its additional opcodes.</summary>
    WDC65C02 = 2
}
