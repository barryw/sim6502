using sim6502;
using FluentAssertions;
using sim6502.Backend;
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

    /// <summary>
    /// Undocumented opcodes execute. The previous engine registered 151 of the 256 and
    /// threw "The OpCode .. is not supported" for the rest, which aborted the whole run —
    /// so any suite covering hand-optimised code that uses LAX, SAX or DCP could not be
    /// written at all. SixtyFiveXX implements all 256 per variant.
    /// </summary>
    [Fact]
    public void UndocumentedOpcodes_Execute()
    {
        using var backend = CreateBackend();
        backend.WriteByte(0x0010, 0x42);
        backend.WriteByte(0xC000, 0xA7);   // LAX $10 — undocumented: loads A and X at once
        backend.WriteByte(0xC001, 0x10);
        backend.WriteByte(0xC002, 0x60);   // RTS

        backend.ExecuteJsr(0xC000, 0, true, true);

        backend.GetRegister("a").Should().Be(0x42);
        backend.GetRegister("x").Should().Be(0x42);
    }

    [Fact]
    public void ProcessorType_IsReported()
    {
        using var backend = CreateBackend();
        backend.ProcessorType.Should().Be(ProcessorType.MOS6502);
    }

    /// <summary>
    /// The 6510 runs the 6502's instruction set — the two share one opcode table — so the
    /// same program must behave identically. What makes a 6510 a 6510 here is the $00/$01
    /// port, and that belongs to the memory map, not to the core.
    /// </summary>
    [Fact]
    public void Mos6510_RunsTheSameInstructionSetAsThe6502()
    {
        using var sixtyFiveOhTwo = CreateBackend(ProcessorType.MOS6502);
        using var sixtyFiveTen = CreateBackend(ProcessorType.MOS6510);

        foreach (var backend in new[] { sixtyFiveOhTwo, sixtyFiveTen })
        {
            backend.WriteByte(0xC000, 0xA9);   // LDA #$42
            backend.WriteByte(0xC001, 0x42);
            backend.WriteByte(0xC002, 0x60);   // RTS
            backend.ResetCycleCount();
            backend.ExecuteJsr(0xC000, 0, true, true);
        }

        sixtyFiveTen.GetRegister("a").Should().Be(sixtyFiveOhTwo.GetRegister("a"));
        sixtyFiveTen.GetCycles().Should().Be(sixtyFiveOhTwo.GetCycles());
    }

    /// <summary>
    /// A 6510 test suite that switches C64 banking writes $01, and that write has to reach
    /// the memory map. It would not if the core answered $00/$01 itself, which is exactly
    /// what SixtyFiveXX's 6510 variant does — hence the 6502 variant here.
    /// </summary>
    [Fact]
    public void Mos6510_PortWritesReachTheMemoryMap()
    {
        var map = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6510).map;
        using var backend = new SimulatorBackend(ProcessorType.MOS6510, map);

        backend.WriteByte(0xC000, 0xA9);   // LDA #$35
        backend.WriteByte(0xC001, 0x35);
        backend.WriteByte(0xC002, 0x85);   // STA $01
        backend.WriteByte(0xC003, 0x01);
        backend.WriteByte(0xC004, 0x60);   // RTS
        backend.ExecuteJsr(0xC000, 0, true, true);

        map.ReadWithoutCycle(0x01).Should().Be(0x35);
    }
}
