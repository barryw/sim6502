using FluentAssertions;
using sim6502.Proc;
using Xunit;
using Xunit.Abstractions;

namespace sim6502tests;

/// <summary>
/// Tests the 6502 emulator against Klaus Dormann's functional test suite.
/// See: https://github.com/Klaus2m5/6502_65C02_functional_tests
///
/// The test binary is a 64KB memory image that tests all valid NMOS 6502
/// opcodes and addressing modes. When all tests pass, execution traps at
/// address $3469. Any other trap address indicates a test failure.
/// </summary>
public class KlausDormannFunctionalTests
{
    private readonly ITestOutputHelper _output;

    // Success trap address from 6502_functional_test.lst
    private const int SuccessAddress = 0x3469;

    // Start address where code begins
    private const int StartAddress = 0x0400;

    // Maximum cycles before we assume the test is stuck
    private const long MaxCycles = 100_000_000; // 100 million cycles should be plenty

    public KlausDormannFunctionalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void AllOpcodes_ShouldPassFunctionalTest()
    {
        // Arrange
        var processor = new Processor();
        var testBinary = File.ReadAllBytes("FunctionalTests/6502_functional_test.bin");

        _output.WriteLine($"Loaded test binary: {testBinary.Length} bytes");

        // The binary is a full 64KB memory image - load at address 0
        processor.LoadProgram(0, testBinary, StartAddress);

        _output.WriteLine($"Starting execution at ${StartAddress:X4}");
        _output.WriteLine($"Success address: ${SuccessAddress:X4}");

        // Act - run until PC is trapped (same address for consecutive instructions)
        var previousPC = -1;
        var trapCount = 0;
        var lastReportedPC = -1;

        while (processor.CycleCount < MaxCycles)
        {
            var currentPC = processor.ProgramCounter;

            // Check if we're trapped (PC hasn't changed after executing)
            if (currentPC == previousPC)
            {
                trapCount++;
                if (trapCount >= 3) // Trapped for 3 consecutive cycles = stuck
                {
                    _output.WriteLine($"Trapped at ${currentPC:X4} after {processor.CycleCount:N0} cycles");
                    break;
                }
            }
            else
            {
                trapCount = 0;
            }

            // Progress reporting every 10 million cycles
            if (processor.CycleCount % 10_000_000 == 0 && currentPC != lastReportedPC)
            {
                _output.WriteLine($"Progress: {processor.CycleCount:N0} cycles, PC=${currentPC:X4}");
                lastReportedPC = currentPC;
            }

            previousPC = currentPC;

            try
            {
                processor.NextStep();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Exception at PC=${currentPC:X4}: {ex.Message}");
                _output.WriteLine($"OpCode: ${processor.CurrentOpCode:X2}");
                throw;
            }
        }

        // Assert
        var finalPC = processor.ProgramCounter;

        if (finalPC != SuccessAddress)
        {
            // Try to provide diagnostic info about where we failed
            _output.WriteLine($"FAILED at PC=${finalPC:X4}");
            _output.WriteLine($"Expected success at ${SuccessAddress:X4}");
            _output.WriteLine($"Total cycles: {processor.CycleCount:N0}");
            _output.WriteLine($"Current opcode: ${processor.CurrentOpCode:X2}");

            // Dump some memory around the failure point for debugging
            _output.WriteLine($"Memory around failure point:");
            var dumpStart = Math.Max(0, finalPC - 16);
            var dumpEnd = Math.Min(0xFFFF, finalPC + 16);
            for (var addr = dumpStart; addr <= dumpEnd; addr += 8)
            {
                var line = $"${addr:X4}:";
                for (var i = 0; i < 8 && addr + i <= dumpEnd; i++)
                {
                    line += $" {processor.ReadMemoryValueWithoutCycle(addr + i):X2}";
                }
                _output.WriteLine(line);
            }
        }

        finalPC.Should().Be(SuccessAddress,
            $"Test should complete at success address ${SuccessAddress:X4}, but trapped at ${finalPC:X4}");
    }

    [Fact]
    public void TestBinary_ShouldExist()
    {
        // Sanity check that the test binary is available
        File.Exists("FunctionalTests/6502_functional_test.bin").Should().BeTrue(
            "Klaus Dormann's 6502 functional test binary should be present");

        var bytes = File.ReadAllBytes("FunctionalTests/6502_functional_test.bin");
        bytes.Length.Should().Be(65536, "Test binary should be exactly 64KB (full memory image)");
    }
}
