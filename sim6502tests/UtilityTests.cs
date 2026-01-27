using FluentAssertions;
using sim6502.Utilities;
using Xunit;

namespace sim6502tests;

public class UtilityTests
{
    [Fact]
    public void TestParseHexWithDollarSign()
    {
        var hex1 = "$ffff";
        hex1.ParseNumber().Should().Be(0xffff);

        hex1 = "$fff";
        hex1.ParseNumber().Should().Be(0xfff);

        hex1 = "$ff";
        hex1.ParseNumber().Should().Be(0xff);

        hex1 = "$f";
        hex1.ParseNumber().Should().Be(0xf);

        hex1 = "$abcd";
        hex1.ParseNumber().Should().Be(0xabcd);
    }

    [Fact]
    public void TestParseWithZeroX()
    {
        var hex1 = "0xffff";
        hex1.ParseNumber().Should().Be(0xffff);

        hex1 = "0xfff";
        hex1.ParseNumber().Should().Be(0xfff);

        hex1 = "0xff";
        hex1.ParseNumber().Should().Be(0xff);

        hex1 = "0xf";
        hex1.ParseNumber().Should().Be(0xf);

        hex1 = "0xabcd";
        hex1.ParseNumber().Should().Be(0xabcd);
    }

    [Fact]
    public void TestPlainIntegers()
    {
        var int1 = "65535";
        int1.ParseNumber().Should().Be(0xffff);

        int1 = "4095";
        int1.ParseNumber().Should().Be(0xfff);

        int1 = "255";
        int1.ParseNumber().Should().Be(0xff);

        int1 = "15";
        int1.ParseNumber().Should().Be(0xf);

        int1 = "43981";
        int1.ParseNumber().Should().Be(0xabcd);
    }

    [Fact]
    public void TestGetLoadAddress()
    {
        var bytes = new byte[2];
        bytes[0] = 0;
        bytes[1] = 192;

        var address = Utility.GetProgramLoadAddress(bytes);
        address.Should().Be(0xc000);
    }

    [Fact]
    public void TestConvertIntToHex()
    {
        var i = 65535;
        i.ToHex().Should().Be("$ffff");

        i = 4095;
        i.ToHex().Should().Be("$fff");

        i = 255;
        i.ToHex().Should().Be("$ff");

        i = 15;
        i.ToHex().Should().Be("$f");
    }
}
