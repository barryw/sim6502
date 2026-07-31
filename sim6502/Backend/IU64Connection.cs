namespace sim6502.Backend;

/// <summary>
/// Transport to an Ultimate 64's REST API, mirroring the IViceConnection seam so
/// the backend can be tested without hardware.
///
/// Implementations MUST serialize requests. Concurrent requests can lock a real
/// machine up, and that is not something callers can be relied on to respect.
/// </summary>
public interface IU64Connection : IDisposable
{
    /// <summary>
    /// Read exactly one byte. Single-byte reads are mandatory around the UCI
    /// registers: a read spanning $DF1E/$DF1F pops those FIFOs.
    /// </summary>
    byte ReadByte(int address);

    /// <summary>Write exactly one byte.</summary>
    void WriteByte(int address, byte value);

    /// <summary>Read an ascending span. Not safe across FIFO ports.</summary>
    byte[] ReadBytes(int address, int length);

    /// <summary>Write an ascending span, chunked to the firmware's limit.</summary>
    void WriteBytes(int address, byte[] data);

    /// <summary>Reset the C64. Does not restart the Ultimate's own firmware tasks.</summary>
    void ResetMachine();
}
