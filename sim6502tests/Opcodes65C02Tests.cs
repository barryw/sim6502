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
}
