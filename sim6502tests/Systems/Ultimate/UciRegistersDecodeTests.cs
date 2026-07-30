using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class UciRegistersDecodeTests
{
    private static UciRegisters NewUci() => new(latencyCycles: 0);

    [Fact]
    public void ReadCommandRegister_ReturnsIdentifier()
    {
        NewUci().Read(UciConstants.CommandAddress).Should().Be(0xC9);
    }

    [Fact]
    public void ReadBusId_ReturnsConfiguredValue()
    {
        var uci = NewUci();
        uci.BusId = 0x0B;
        uci.Read(UciConstants.BusIdAddress).Should().Be(0x0B);
    }

    [Fact]
    public void InitialStatus_IsIdleWithNothingAvailable()
    {
        NewUci().Read(UciConstants.ControlAddress).Should().Be(0x00);
    }

    [Fact]
    public void WritingCommandBytes_AdvancesCommandLength()
    {
        var uci = NewUci();
        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.CommandAddress, 0x11);
        uci.Write(UciConstants.CommandAddress, 0x41);
        uci.CommandLength.Should().Be(3);
    }

    [Fact]
    public void CommandPointer_SaturatesAtBufferEnd()
    {
        var uci = NewUci();
        for (var i = 0; i < UciConstants.CommandBufferSize + 32; i++)
            uci.Write(UciConstants.CommandAddress, 0xAA);

        uci.CommandLength.Should().Be(UciConstants.CommandBufferEnd);
    }

    [Fact]
    public void PushCommandWhenIdle_EntersBusyAndSetsNewCommandFlag()
    {
        var uci = NewUci();
        uci.RegisterTarget(1, new NeverAnsweringTarget());
        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);

        var status = uci.StatusByte;
        (status & UciConstants.StatusStateMask).Should().Be(UciConstants.StateBusy);
        (status & UciConstants.StatusNewCommandSet).Should().Be(UciConstants.StatusNewCommandSet);
    }

    [Fact]
    public void PushCommandWhenNotIdle_SetsErrorFlag()
    {
        var uci = NewUci();
        uci.RegisterTarget(1, new NeverAnsweringTarget());
        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);

        (uci.StatusByte & UciConstants.StatusError).Should().Be(UciConstants.StatusError);
    }

    [Fact]
    public void ClearError_ClearsTheErrorFlag()
    {
        var uci = NewUci();
        uci.RegisterTarget(1, new NeverAnsweringTarget());
        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);
        (uci.StatusByte & UciConstants.StatusError).Should().NotBe(0);

        uci.Write(UciConstants.ControlAddress, UciConstants.ControlClearError);
        (uci.StatusByte & UciConstants.StatusError).Should().Be(0);
    }

    [Fact]
    public void AbortWrite_SetsAbortFlag()
    {
        var uci = NewUci();
        uci.RegisterTarget(1, new NeverAnsweringTarget());
        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlAbort);

        (uci.StatusByte & UciConstants.StatusAbortSet).Should().NotBe(0);
    }

    [Fact]
    public void ReadingResponseWhileIdle_ReturnsZero()
    {
        NewUci().Read(UciConstants.ResponseAddress).Should().Be(0x00);
    }

    [Fact]
    public void ReadingStatusDataWhileIdle_ReturnsZero()
    {
        NewUci().Read(UciConstants.StatusAddress).Should().Be(0x00);
    }

    [Fact]
    public void UnknownAddressInRange_ReadsAsFF()
    {
        NewUci().Read(0xDF1A).Should().Be(0xFF);
    }

    /// <summary>
    /// A target whose ParseCommand is never reached in these tests because the
    /// service loop is suppressed. Used only so RegisterTarget has something to
    /// store; see UciRegistersDispatchTests for real dispatch coverage.
    /// </summary>
    private sealed class NeverAnsweringTarget : ICommandTarget
    {
        public UciReply ParseCommand(byte[] command) => UciReply.Empty(UciConstants.StatusOk);
        public UciReply GetMoreData() => UciReply.Empty(UciConstants.StatusOk);
        public void Abort(int bytesConsumed) { }
    }
}
