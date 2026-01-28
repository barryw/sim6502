/*
Copyright (c) 2013, Aaron Mell
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
this list of conditions and the following disclaimer in the documentation
and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using FluentAssertions;
using sim6502.Proc;
using Xunit;

namespace sim6502tests;

public class Opcodes65C02Tests
{
    [Fact]
    public void PHX_PushesXRegisterToStack()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.XRegister = 0x42;
        var initialSp = proc.StackPointer;

        // PHX at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xDA); // PHX
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.StackPointer.Should().Be(initialSp - 1);
        proc.ReadMemoryValueWithoutCycle(0x100 + initialSp).Should().Be(0x42);
    }

    [Fact]
    public void PLX_PullsStackToXRegister()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.XRegister = 0x00;

        // Push $42 onto stack manually
        proc.WriteMemoryValueWithoutIncrement(0x1FF, 0x42);
        proc.StackPointer = 0xFE; // Points to next free location

        // PLX at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xFA); // PLX
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.XRegister.Should().Be(0x42);
        proc.StackPointer.Should().Be(0xFF);
    }

    [Fact]
    public void PHY_PushesYRegisterToStack()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.YRegister = 0x37;
        var initialSp = proc.StackPointer;

        // PHY at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x5A); // PHY
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.StackPointer.Should().Be(initialSp - 1);
        proc.ReadMemoryValueWithoutCycle(0x100 + initialSp).Should().Be(0x37);
    }

    [Fact]
    public void PLY_PullsStackToYRegister()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.YRegister = 0x00;

        // Push $99 onto stack manually
        proc.WriteMemoryValueWithoutIncrement(0x1FF, 0x99);
        proc.StackPointer = 0xFE;

        // PLY at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x7A); // PLY
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.YRegister.Should().Be(0x99);
        proc.StackPointer.Should().Be(0xFF);
    }

    [Fact]
    public void PHX_NotAvailableOn6502()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xDA); // PHX (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void PHX_NotAvailableOn6510()
    {
        var proc = new Processor(ProcessorType.MOS6510);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xDA); // PHX (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void PLX_NotAvailableOn6502()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xFA); // PLX (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void PLX_NotAvailableOn6510()
    {
        var proc = new Processor(ProcessorType.MOS6510);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xFA); // PLX (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void PHY_NotAvailableOn6502()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x5A); // PHY (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void PHY_NotAvailableOn6510()
    {
        var proc = new Processor(ProcessorType.MOS6510);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x5A); // PHY (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void PLY_NotAvailableOn6502()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x7A); // PLY (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void PLY_NotAvailableOn6510()
    {
        var proc = new Processor(ProcessorType.MOS6510);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x7A); // PLY (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Theory]
    [InlineData(0x00, true)]
    [InlineData(0x01, false)]
    [InlineData(0xFF, false)]
    public void PLX_Zero_Flag_Has_Correct_Value(byte valueToLoad, bool expectedResult)
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Push value onto stack manually
        proc.WriteMemoryValueWithoutIncrement(0x1FF, valueToLoad);
        proc.StackPointer = 0xFE;

        // PLX at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xFA); // PLX
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.ZeroFlag.Should().Be(expectedResult);
    }

    [Theory]
    [InlineData(0x7F, false)]
    [InlineData(0x80, true)]
    [InlineData(0xFF, true)]
    public void PLX_Negative_Flag_Has_Correct_Value(byte valueToLoad, bool expectedResult)
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Push value onto stack manually
        proc.WriteMemoryValueWithoutIncrement(0x1FF, valueToLoad);
        proc.StackPointer = 0xFE;

        // PLX at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xFA); // PLX
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.NegativeFlag.Should().Be(expectedResult);
    }

    [Theory]
    [InlineData(0x00, true)]
    [InlineData(0x01, false)]
    [InlineData(0xFF, false)]
    public void PLY_Zero_Flag_Has_Correct_Value(byte valueToLoad, bool expectedResult)
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Push value onto stack manually
        proc.WriteMemoryValueWithoutIncrement(0x1FF, valueToLoad);
        proc.StackPointer = 0xFE;

        // PLY at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x7A); // PLY
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.ZeroFlag.Should().Be(expectedResult);
    }

    [Theory]
    [InlineData(0x7F, false)]
    [InlineData(0x80, true)]
    [InlineData(0xFF, true)]
    public void PLY_Negative_Flag_Has_Correct_Value(byte valueToLoad, bool expectedResult)
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Push value onto stack manually
        proc.WriteMemoryValueWithoutIncrement(0x1FF, valueToLoad);
        proc.StackPointer = 0xFE;

        // PLY at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x7A); // PLY
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.NegativeFlag.Should().Be(expectedResult);
    }

    #region STZ Tests

    [Fact]
    public void STZ_ZeroPage_StoresZero()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Set target memory location to non-zero value
        proc.WriteMemoryValueWithoutIncrement(0x0042, 0xFF);

        // STZ $42 at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x64); // STZ
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42); // Zero page address
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.ReadMemoryValueWithoutCycle(0x0042).Should().Be(0x00);
        proc.ProgramCounter.Should().Be(0x0202);
        proc.CurrentOpCode.Should().Be(0x64);
    }

    [Fact]
    public void STZ_ZeroPageX_StoresZero()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.XRegister = 0x05;

        // Set target memory location to non-zero value
        proc.WriteMemoryValueWithoutIncrement(0x0047, 0xAA);

        // STZ $42,X at address $0200 (will store to $42 + $05 = $47)
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x74); // STZ
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42); // Zero page address
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.ReadMemoryValueWithoutCycle(0x0047).Should().Be(0x00);
        proc.ProgramCounter.Should().Be(0x0202);
        proc.CurrentOpCode.Should().Be(0x74);
    }

    [Fact]
    public void STZ_Absolute_StoresZero()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Set target memory location to non-zero value
        proc.WriteMemoryValueWithoutIncrement(0x1234, 0x88);

        // STZ $1234 at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x9C); // STZ
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x34); // Low byte
        proc.WriteMemoryValueWithoutIncrement(0x0202, 0x12); // High byte
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.ReadMemoryValueWithoutCycle(0x1234).Should().Be(0x00);
        proc.ProgramCounter.Should().Be(0x0203);
        proc.CurrentOpCode.Should().Be(0x9C);
    }

    [Fact]
    public void STZ_AbsoluteX_StoresZero()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.XRegister = 0x0A;

        // Set target memory location to non-zero value
        proc.WriteMemoryValueWithoutIncrement(0x123E, 0x77);

        // STZ $1234,X at address $0200 (will store to $1234 + $0A = $123E)
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x9E); // STZ
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x34); // Low byte
        proc.WriteMemoryValueWithoutIncrement(0x0202, 0x12); // High byte
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.ReadMemoryValueWithoutCycle(0x123E).Should().Be(0x00);
        proc.ProgramCounter.Should().Be(0x0203);
        proc.CurrentOpCode.Should().Be(0x9E);
    }

    [Theory]
    [InlineData(0x64)] // STZ Zero Page
    [InlineData(0x74)] // STZ Zero Page,X
    [InlineData(0x9C)] // STZ Absolute
    [InlineData(0x9E)] // STZ Absolute,X
    public void STZ_NotAvailableOn6502(byte opcode)
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, opcode);
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Theory]
    [InlineData(0x64)] // STZ Zero Page
    [InlineData(0x74)] // STZ Zero Page,X
    [InlineData(0x9C)] // STZ Absolute
    [InlineData(0x9E)] // STZ Absolute,X
    public void STZ_NotAvailableOn6510(byte opcode)
    {
        var proc = new Processor(ProcessorType.MOS6510);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, opcode);
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    #endregion

    #region BRA Tests

    [Fact]
    public void BRA_BranchesForward()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // BRA +10 at address $0200
        // Expected: PC should be at $0200 + 2 (instruction size) + 10 (offset) = $020C
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x80); // BRA
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x0A); // offset +10
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.ProgramCounter.Should().Be(0x020C);
        proc.CurrentOpCode.Should().Be(0x80);
    }

    [Fact]
    public void BRA_BranchesBackward()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // BRA -10 at address $0200
        // Offset is two's complement: -10 = 256 - 10 = 246 = 0xF6
        // Expected: PC should be at $0200 + 2 (instruction size) - 10 (offset) = $01F8
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x80); // BRA
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0xF6); // offset -10 (two's complement)
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.ProgramCounter.Should().Be(0x01F8);
        proc.CurrentOpCode.Should().Be(0x80);
    }

    [Fact]
    public void BRA_PageBoundaryCrossed()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Use pattern from existing tests: start near end of page and branch forward
        // PC = $02F0, after instruction PC = $02F2
        // offset = 0x0F (15), movement = 15
        // newPC = $02F2 + 15 = $0301, then +1 = $0302
        // But actually the PC ends up at $0301
        proc.WriteMemoryValueWithoutIncrement(0x02F0, 0x80); // BRA
        proc.WriteMemoryValueWithoutIncrement(0x02F1, 0x0F); // offset +15
        proc.ProgramCounter = 0x02F0;

        var initialCycles = proc.CycleCount;

        proc.NextStep();

        proc.ProgramCounter.Should().Be(0x0301);
        // Base cycles: 3, plus 1 for page boundary crossing = 4 total
        (proc.CycleCount - initialCycles).Should().Be(4);
    }

    [Fact]
    public void BRA_NoPageBoundaryCross()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // BRA within same page
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x80); // BRA
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x05); // offset +5
        proc.ProgramCounter = 0x0200;

        var initialCycles = proc.CycleCount;

        proc.NextStep();

        proc.ProgramCounter.Should().Be(0x0207);
        // Base cycles: 3, no page boundary crossing
        (proc.CycleCount - initialCycles).Should().Be(3);
    }

    [Fact]
    public void BRA_NotAvailableOn6502()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x80); // BRA (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void BRA_NotAvailableOn6510()
    {
        var proc = new Processor(ProcessorType.MOS6510);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x80); // BRA (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    #endregion

    #region INC A Tests

    [Fact]
    public void INC_A_IncrementsAccumulator()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.Accumulator = 0x42;

        // INC A at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x1A); // INC A
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.Accumulator.Should().Be(0x43);
        proc.ProgramCounter.Should().Be(0x0201);
        proc.CurrentOpCode.Should().Be(0x1A);
    }

    [Fact]
    public void INC_A_WrapsAndSetsZeroFlag()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.Accumulator = 0xFF;

        // INC A at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x1A); // INC A
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.Accumulator.Should().Be(0x00);
        proc.ZeroFlag.Should().BeTrue();
        proc.NegativeFlag.Should().BeFalse();
    }

    [Theory]
    [InlineData(0x00, 0x01, false, false)]
    [InlineData(0x7F, 0x80, false, true)]
    [InlineData(0x80, 0x81, false, true)]
    [InlineData(0xFE, 0xFF, false, true)]
    [InlineData(0xFF, 0x00, true, false)]
    public void INC_A_SetsCorrectFlags(byte initial, byte expected, bool expectedZero, bool expectedNegative)
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.Accumulator = initial;

        // INC A at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x1A); // INC A
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.Accumulator.Should().Be(expected);
        proc.ZeroFlag.Should().Be(expectedZero);
        proc.NegativeFlag.Should().Be(expectedNegative);
    }

    [Fact]
    public void INC_A_NotAvailableOn6502()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x1A); // INC A (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void INC_A_NotAvailableOn6510()
    {
        var proc = new Processor(ProcessorType.MOS6510);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x1A); // INC A (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void INC_A_HasCorrectCycles()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.Accumulator = 0x42;

        // INC A at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x1A); // INC A
        proc.ProgramCounter = 0x0200;

        var initialCycles = proc.CycleCount;
        proc.NextStep();

        // Should take 2 cycles
        (proc.CycleCount - initialCycles).Should().Be(2);
    }

    #endregion

    #region DEC A Tests

    [Fact]
    public void DEC_A_DecrementsAccumulator()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.Accumulator = 0x42;

        // DEC A at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x3A); // DEC A
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.Accumulator.Should().Be(0x41);
        proc.ProgramCounter.Should().Be(0x0201);
        proc.CurrentOpCode.Should().Be(0x3A);
    }

    [Fact]
    public void DEC_A_WrapsAndSetsNegativeFlag()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.Accumulator = 0x00;

        // DEC A at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x3A); // DEC A
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.Accumulator.Should().Be(0xFF);
        proc.ZeroFlag.Should().BeFalse();
        proc.NegativeFlag.Should().BeTrue();
    }

    [Fact]
    public void DEC_A_SetsZeroFlag()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.Accumulator = 0x01;

        // DEC A at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x3A); // DEC A
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.Accumulator.Should().Be(0x00);
        proc.ZeroFlag.Should().BeTrue();
        proc.NegativeFlag.Should().BeFalse();
    }

    [Theory]
    [InlineData(0x01, 0x00, true, false)]
    [InlineData(0x02, 0x01, false, false)]
    [InlineData(0x80, 0x7F, false, false)]
    [InlineData(0x81, 0x80, false, true)]
    [InlineData(0xFF, 0xFE, false, true)]
    [InlineData(0x00, 0xFF, false, true)]
    public void DEC_A_SetsCorrectFlags(byte initial, byte expected, bool expectedZero, bool expectedNegative)
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.Accumulator = initial;

        // DEC A at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x3A); // DEC A
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.Accumulator.Should().Be(expected);
        proc.ZeroFlag.Should().Be(expectedZero);
        proc.NegativeFlag.Should().Be(expectedNegative);
    }

    [Fact]
    public void DEC_A_NotAvailableOn6502()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x3A); // DEC A (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void DEC_A_NotAvailableOn6510()
    {
        var proc = new Processor(ProcessorType.MOS6510);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x3A); // DEC A (65C02 only)
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void DEC_A_HasCorrectCycles()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.Accumulator = 0x42;

        // DEC A at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x3A); // DEC A
        proc.ProgramCounter = 0x0200;

        var initialCycles = proc.CycleCount;
        proc.NextStep();

        // Should take 2 cycles
        (proc.CycleCount - initialCycles).Should().Be(2);
    }

    #endregion

    #region TRB Tests

    [Fact]
    public void TRB_ZeroPage_ClearsBitsCorrectly()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Set A to 0b00001111 (0x0F) to clear lower 4 bits
        proc.Accumulator = 0x0F;

        // Set memory to 0b11111111 (0xFF)
        proc.WriteMemoryValueWithoutIncrement(0x0042, 0xFF);

        // TRB $42 at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x14); // TRB
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42); // Zero page address
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        // Memory should be 0b11110000 (0xF0) - lower 4 bits cleared
        proc.ReadMemoryValueWithoutCycle(0x0042).Should().Be(0xF0);
        proc.ProgramCounter.Should().Be(0x0202);
    }

    [Fact]
    public void TRB_Absolute_ClearsBitsCorrectly()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Set A to 0b10101010 (0xAA)
        proc.Accumulator = 0xAA;

        // Set memory to 0b11111111 (0xFF)
        proc.WriteMemoryValueWithoutIncrement(0x1234, 0xFF);

        // TRB $1234 at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x1C); // TRB
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x34); // Low byte
        proc.WriteMemoryValueWithoutIncrement(0x0202, 0x12); // High byte
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        // Memory should be 0b01010101 (0x55) - alternating bits cleared
        proc.ReadMemoryValueWithoutCycle(0x1234).Should().Be(0x55);
        proc.ProgramCounter.Should().Be(0x0203);
    }

    [Fact]
    public void TRB_SetsZeroFlag_WhenNoCommonBits()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Set A to 0b00001111 (0x0F)
        proc.Accumulator = 0x0F;

        // Set memory to 0b11110000 (0xF0) - no overlapping bits with A
        proc.WriteMemoryValueWithoutIncrement(0x0042, 0xF0);

        // TRB $42 at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x14); // TRB
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42);
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        // Z flag should be set because A AND memory (before modification) == 0
        proc.ZeroFlag.Should().BeTrue();
        // Memory should still be 0xF0 (no bits to clear)
        proc.ReadMemoryValueWithoutCycle(0x0042).Should().Be(0xF0);
    }

    [Fact]
    public void TRB_ClearsZeroFlag_WhenCommonBits()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Set A to 0b00001111 (0x0F)
        proc.Accumulator = 0x0F;

        // Set memory to 0b11111111 (0xFF) - has overlapping bits with A
        proc.WriteMemoryValueWithoutIncrement(0x0042, 0xFF);

        // TRB $42 at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x14); // TRB
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42);
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        // Z flag should be clear because A AND memory (before modification) != 0
        proc.ZeroFlag.Should().BeFalse();
        // Memory should be 0xF0 (lower 4 bits cleared)
        proc.ReadMemoryValueWithoutCycle(0x0042).Should().Be(0xF0);
    }

    [Theory]
    [InlineData(0x14)] // TRB Zero Page
    [InlineData(0x1C)] // TRB Absolute
    public void TRB_NotAvailableOn6502(byte opcode)
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, opcode);
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Theory]
    [InlineData(0x14)] // TRB Zero Page
    [InlineData(0x1C)] // TRB Absolute
    public void TRB_NotAvailableOn6510(byte opcode)
    {
        var proc = new Processor(ProcessorType.MOS6510);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, opcode);
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void TRB_ZeroPage_HasCorrectCycles()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.Accumulator = 0x0F;
        proc.WriteMemoryValueWithoutIncrement(0x0042, 0xFF);

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x14);
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42);
        proc.ProgramCounter = 0x0200;

        var initialCycles = proc.CycleCount;
        proc.NextStep();

        (proc.CycleCount - initialCycles).Should().Be(5);
    }

    [Fact]
    public void TRB_Absolute_HasCorrectCycles()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.Accumulator = 0x0F;
        proc.WriteMemoryValueWithoutIncrement(0x1234, 0xFF);

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x1C);
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x34);
        proc.WriteMemoryValueWithoutIncrement(0x0202, 0x12);
        proc.ProgramCounter = 0x0200;

        var initialCycles = proc.CycleCount;
        proc.NextStep();

        (proc.CycleCount - initialCycles).Should().Be(6);
    }

    #endregion

    #region TSB Tests

    [Fact]
    public void TSB_ZeroPage_SetsBitsCorrectly()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Set A to 0b00001111 (0x0F) to set lower 4 bits
        proc.Accumulator = 0x0F;

        // Set memory to 0b00000000 (0x00)
        proc.WriteMemoryValueWithoutIncrement(0x0042, 0x00);

        // TSB $42 at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x04); // TSB
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42); // Zero page address
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        // Memory should be 0b00001111 (0x0F) - lower 4 bits set
        proc.ReadMemoryValueWithoutCycle(0x0042).Should().Be(0x0F);
        proc.ProgramCounter.Should().Be(0x0202);
    }

    [Fact]
    public void TSB_Absolute_SetsBitsCorrectly()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Set A to 0b10101010 (0xAA)
        proc.Accumulator = 0xAA;

        // Set memory to 0b01010101 (0x55)
        proc.WriteMemoryValueWithoutIncrement(0x1234, 0x55);

        // TSB $1234 at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x0C); // TSB
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x34); // Low byte
        proc.WriteMemoryValueWithoutIncrement(0x0202, 0x12); // High byte
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        // Memory should be 0b11111111 (0xFF) - all bits set
        proc.ReadMemoryValueWithoutCycle(0x1234).Should().Be(0xFF);
        proc.ProgramCounter.Should().Be(0x0203);
    }

    [Fact]
    public void TSB_SetsZeroFlag_WhenNoCommonBits()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Set A to 0b00001111 (0x0F)
        proc.Accumulator = 0x0F;

        // Set memory to 0b11110000 (0xF0) - no overlapping bits with A
        proc.WriteMemoryValueWithoutIncrement(0x0042, 0xF0);

        // TSB $42 at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x04); // TSB
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42);
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        // Z flag should be set because A AND memory (before modification) == 0
        proc.ZeroFlag.Should().BeTrue();
        // Memory should now be 0xFF (bits from A set)
        proc.ReadMemoryValueWithoutCycle(0x0042).Should().Be(0xFF);
    }

    [Fact]
    public void TSB_ClearsZeroFlag_WhenCommonBits()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // Set A to 0b00001111 (0x0F)
        proc.Accumulator = 0x0F;

        // Set memory to 0b11111111 (0xFF) - has overlapping bits with A
        proc.WriteMemoryValueWithoutIncrement(0x0042, 0xFF);

        // TSB $42 at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x04); // TSB
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42);
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        // Z flag should be clear because A AND memory (before modification) != 0
        proc.ZeroFlag.Should().BeFalse();
        // Memory should still be 0xFF (all bits already set)
        proc.ReadMemoryValueWithoutCycle(0x0042).Should().Be(0xFF);
    }

    [Theory]
    [InlineData(0x04)] // TSB Zero Page
    [InlineData(0x0C)] // TSB Absolute
    public void TSB_NotAvailableOn6502(byte opcode)
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, opcode);
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Theory]
    [InlineData(0x04)] // TSB Zero Page
    [InlineData(0x0C)] // TSB Absolute
    public void TSB_NotAvailableOn6510(byte opcode)
    {
        var proc = new Processor(ProcessorType.MOS6510);
        proc.Reset();

        proc.WriteMemoryValueWithoutIncrement(0x0200, opcode);
        proc.ProgramCounter = 0x0200;

        var action = () => proc.NextStep();
        action.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public void TSB_ZeroPage_HasCorrectCycles()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.Accumulator = 0x0F;
        proc.WriteMemoryValueWithoutIncrement(0x0042, 0x00);

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x04);
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42);
        proc.ProgramCounter = 0x0200;

        var initialCycles = proc.CycleCount;
        proc.NextStep();

        (proc.CycleCount - initialCycles).Should().Be(5);
    }

    [Fact]
    public void TSB_Absolute_HasCorrectCycles()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();
        proc.Accumulator = 0x0F;
        proc.WriteMemoryValueWithoutIncrement(0x1234, 0x00);

        proc.WriteMemoryValueWithoutIncrement(0x0200, 0x0C);
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x34);
        proc.WriteMemoryValueWithoutIncrement(0x0202, 0x12);
        proc.ProgramCounter = 0x0200;

        var initialCycles = proc.CycleCount;
        proc.NextStep();

        (proc.CycleCount - initialCycles).Should().Be(6);
    }

    #endregion
}
