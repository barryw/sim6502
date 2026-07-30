using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class UciConstantsTests
{
    [Fact]
    public void RegisterAddresses_MatchUpstream()
    {
        UciConstants.BusIdAddress.Should().Be(0xDF1B);
        UciConstants.ControlAddress.Should().Be(0xDF1C);
        UciConstants.CommandAddress.Should().Be(0xDF1D);
        UciConstants.ResponseAddress.Should().Be(0xDF1E);
        UciConstants.StatusAddress.Should().Be(0xDF1F);
        UciConstants.Identifier.Should().Be(0xC9);
    }

    [Fact]
    public void BufferGeometry_MatchesCommandIfPkg()
    {
        UciConstants.CommandBufferStart.Should().Be(0);
        UciConstants.CommandBufferSize.Should().Be(896);
        UciConstants.CommandBufferEnd.Should().Be(895);

        UciConstants.ResponseBufferStart.Should().Be(896);
        UciConstants.ResponseBufferSize.Should().Be(896);
        UciConstants.ResponseBufferEnd.Should().Be(1791);

        UciConstants.StatusBufferStart.Should().Be(1792);
        UciConstants.StatusBufferSize.Should().Be(256);
        UciConstants.StatusBufferEnd.Should().Be(2047);

        UciConstants.BackingStoreSize.Should().Be(2048);
    }

    [Fact]
    public void StatusStrings_AreByteExact()
    {
        UciConstants.StatusOk.Should().Be("00,OK");
        UciConstants.StatusUnknownCommand.Should().Be("21,UNKNOWN COMMAND");
        UciConstants.MessageNoTarget.Should().Be("NO TARGET");
    }

    [Fact]
    public void UciReply_Empty_HasNoDataAndIsLastPart()
    {
        var reply = UciReply.Empty(UciConstants.StatusOk);
        reply.Data.Should().BeEmpty();
        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
    }

    [Fact]
    public void UciReply_Ok_CarriesDataWithOkStatus()
    {
        var reply = UciReply.Ok(new byte[] { 1, 2, 3 });
        reply.Data.Should().Equal(1, 2, 3);
        reply.Status.Should().Be("00,OK");
        reply.LastPart.Should().BeTrue();
    }
}
