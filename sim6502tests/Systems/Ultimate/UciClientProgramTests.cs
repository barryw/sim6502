using System.Text;
using FluentAssertions;
using sim6502.Backend;
using sim6502.Systems;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

/// <summary>
/// Drives the whole stack the way a real program does: 6502 code executing on the
/// simulated 6510, touching $DF1C-$DF1F through the C64 memory map, reaching the
/// UCI register block and the Ultimate DOS target.
/// </summary>
public class UciClientProgramTests : IDisposable
{
    private const int ProgramAddress = 0xC200;
    private const int ResultBuffer = 0xC000;
    private const int LengthByte = 0xC0FF;

    /// <summary>Offset and length of the busy-wait loop within the program.</summary>
    private const int WaitLoopOffset = 20;
    private const int WaitLoopLength = 9;

    /// <summary>
    /// IDENTIFY against DOS target $01. Copies the response to $C000 and stores the
    /// byte count at $C0FF. Hand-assembled at $C200:
    ///
    ///   C200  A9 08        lda #$08         ; CMD_ERROR - clear any stale error
    ///   C202  8D 1C DF     sta $DF1C
    ///   C205  A9 01        lda #$01         ; target $01 = Ultimate DOS
    ///   C207  8D 1D DF     sta $DF1D
    ///   C20A  A9 01        lda #$01         ; DOS_CMD_IDENTIFY
    ///   C20C  8D 1D DF     sta $DF1D
    ///   C20F  A9 01        lda #$01         ; CMD_PUSH_CMD
    ///   C211  8D 1C DF     sta $DF1C
    ///   C214  AD 1C DF     lda $DF1C        ; wait: poll while state == Busy
    ///   C217  29 30        and #$30
    ///   C219  C9 10        cmp #$10
    ///   C21B  F0 F7        beq $C214
    ///   C21D  A2 00        ldx #$00
    ///   C21F  AD 1C DF     lda $DF1C        ; rdloop: bit 7 = response available
    ///   C222  10 09        bpl $C22D
    ///   C224  AD 1E DF     lda $DF1E
    ///   C227  9D 00 C0     sta $C000,x
    ///   C22A  E8           inx
    ///   C22B  D0 F2        bne $C21F
    ///   C22D  8E FF C0     stx $C0FF        ; done
    ///   C230  A9 02        lda #$02         ; CMD_NEXT_DATA
    ///   C232  8D 1C DF     sta $DF1C
    ///   C235  AD 1C DF     lda $DF1C        ; ack: wait for bit 1 to clear
    ///   C238  29 02        and #$02
    ///   C23A  D0 F9        bne $C235
    ///   C23C  60           rts
    /// </summary>
    private static readonly byte[] CorrectClient =
    {
        0xA9, 0x08, 0x8D, 0x1C, 0xDF,
        0xA9, 0x01, 0x8D, 0x1D, 0xDF,
        0xA9, 0x01, 0x8D, 0x1D, 0xDF,
        0xA9, 0x01, 0x8D, 0x1C, 0xDF,
        0xAD, 0x1C, 0xDF, 0x29, 0x30, 0xC9, 0x10, 0xF0, 0xF7,
        0xA2, 0x00,
        0xAD, 0x1C, 0xDF, 0x10, 0x09,
        0xAD, 0x1E, 0xDF, 0x9D, 0x00, 0xC0, 0xE8, 0xD0, 0xF2,
        0x8E, 0xFF, 0xC0,
        0xA9, 0x02, 0x8D, 0x1C, 0xDF,
        0xAD, 0x1C, 0xDF, 0x29, 0x02, 0xD0, 0xF9,
        0x60
    };

    private readonly string _fixture;

    public UciClientProgramTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64sim-client-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixture);
        File.WriteAllText(Path.Combine(_fixture, "readme.txt"), "readme");
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    private U64SimBackend NewBackend(int latency)
    {
        var config = new U64SimBackendConfig { FsRoot = _fixture, UciLatencyCycles = latency };
        return new U64SimBackend(config, new C64MemoryMap());
    }

    private static string ReadResult(U64SimBackend backend, int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
            bytes[i] = backend.ReadByte(ResultBuffer + i);
        return Encoding.ASCII.GetString(bytes);
    }

    /// <summary>The same program with its busy-wait loop replaced by NOPs.</summary>
    private static byte[] BrokenClient()
    {
        var broken = (byte[])CorrectClient.Clone();
        for (var i = WaitLoopOffset; i < WaitLoopOffset + WaitLoopLength; i++)
            broken[i] = 0xEA;   // NOP
        return broken;
    }

    [Fact]
    public void ProgramIsExactlyTheDocumentedLength()
    {
        CorrectClient.Should().HaveCount(61, "the hand-assembled listing is 61 bytes");
    }

    [Fact]
    public void WaitLoopOffsetPointsAtTheDocumentedInstructions()
    {
        // C214: AD 1C DF  lda $DF1C
        CorrectClient.Skip(WaitLoopOffset).Take(3).Should().Equal(0xAD, 0x1C, 0xDF);
        // C21B: F0 F7  beq $C214 -- the last two bytes of the loop
        CorrectClient.Skip(WaitLoopOffset + WaitLoopLength - 2).Take(2).Should().Equal(0xF0, 0xF7);
    }

    [Fact]
    public void CorrectClient_ReadsTheIdentifyResponse()
    {
        using var backend = NewBackend(latency: 64);
        backend.LoadBinary(CorrectClient, ProgramAddress);

        var result = backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true);

        result.ExitedCleanly.Should().BeTrue();
        backend.ReadByte(LengthByte).Should().Be(20, "\"ULTIMATE-II DOS V1.2\" is 20 bytes");
        ReadResult(backend, 20).Should().Be("ULTIMATE-II DOS V1.2");
    }

    [Fact]
    public void CorrectClient_WorksWithZeroLatencyToo()
    {
        using var backend = NewBackend(latency: 0);
        backend.LoadBinary(CorrectClient, ProgramAddress);

        backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true)
               .ExitedCleanly.Should().BeTrue();
        ReadResult(backend, 20).Should().Be("ULTIMATE-II DOS V1.2");
    }

    [Fact]
    public void CorrectClient_LeavesTheUciReadyForAnotherCommand()
    {
        using var backend = NewBackend(latency: 64);
        backend.LoadBinary(CorrectClient, ProgramAddress);
        backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true);

        // Rerunning must work. If the acknowledge sequence were wrong the UCI would
        // be stuck out of the idle state and the second run would read nothing.
        for (var i = 0; i < 20; i++) backend.WriteByte(ResultBuffer + i, 0x00);
        backend.WriteByte(LengthByte, 0x00);

        backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true);

        backend.ReadByte(LengthByte).Should().Be(20);
        ReadResult(backend, 20).Should().Be("ULTIMATE-II DOS V1.2");
    }

    [Fact]
    public void BrokenClient_WithNoBusyWait_ReadsNothingAtTheDefaultLatency()
    {
        using var backend = NewBackend(latency: 64);
        backend.LoadBinary(BrokenClient(), ProgramAddress);

        backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true);

        backend.ReadByte(LengthByte).Should().Be(0,
            "without a busy-wait the response is not ready yet -- exactly the bug a " +
            "zero-latency simulator would hide");
    }

    [Fact]
    public void BrokenClient_PassesAtZeroLatency_WhichIsWhyTheDefaultIsNonZero()
    {
        using var backend = NewBackend(latency: 0);
        backend.LoadBinary(BrokenClient(), ProgramAddress);

        backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true);

        backend.ReadByte(LengthByte).Should().Be(20,
            "a zero-latency UCI answers instantly and the missing busy-wait goes " +
            "unnoticed -- documented here so the non-zero default is not simplified away");
    }

    [Fact]
    public void HostAndCpuPathsAgree()
    {
        using var backend = NewBackend(latency: 64);

        var (status, data) = backend.IssueUciCommand(new byte[] { 0x01, 0x01 });
        status.Should().Be("00,OK");

        backend.LoadBinary(CorrectClient, ProgramAddress);
        backend.ExecuteJsr(ProgramAddress, 0, stopOnRts: true, failOnBrk: true);

        ReadResult(backend, data.Length).Should().Be(Encoding.ASCII.GetString(data),
            "the host-side and register-level paths must return the same bytes");
    }
}
