using FluentAssertions;
using sim6502.Backend;
using Xunit;

namespace sim6502tests.Backend;

public class NovaVmBackendConfigTests
{
    [Fact]
    public void DefaultConfig_HasSensibleDefaults()
    {
        var config = new NovaVmBackendConfig();
        config.Host.Should().Be("127.0.0.1");
        config.Port.Should().Be(6502);
        config.TimeoutMs.Should().Be(10000);
    }

    [Fact]
    public void Config_AcceptsCustomValues()
    {
        var config = new NovaVmBackendConfig
        {
            Host = "192.168.1.50",
            Port = 7000,
            TimeoutMs = 30000
        };
        config.Host.Should().Be("192.168.1.50");
        config.Port.Should().Be(7000);
        config.TimeoutMs.Should().Be(30000);
    }
}
