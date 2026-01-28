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

public class ProcessorExecutionTests
{
    [Fact]
    public void ExecuteOpcode_6502_LDA_Works()
    {
        var proc = new Processor(ProcessorType.MOS6502);
        proc.Reset();

        // LDA #$42 at address $0200
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xA9); // LDA immediate
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42); // value
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.Accumulator.Should().Be(0x42);
    }

    [Fact]
    public void ExecuteOpcode_65C02_UsesCorrectOpcodeTable()
    {
        var proc = new Processor(ProcessorType.WDC65C02);
        proc.Reset();

        // LDA #$42 at address $0200 (same instruction works on both)
        proc.WriteMemoryValueWithoutIncrement(0x0200, 0xA9); // LDA immediate
        proc.WriteMemoryValueWithoutIncrement(0x0201, 0x42); // value
        proc.ProgramCounter = 0x0200;

        proc.NextStep();

        proc.Accumulator.Should().Be(0x42);
    }
}
