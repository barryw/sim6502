using FluentAssertions;
using sim6502.Backend;
using Xunit;

namespace sim6502tests.Backend;

public class ViceBackendTests
{
    [Fact]
    public void Constructor_StoresConfiguration()
    {
        var config = new ViceBackendConfig
        {
            Host = "192.168.1.100",
            Port = 7000,
            TimeoutMs = 10000,
            WarpMode = false
        };
        config.Host.Should().Be("192.168.1.100");
        config.Port.Should().Be(7000);
        config.TimeoutMs.Should().Be(10000);
        config.WarpMode.Should().BeFalse();
    }

    [Fact]
    public void DefaultConfig_HasSensibleDefaults()
    {
        var config = new ViceBackendConfig();
        config.Host.Should().Be("127.0.0.1");
        config.Port.Should().Be(6510);
        config.TimeoutMs.Should().Be(5000);
        config.WarpMode.Should().BeTrue();
    }
}
