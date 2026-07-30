using System.Text;
using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class UciRegistersDispatchTests
{
    /// <summary>Answers IDENTIFY with a fixed string, in one part.</summary>
    private sealed class SinglePartTarget : ICommandTarget
    {
        public byte[]? LastCommand { get; private set; }
        public int AbortedAt { get; private set; } = -1;

        public UciReply ParseCommand(byte[] command)
        {
            LastCommand = command;
            return new UciReply(Encoding.ASCII.GetBytes("HELLO"), "00,OK", true);
        }

        public UciReply GetMoreData() => UciReply.Empty("00,OK");
        public void Abort(int bytesConsumed) => AbortedAt = bytesConsumed;
    }

    /// <summary>Answers with three one-byte parts, then stops.</summary>
    private sealed class MultiPartTarget : ICommandTarget
    {
        private int _index;
        public int MoreDataCalls { get; private set; }

        public UciReply ParseCommand(byte[] command)
        {
            _index = 0;
            return Next();
        }

        public UciReply GetMoreData()
        {
            MoreDataCalls++;
            return Next();
        }

        private UciReply Next()
        {
            var payload = new[] { (byte)(0xA0 + _index) };
            _index++;
            return _index >= 3
                ? new UciReply(payload, "00,OK", true)
                : new UciReply(payload, "", false);
        }

        public void Abort(int bytesConsumed) { }
    }

    private static UciRegisters NewUci(ICommandTarget target, int latency = 0, Func<long>? clock = null)
    {
        var uci = new UciRegisters(latency) { ServiceEnabled = true };
        if (clock != null) uci.CycleCounter = clock;
        uci.RegisterTarget(1, target);
        return uci;
    }

    private static void SendCommand(UciRegisters uci, params byte[] bytes)
    {
        foreach (var b in bytes)
            uci.Write(UciConstants.CommandAddress, b);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);
    }

    // Bounded, not unbounded: a stuck availability bit is expected upstream
    // behaviour for an exactly-full queue (see UciRegisters class doc). Without
    // a cap, a regression that gets a bit stuck on a *non*-full queue would
    // hang the test run instead of failing it.
    private static string ReadResponse(UciRegisters uci)
    {
        var sb = new StringBuilder();
        var count = 0;
        while ((uci.Read(UciConstants.ControlAddress) & UciConstants.StatusResponseAvailable) != 0)
        {
            sb.Append((char)uci.Read(UciConstants.ResponseAddress));
            if (++count > UciConstants.ResponseBufferSize + 16)
                throw new InvalidOperationException(
                    $"ReadResponse read more than {UciConstants.ResponseBufferSize + 16} bytes without " +
                    "the response-available bit clearing. Likely cause: the response queue filled " +
                    "exactly to ResponseBufferSize, which leaves the bit stuck by upstream design " +
                    "(see UciRegisters class doc) — use a bounded read for that case instead.");
        }
        return sb.ToString();
    }

    private static string ReadStatus(UciRegisters uci)
    {
        var sb = new StringBuilder();
        var count = 0;
        while ((uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStatusAvailable) != 0)
        {
            sb.Append((char)uci.Read(UciConstants.StatusAddress));
            if (++count > UciConstants.StatusBufferSize + 16)
                throw new InvalidOperationException(
                    $"ReadStatus read more than {UciConstants.StatusBufferSize + 16} bytes without " +
                    "the status-available bit clearing. Likely cause: the status queue filled " +
                    "exactly to StatusBufferSize, which leaves the bit stuck by upstream design " +
                    "(see UciRegisters class doc) — use a bounded read for that case instead.");
        }
        return sb.ToString();
    }

    [Fact]
    public void Command_IsDeliveredToTargetVerbatim()
    {
        var target = new SinglePartTarget();
        var uci = NewUci(target);
        SendCommand(uci, 0x01, 0x01, 0x2A);

        target.LastCommand.Should().Equal(0x01, 0x01, 0x2A);
    }

    [Fact]
    public void AfterDispatch_StateIsDataLast()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x01, 0x01);

        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateDataLast);
    }

    [Fact]
    public void ResponseBytes_AreReadableThenExhausted()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x01, 0x01);

        ReadResponse(uci).Should().Be("HELLO");
        uci.Read(UciConstants.ResponseAddress).Should().Be(0x00);
    }

    [Fact]
    public void StatusBytes_AreReadableThenExhausted()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x01, 0x01);

        ReadStatus(uci).Should().Be("00,OK");
        uci.Read(UciConstants.StatusAddress).Should().Be(0x00);
    }

    [Fact]
    public void DataAccept_FromDataLast_ReturnsToIdle()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x01, 0x01);
        ReadResponse(uci);

        uci.Write(UciConstants.ControlAddress, UciConstants.ControlDataAccept);

        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateIdle);
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusDataAcceptedSet)
            .Should().Be(0);
    }

    [Fact]
    public void AfterDataAccept_CommandPointerIsReset()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x01, 0x01, 0x02, 0x03);
        uci.CommandLength.Should().Be(0, "AcceptCommand resets the pointer after dispatch");
    }

    [Fact]
    public void MultiPartReply_StateIsDataMoreUntilFinalPart()
    {
        var target = new MultiPartTarget();
        var uci = NewUci(target);
        SendCommand(uci, 0x01, 0x14);

        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateDataMore);
        uci.Read(UciConstants.ResponseAddress).Should().Be(0xA0);

        uci.Write(UciConstants.ControlAddress, UciConstants.ControlDataAccept);
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateDataMore);
        uci.Read(UciConstants.ResponseAddress).Should().Be(0xA1);

        uci.Write(UciConstants.ControlAddress, UciConstants.ControlDataAccept);
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateDataLast);
        uci.Read(UciConstants.ResponseAddress).Should().Be(0xA2);

        target.MoreDataCalls.Should().Be(2);
    }

    [Fact]
    public void Abort_MidTransfer_ReportsBytesConsumedAndReturnsToIdle()
    {
        var target = new SinglePartTarget();
        var uci = NewUci(target);
        SendCommand(uci, 0x01, 0x01);

        uci.Read(UciConstants.ResponseAddress).Should().Be((byte)'H');
        uci.Read(UciConstants.ResponseAddress).Should().Be((byte)'E');

        uci.Write(UciConstants.ControlAddress, UciConstants.ControlAbort);

        target.AbortedAt.Should().Be(2);
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateIdle);
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusAbortSet).Should().Be(0);
    }

    [Fact]
    public void NoReplyFlag_LeavesStateIdleWithNoResponse()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x81, 0x01);   // bit 7 set on the target byte

        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateIdle);
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusResponseAvailable)
            .Should().Be(0);
    }

    [Fact]
    public void UnregisteredTarget_AnswersIdentifyWithNoTarget()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x07, 0x01);   // target 7 is not registered

        ReadResponse(uci).Should().Be("NO TARGET");
        ReadStatus(uci).Should().Be("00,OK");
    }

    [Fact]
    public void UnregisteredTarget_RejectsOtherCommands()
    {
        var uci = NewUci(new SinglePartTarget());
        SendCommand(uci, 0x07, 0x55);

        ReadResponse(uci).Should().BeEmpty();
        ReadStatus(uci).Should().Be("21,UNKNOWN COMMAND");
    }

    [Fact]
    public void BusyState_IsHeldForTheConfiguredLatency()
    {
        long cycles = 0;
        var uci = NewUci(new SinglePartTarget(), latency: 64, clock: () => cycles);

        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.CommandAddress, 0x01);
        uci.Write(UciConstants.ControlAddress, UciConstants.ControlPushCommand);

        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateBusy, "no cycles have elapsed yet");

        cycles = 63;
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateBusy, "one cycle short of the latency");

        cycles = 64;
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusStateMask)
            .Should().Be(UciConstants.StateDataLast, "latency has elapsed");
    }

    // Upstream (command_protocol.vhd lines 131-135, 176-180): the response
    // pointer saturates at ResponseBufferEnd — the last valid slot — via a
    // `/=` (i.e. !=) guard, and response_valid stays true as long as
    // (pointer - start) < length. When a reply exactly fills the buffer, the
    // pointer parks on the last slot and that comparison is permanently true,
    // so the availability bit never clears and the C64 would re-read the
    // final byte forever. This is genuine hardware behaviour, reproduced here
    // deliberately, not a bug — do not "fix" it by loosening the pointer
    // guard back to <=. A real client must track how many bytes it expects
    // rather than reading until the bit clears.
    [Fact]
    public void OversizedReply_TruncatesToBufferAndLeavesAvailabilityBitStuck()
    {
        var uci = NewUci(new OversizedTarget());
        SendCommand(uci, 0x01, 0x01);

        for (var i = 0; i < UciConstants.ResponseBufferSize; i++)
        {
            (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusResponseAvailable)
                .Should().NotBe(0, $"byte {i} of {UciConstants.ResponseBufferSize} should still be available");
            uci.Read(UciConstants.ResponseAddress).Should().Be((byte)(i & 0xFF),
                "CopyResult must truncate the oversized reply to the first ResponseBufferSize bytes");
        }

        // The buffer is now exactly full: the availability bit stays stuck set
        // instead of clearing, per upstream's saturation behaviour above.
        (uci.Read(UciConstants.ControlAddress) & UciConstants.StatusResponseAvailable)
            .Should().NotBe(0, "an exactly-full response buffer leaves the bit stuck, per upstream");
    }

    private sealed class OversizedTarget : ICommandTarget
    {
        public UciReply ParseCommand(byte[] command)
        {
            var data = new byte[UciConstants.ResponseBufferSize + 100];
            for (var i = 0; i < data.Length; i++)
                data[i] = (byte)(i & 0xFF);
            return UciReply.Ok(data);
        }

        public UciReply GetMoreData() => UciReply.Empty("00,OK");
        public void Abort(int bytesConsumed) { }
    }

    [Fact]
    public void IssueHostCommand_ConcatenatesAllParts()
    {
        var uci = NewUci(new MultiPartTarget());
        var (status, data) = uci.IssueHostCommand(new byte[] { 0x01, 0x14 });

        data.Should().Equal(0xA0, 0xA1, 0xA2);
        status.Should().Be("00,OK");
    }

    [Fact]
    public void IssueHostCommand_RejectsShortCommands()
    {
        var uci = NewUci(new SinglePartTarget());
        var act = () => uci.IssueHostCommand(new byte[] { 0x01 });
        act.Should().Throw<ArgumentException>();
    }
}
