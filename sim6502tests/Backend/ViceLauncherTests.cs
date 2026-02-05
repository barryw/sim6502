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
}
