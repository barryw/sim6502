// sim6502/Systems/IIOHandler.cs
namespace sim6502.Systems;

/// <summary>
/// Interface for handling I/O region reads and writes.
/// Systems can register handlers for specific address ranges.
/// </summary>
public interface IIOHandler
{
    /// <summary>
    /// Read from an I/O address.
    /// </summary>
    /// <param name="address">The I/O address being read</param>
    /// <returns>The value at that address</returns>
    byte Read(int address);

    /// <summary>
    /// Write to an I/O address.
    /// </summary>
    /// <param name="address">The I/O address being written</param>
    /// <param name="value">The value to write</param>
    void Write(int address, byte value);
}
