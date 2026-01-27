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

using sim6502.Proc;
using Xunit;

namespace sim6502tests;

/// <summary>
/// Tests for the OpcodeRegistry
/// </summary>
public class OpcodeRegistryTests
{
    [Fact]
    public void Registry_Should_Contain_All_Opcodes()
    {
        // The 6502 has 151 official opcodes
        Assert.Equal(151, Processor.OpcodeRegistry.Count);
    }

    [Theory]
    [InlineData(0x69, "ADC", AddressingMode.Immediate, 2, 2)]
    [InlineData(0xA9, "LDA", AddressingMode.Immediate, 2, 2)]
    [InlineData(0x4C, "JMP", AddressingMode.Absolute, 3, 3)]
    [InlineData(0x00, "BRK", AddressingMode.Implied, 1, 7)]
    [InlineData(0xEA, "NOP", AddressingMode.Implied, 1, 2)]
    [InlineData(0x20, "JSR", AddressingMode.Absolute, 3, 6)]
    [InlineData(0x60, "RTS", AddressingMode.Implied, 1, 6)]
    [InlineData(0x40, "RTI", AddressingMode.Implied, 1, 6)]
    public void Opcode_Should_Have_Correct_Metadata(byte opcodeValue, string expectedMnemonic,
        AddressingMode expectedMode, int expectedBytes, int expectedCycles)
    {
        var opcode = Processor.OpcodeRegistry.GetOpcode(opcodeValue);

        Assert.NotNull(opcode);
        Assert.Equal(opcodeValue, opcode.Opcode);
        Assert.Equal(expectedMnemonic, opcode.Mnemonic);
        Assert.Equal(expectedMode, opcode.AddressingMode);
        Assert.Equal(expectedBytes, opcode.Bytes);
        Assert.Equal(expectedCycles, opcode.BaseCycles);
        Assert.NotNull(opcode.Handler);
    }

    [Fact]
    public void Invalid_Opcode_Should_Return_Null()
    {
        // Test an illegal/undocumented opcode
        var opcode = Processor.OpcodeRegistry.GetOpcode(0xFF);
        Assert.Null(opcode);
    }

    [Fact]
    public void All_ADC_Variants_Should_Be_Registered()
    {
        var adcOpcodes = new byte[] { 0x69, 0x65, 0x75, 0x6D, 0x7D, 0x79, 0x61, 0x71 };

        foreach (var opcodeValue in adcOpcodes)
        {
            var opcode = Processor.OpcodeRegistry.GetOpcode(opcodeValue);
            Assert.NotNull(opcode);
            Assert.Equal("ADC", opcode.Mnemonic);
        }
    }

    [Fact]
    public void All_Branch_Instructions_Should_Be_Registered()
    {
        var branchOpcodes = new Dictionary<byte, string>
        {
            { 0x90, "BCC" },
            { 0xB0, "BCS" },
            { 0xF0, "BEQ" },
            { 0x30, "BMI" },
            { 0xD0, "BNE" },
            { 0x10, "BPL" },
            { 0x50, "BVC" },
            { 0x70, "BVS" }
        };

        foreach (var (opcodeValue, mnemonic) in branchOpcodes)
        {
            var opcode = Processor.OpcodeRegistry.GetOpcode(opcodeValue);
            Assert.NotNull(opcode);
            Assert.Equal(mnemonic, opcode.Mnemonic);
            Assert.Equal(AddressingMode.Relative, opcode.AddressingMode);
            Assert.Equal(2, opcode.Bytes);
            Assert.Equal(2, opcode.BaseCycles);
        }
    }

    [Fact]
    public void All_Clear_Flag_Instructions_Should_Be_Registered()
    {
        var clearOpcodes = new Dictionary<byte, string>
        {
            { 0x18, "CLC" },
            { 0xD8, "CLD" },
            { 0x58, "CLI" },
            { 0xB8, "CLV" }
        };

        foreach (var (opcodeValue, mnemonic) in clearOpcodes)
        {
            var opcode = Processor.OpcodeRegistry.GetOpcode(opcodeValue);
            Assert.NotNull(opcode);
            Assert.Equal(mnemonic, opcode.Mnemonic);
            Assert.Equal(AddressingMode.Implied, opcode.AddressingMode);
            Assert.Equal(1, opcode.Bytes);
            Assert.Equal(2, opcode.BaseCycles);
        }
    }

    [Fact]
    public void All_Set_Flag_Instructions_Should_Be_Registered()
    {
        var setOpcodes = new Dictionary<byte, string>
        {
            { 0x38, "SEC" },
            { 0xF8, "SED" },
            { 0x78, "SEI" }
        };

        foreach (var (opcodeValue, mnemonic) in setOpcodes)
        {
            var opcode = Processor.OpcodeRegistry.GetOpcode(opcodeValue);
            Assert.NotNull(opcode);
            Assert.Equal(mnemonic, opcode.Mnemonic);
            Assert.Equal(AddressingMode.Implied, opcode.AddressingMode);
            Assert.Equal(1, opcode.Bytes);
            Assert.Equal(2, opcode.BaseCycles);
        }
    }

    [Fact]
    public void All_Transfer_Instructions_Should_Be_Registered()
    {
        var transferOpcodes = new Dictionary<byte, string>
        {
            { 0xAA, "TAX" },
            { 0xA8, "TAY" },
            { 0xBA, "TSX" },
            { 0x8A, "TXA" },
            { 0x9A, "TXS" },
            { 0x98, "TYA" }
        };

        foreach (var (opcodeValue, mnemonic) in transferOpcodes)
        {
            var opcode = Processor.OpcodeRegistry.GetOpcode(opcodeValue);
            Assert.NotNull(opcode);
            Assert.Equal(mnemonic, opcode.Mnemonic);
            Assert.Equal(AddressingMode.Implied, opcode.AddressingMode);
            Assert.Equal(1, opcode.Bytes);
            Assert.Equal(2, opcode.BaseCycles);
        }
    }
}
