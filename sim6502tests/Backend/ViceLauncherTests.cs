using FluentAssertions;
using sim6502.Backend;
using Xunit;

namespace sim6502tests.Backend;

public class ViceLauncherTests
{
    [Fact]
    public void BuildArguments_IncludesMcpFlags()
    {
        var args = ViceLauncher.BuildArguments(6510);
        args.Should().Contain("-mcpserver");
        args.Should().Contain("-mcpserverport");
        args.Should().Contain("6510");
        args.Should().Contain("+confirmexit");
    }

    [Fact]
    public void BuildArguments_UsesSpecifiedPort()
    {
        var args = ViceLauncher.BuildArguments(7000);
        args.Should().Contain("7000");
    }

    // ── Construction / Dispose (never launched) ──
    // NOTE: We never call Launch() anywhere in this file — it spawns a real VICE
    // process, which is explicitly out of bounds for hermetic tests.

    [Fact]
    public void Constructor_DefaultPort_DoesNotThrow()
    {
        var act = () => new ViceLauncher();
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_CustomPort_DoesNotThrow()
    {
        var act = () => new ViceLauncher(7777);
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_NeverLaunched_DoesNotThrow()
    {
        var launcher = new ViceLauncher();
        var act = () => launcher.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var launcher = new ViceLauncher();
        launcher.Dispose();
        var act = () => launcher.Dispose();
        act.Should().NotThrow();
    }
}
