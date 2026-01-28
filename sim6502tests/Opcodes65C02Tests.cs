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
}
