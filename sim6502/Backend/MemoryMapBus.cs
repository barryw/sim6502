using SixtyFiveXX;
using sim6502.Systems;

namespace sim6502.Backend;

/// <summary>
/// Presents an <see cref="IMemoryMap"/> to a SixtyFiveXX core as its bus.
/// </summary>
/// <remarks>
/// <para>
/// Every access the processor makes goes through the memory map's <em>cycle-counting</em>
/// <see cref="IMemoryMap.Read"/> and <see cref="IMemoryMap.Write"/> rather than the
/// <c>WithoutCycle</c> pair. That is not about counting — SixtyFiveXX counts its own
/// cycles and is the authority for <c>GetCycles()</c> — it is because those are the
/// methods that <em>do</em> anything. C64 banking, ROM overlays and registered I/O
/// handlers all hang off them, and routing the core through the quiet pair would leave a
/// C64 permanently in its power-on bank.
/// </para>
/// <para>
/// The map's own <see cref="IMemoryMap.IncrementCycleCount"/> is therefore left as the
/// no-op it defaults to. Wiring it to anything would double-count: the core already
/// counts every cycle it takes, including the dummy reads and writes a map cannot see.
/// </para>
/// <para>
/// This is a class rather than a struct because a map is shared, mutable state that the
/// backend also reaches directly. The core wraps it in <c>RefBus</c>, which exists for
/// exactly this — one virtual call per access, in exchange for a bus chosen at runtime.
/// </para>
/// </remarks>
internal sealed class MemoryMapBus(IMemoryMap map) : IBus
{
    /// <summary>The underlying map, for accesses that must not look like processor cycles.</summary>
    public IMemoryMap Map { get; } = map;

    /// <inheritdoc />
    public byte Read(int address) => Map.Read(address & 0xFFFF);

    /// <inheritdoc />
    public void Write(int address, byte value) => Map.Write(address & 0xFFFF, value);
}
