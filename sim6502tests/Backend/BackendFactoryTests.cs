using sim6502;
using System.Net.Sockets;
using FluentAssertions;
using sim6502.Backend;
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

    [Fact]
    public void Create_U64Sim_ReturnsU64SimBackend()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "u64sim-factory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var (memMap, procType) = MemoryMapFactory.CreateForSystem(SystemType.C64);
            var config = new U64SimBackendConfig { FsRoot = fixture, UciLatencyCycles = 0 };

            var backend = BackendFactory.Create("u64sim", procType, memMap, u64SimConfig: config);

            backend.Should().BeOfType<U64SimBackend>();
            backend.Dispose();
        }
        finally
        {
            Directory.Delete(fixture, recursive: true);
        }
    }

    [Fact]
    public void Create_U64Sim_WithoutC64MemoryMap_ThrowsNamingSystemC64()
    {
        var (memMap, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6510);

        var act = () => BackendFactory.Create("u64sim", ProcessorType.MOS6510, memMap);

        act.Should().Throw<ArgumentException>().WithMessage("*system(c64)*");
    }

    [Fact]
    public void Create_UnknownBackend_ListsU64SimAsAnOption()
    {
        var (memMap, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6502);

        var act = () => BackendFactory.Create("nonsense", ProcessorType.MOS6502, memMap);

        act.Should().Throw<ArgumentException>().WithMessage("*u64sim*");
    }

    [Fact]
    public void Create_U64_ReturnsU64Backend()
    {
        using var backend = BackendFactory.Create(
            "u64", ProcessorType.MOS6510, new C64MemoryMap(),
            u64Config: new U64BackendConfig { Host = "192.0.2.1" });

        backend.Should().BeOfType<U64Backend>();
    }

    [Fact]
    public void Create_U64_WithoutHost_ThrowsWithTheFix()
    {
        var act = () => BackendFactory.Create(
            "u64", ProcessorType.MOS6510, new C64MemoryMap(),
            u64Config: new U64BackendConfig { Host = "" });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*--u64-host*");
    }

    [Fact]
    public void Create_UnknownBackend_ListsU64AmongValidOptions()
    {
        var act = () => BackendFactory.Create(
            "nonsense", ProcessorType.MOS6510, new C64MemoryMap());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*u64*");
    }

    // The three cases below construct a real backend and immediately call
    // Connect() against a server that is never running in this test suite.
    // They can't cover the success path without a live VICE/NovaVM/Verilator
    // process, but they can cover the branch and the specific exception type
    // each one surfaces, rather than asserting on a bare Exception.

    [Fact]
    public void Create_Vice_ThrowsInvalidOperationException_WhenNoViceRunning()
    {
        // ViceBackend.Connect() calls IViceConnection.Ping(), which swallows the
        // underlying HTTP failure and returns false; Connect() then throws
        // InvalidOperationException itself rather than letting a raw transport
        // exception escape.
        var (memMap, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6502);
        var config = new ViceBackendConfig { Host = "127.0.0.1", Port = 19998 };

        var act = () => BackendFactory.Create("vice", ProcessorType.MOS6502, memMap, viceConfig: config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Could not connect to VICE MCP server*");
    }

    [Fact]
    public void Create_NovaVm_ThrowsSocketException_WhenNoEmulatorRunning()
    {
        // NovaVmBackend.Connect() calls INovaVmConnection.Connect(), a raw
        // TcpClient.Connect with nothing wrapping it, so a refused connection
        // surfaces as an unmodified SocketException.
        var (memMap, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6502);
        var config = new NovaVmBackendConfig { Host = "127.0.0.1", Port = 19999 };

        var act = () => BackendFactory.Create("novavm", ProcessorType.MOS6502, memMap, novaVmConfig: config);

        act.Should().Throw<SocketException>();
    }

    [Fact]
    public void Create_Verilator_AppliesPortOverride_ThenThrowsConnectionFailure()
    {
        // Same protocol/backend as novavm, but the "verilator" case rewrites a
        // default-novavm-port (6502) config to 6503 before connecting. Passing
        // Port = 6502 here exercises that override branch; since the mutation
        // happens on the same config object before Connect() throws, we can
        // observe it afterward even though the call never returns a backend.
        //
        // The failure is either a SocketException (nothing listening on 6503,
        // the case on a bare CI box) or an InvalidOperationException (something
        // else answers the TCP connection on 6503 but fails the emulator's
        // ping handshake — observed locally, where a container runtime happens
        // to squat on that port). Both mean "no real verilator server there".
        var (memMap, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6502);
        var config = new NovaVmBackendConfig { Host = "127.0.0.1", Port = 6502 };

        var act = () => BackendFactory.Create("verilator", ProcessorType.MOS6502, memMap, novaVmConfig: config);

        act.Should().Throw<Exception>()
            .Where(ex => ex is SocketException || ex is InvalidOperationException);
        config.Port.Should().Be(6503);
    }
}
