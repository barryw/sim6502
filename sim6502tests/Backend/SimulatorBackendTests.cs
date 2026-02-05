using FluentAssertions;
using sim6502.Backend;
using sim6502.Proc;
using sim6502.Systems;
using Xunit;

namespace sim6502tests.Backend;

public class SimulatorBackendTests
{
    private SimulatorBackend CreateBackend(
        ProcessorType type = ProcessorType.MOS6502,
        IMemoryMap? memoryMap = null)
    {
        memoryMap ??= MemoryMapFactory.CreateForProcessor(type).map;
        return new SimulatorBackend(type, memoryMap);
    }

    [Fact]
    public void WriteByte_ReadByte_RoundTrips()
    {
        using var backend = CreateBackend();
        backend.WriteByte(0x1000, 0xAB);
        backend.ReadByte(0x1000).Should().Be(0xAB);
    }

    [Fact]
    public void WriteWord_ReadWord_RoundTrips()
    {
        using var backend = CreateBackend();
        backend.WriteWord(0x2000, 0xABCD);
        backend.ReadWord(0x2000).Should().Be(0xABCD);
    }

    [Fact]
    public void SetRegister_GetRegister_RoundTrips()
    {
        using var backend = CreateBackend();
        backend.SetRegister("a", 0x42);
        backend.GetRegister("a").Should().Be(0x42);

        backend.SetRegister("x", 0x10);
        backend.GetRegister("x").Should().Be(0x10);

        backend.SetRegister("y", 0xFF);
        backend.GetRegister("y").Should().Be(0xFF);
    }

    [Fact]
    public void SetFlag_GetFlag_RoundTrips()
    {
        using var backend = CreateBackend();
        backend.SetFlag("c", true);
        backend.GetFlag("c").Should().BeTrue();

        backend.SetFlag("z", true);
        backend.GetFlag("z").Should().BeTrue();
    }

    [Fact]
    public void LoadBinary_WritesToMemory()
    {
        using var backend = CreateBackend();
        var data = new byte[] { 0x01, 0x02, 0x03 };
        backend.LoadBinary(data, 0xC000);
        backend.ReadByte(0xC000).Should().Be(0x01);
        backend.ReadByte(0xC001).Should().Be(0x02);
        backend.ReadByte(0xC002).Should().Be(0x03);
    }

    [Fact]
    public void ExecuteJsr_SimpleRts_ReturnsCleanly()
    {
        using var backend = CreateBackend();
        // Write a simple RTS at $C000
        backend.WriteByte(0xC000, 0x60); // RTS
        var result = backend.ExecuteJsr(0xC000, 0, true, true);
        result.ExitedCleanly.Should().BeTrue();
        result.Reason.Should().Be(StopReason.Rts);
    }

    [Fact]
    public void ExecuteJsr_Brk_FailsWhenFailOnBrk()
    {
        using var backend = CreateBackend();
        // Write BRK at $C000
        backend.WriteByte(0xC000, 0x00); // BRK
        var result = backend.ExecuteJsr(0xC000, 0, true, true);
        result.ExitedCleanly.Should().BeFalse();
        result.Reason.Should().Be(StopReason.Brk);
    }

    [Fact]
    public void GetCycles_TracksCycles()
    {
        using var backend = CreateBackend();
        backend.ResetCycleCount();
        // NOP + RTS = some cycles
        backend.WriteByte(0xC000, 0xEA); // NOP
        backend.WriteByte(0xC001, 0x60); // RTS
        backend.ExecuteJsr(0xC000, 0, true, true);
        backend.GetCycles().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Processor_IsAccessible()
    {
        using var backend = CreateBackend();
        backend.Processor.Should().NotBeNull();
        backend.Processor.ProcessorType.Should().Be(ProcessorType.MOS6502);
    }
}
