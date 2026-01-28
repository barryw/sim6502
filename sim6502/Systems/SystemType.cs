// sim6502/Systems/SystemType.cs
namespace sim6502.Systems;

/// <summary>
/// Supported system types for emulation.
/// Each system has its own memory map, banking, and I/O behavior.
/// </summary>
public enum SystemType
{
    /// <summary>
    /// Generic 6502 system - flat 64KB RAM, no banking (default, backward compatible)
    /// </summary>
    Generic6502 = 0,

    /// <summary>
    /// Generic 6510 system - flat 64KB RAM with $00/$01 I/O port
    /// </summary>
    Generic6510 = 1,

    /// <summary>
    /// Generic 65C02 system - flat 64KB RAM, no banking
    /// </summary>
    Generic65C02 = 2,

    /// <summary>
    /// Commodore 64 - 6510 with full banking via $01, ROM overlays, I/O at $D000-$DFFF
    /// </summary>
    C64 = 10
}
