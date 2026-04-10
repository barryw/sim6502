using FluentAssertions;
using sim6502.Backend;
using sim6502.Proc;
using sim6502.Systems;
using Xunit;

namespace sim6502tests.Backend;

public class BackendFactoryTests
{
    [Fact]
    public void Create_Sim_ReturnsSimulatorBackend()
    {
        var (memMap, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6502);
        var backend = BackendFactory.Create("sim", ProcessorType.MOS6502, memMap);
        backend.Should().BeOfType<SimulatorBackend>();
        backend.Dispose();
    }

    [Fact]
    public void Create_SimCaseInsensitive_Works()
    {
        var (memMap, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6502);
        var backend = BackendFactory.Create("SIM", ProcessorType.MOS6502, memMap);
        backend.Should().BeOfType<SimulatorBackend>();
        backend.Dispose();
    }

    [Fact]
    public void Create_NovaVm_ThrowsWhenNoEmulatorRunning()
    {
        var (memMap, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6502);
        var config = new NovaVmBackendConfig { Host = "127.0.0.1", Port = 19999 };

        // Should fail to connect since no emulator is running on that port
        var act = () => BackendFactory.Create("novavm", ProcessorType.MOS6502, memMap, novaVmConfig: config);
        act.Should().Throw<Exception>(); // Connection refused or similar
    }

    [Fact]
    public void Create_UnknownBackend_ThrowsArgumentException()
    {
        var (memMap, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6502);

        var act = () => BackendFactory.Create("bogus", ProcessorType.MOS6502, memMap);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown backend type*")
            .WithMessage("*novavm*"); // error message includes all valid options
    }

    [Fact]
    public void Create_Vice_ThrowsWhenNoViceRunning()
    {
        var (memMap, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6502);
        var config = new ViceBackendConfig { Host = "127.0.0.1", Port = 19998 };

        var act = () => BackendFactory.Create("vice", ProcessorType.MOS6502, memMap, viceConfig: config);
        act.Should().Throw<Exception>();
    }
}
