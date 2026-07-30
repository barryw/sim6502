using FluentAssertions;
using sim6502.Systems;
using Xunit;

namespace sim6502tests.Systems;

public class C64MemoryMapIoHandlerTests
{
    private sealed class RecordingHandler : IIOHandler
    {
        public List<(int Address, byte Value)> Writes { get; } = new();
        public byte ReadValue { get; set; } = 0xC9;
        public List<int> Reads { get; } = new();

        public byte Read(int address)
        {
            Reads.Add(address);
            return ReadValue;
        }

        public void Write(int address, byte value) => Writes.Add((address, value));
    }

    [Fact]
    public void RegisterIoHandler_ReadInRange_GoesToHandler()
    {
        var map = new C64MemoryMap();
        var handler = new RecordingHandler { ReadValue = 0xC9 };
        map.RegisterIoHandler(0xDF1B, 0xDF1F, handler);

        map.ReadWithoutCycle(0xDF1D).Should().Be(0xC9);
        handler.Reads.Should().ContainSingle().Which.Should().Be(0xDF1D);
    }

    [Fact]
    public void RegisterIoHandler_WriteInRange_GoesToHandlerAndAlsoToRam()
    {
        var map = new C64MemoryMap();
        var handler = new RecordingHandler();
        map.RegisterIoHandler(0xDF1B, 0xDF1F, handler);

        map.WriteWithoutCycle(0xDF1C, 0x01);

        handler.Writes.Should().ContainSingle().Which.Should().Be((0xDF1C, (byte)0x01));
        // Writes under I/O always reach RAM on a C64. Bank RAM in to observe it.
        map.WriteWithoutCycle(0x01, 0x30);   // LORAM=0, HIRAM=0 -> $D000-$DFFF is RAM
        map.ReadWithoutCycle(0xDF1C).Should().Be(0x01);
    }

    [Fact]
    public void ReadOutsideHandlerRange_StillUsesFlatIoRegisters()
    {
        var map = new C64MemoryMap();
        map.RegisterIoHandler(0xDF1B, 0xDF1F, new RecordingHandler());

        map.WriteWithoutCycle(0xD020, 0x0E);
        map.ReadWithoutCycle(0xD020).Should().Be(0x0E);
    }

    [Fact]
    public void HandlerNotConsultedWhenIoIsBankedOut()
    {
        var map = new C64MemoryMap();
        var handler = new RecordingHandler { ReadValue = 0xC9 };
        map.RegisterIoHandler(0xDF1B, 0xDF1F, handler);

        map.WriteWithoutCycle(0x01, 0x30);          // all RAM
        map.ReadWithoutCycle(0xDF1D).Should().Be(0x00);
        handler.Reads.Should().BeEmpty();
    }

    [Fact]
    public void GenericMemoryMap_RegisterIoHandler_Throws()
    {
        // Default interface implementations only resolve through an interface-typed
        // reference, not through the concrete class, hence IMemoryMap here.
        IMemoryMap map = new GenericMemoryMap();
        var act = () => map.RegisterIoHandler(0xDF1B, 0xDF1F, new RecordingHandler());
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Reset_ClearsRegisteredHandlers()
    {
        var map = new C64MemoryMap();
        var handler = new RecordingHandler { ReadValue = 0xC9 };
        map.RegisterIoHandler(0xDF1B, 0xDF1F, handler);
        map.Reset();

        map.ReadWithoutCycle(0xDF1D).Should().Be(0x00);
        handler.Reads.Should().BeEmpty();
    }
}
