// sim6502/Systems/IMemoryMap.cs
namespace sim6502.Systems;

/// <summary>
/// Interface for system-specific memory mapping.
/// Allows different systems (C64, Apple II, etc.) to implement
/// their own memory architecture with banking, ROM overlays, etc.
/// </summary>
public interface IMemoryMap
{
    /// <summary>
    /// Read a byte from the specified address.
    /// Returns data from the appropriate source (RAM, ROM, I/O) based on current banking.
    /// </summary>
    byte Read(int address);

    /// <summary>
    /// Write a byte to the specified address.
    /// On systems with ROM overlays, writes always go to underlying RAM.
    /// </summary>
    void Write(int address, byte value);

    /// <summary>
    /// Read a byte without incrementing the cycle counter.
    /// Used by test harness and debugging.
    /// </summary>
    byte ReadWithoutCycle(int address);

    /// <summary>
    /// Write a byte without incrementing the cycle counter.
    /// Used by test harness and initial memory setup.
    /// </summary>
    void WriteWithoutCycle(int address, byte value);

    /// <summary>
    /// Load a ROM image into the specified ROM slot.
    /// </summary>
    /// <param name="name">ROM name (e.g., "kernal", "basic", "chargen")</param>
    /// <param name="data">ROM data bytes</param>
    void LoadRom(string name, byte[] data);

    /// <summary>
    /// Load program data directly into RAM at the specified address.
    /// </summary>
    void LoadProgram(int address, byte[] data);

    /// <summary>
    /// Get the raw RAM array for direct access (used by processor reset, etc.)
    /// </summary>
    byte[] GetRam();

    /// <summary>
    /// Clear all memory (RAM, reset banking to defaults).
    /// </summary>
    void Reset();

    /// <summary>
    /// Action to increment cycle count (delegated from processor).
    /// </summary>
    Action IncrementCycleCount { get; set; }
}
