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

using System;

namespace sim6502.Proc;

/// <summary>
/// Processor addressing mode methods
/// </summary>
public partial class Processor
{
    protected int GetAddressByAddressingMode(AddressingMode addressingMode)
    {
        int address;
        int highByte;
        switch (addressingMode)
        {
            case AddressingMode.Absolute:
            {
                return ReadMemoryValue(ProgramCounter++) | (ReadMemoryValue(ProgramCounter++) << 8);
            }
            case AddressingMode.AbsoluteX:
            {
                //Get the low half of the address
                address = ReadMemoryValue(ProgramCounter++);

                //Get the high byte
                highByte = ReadMemoryValue(ProgramCounter++);

                //We crossed a page boundary, so an extra read has occurred.
                //However, if this is an ASL, LSR, DEC, INC, ROR, ROL or STA operation, we do not decrease it by 1.
                if (address + XRegister > 0xFF)
                    switch (CurrentOpCode)
                    {
                        case 0x1E:
                        case 0xDE:
                        case 0xFE:
                        case 0x5E:
                        case 0x3E:
                        case 0x7E:
                        case 0x9D:
                        {
                            //This is a Read Fetch Write Operation, so we don't make the extra read.
                            return (((highByte << 8) | address) + XRegister) & 0xFFFF;
                        }
                        default:
                        {
                            ReadMemoryValue((((highByte << 8) | address) + XRegister - 0xFF) & 0xFFFF);
                            break;
                        }
                    }

                return (((highByte << 8) | address) + XRegister) & 0xFFFF;
            }
            case AddressingMode.AbsoluteY:
            {
                //Get the low half of the address
                address = ReadMemoryValue(ProgramCounter++);

                //Get the high byte
                highByte = ReadMemoryValue(ProgramCounter++);

                //We crossed a page boundary, so decrease the number of cycles by 1 if the operation is not STA
                if (address + YRegister > 0xFF && CurrentOpCode != 0x99)
                    ReadMemoryValue((((highByte << 8) | address) + YRegister - 0xFF) & 0xFFFF);

                //Bitshift the high byte into place, AND with $FFFF to handle wrapping.
                return (((highByte << 8) | address) + YRegister) & 0xFFFF;
            }
            case AddressingMode.Immediate:
            {
                return ProgramCounter++;
            }
            case AddressingMode.IndirectX:
            {
                //Get the location of the address to retrieve
                address = ReadMemoryValue(ProgramCounter++);
                ReadMemoryValue(address);

                address += XRegister;

                //Now get the final Address. The is not a zero page address either.
                var finalAddress = ReadMemoryValue(address & 0xFF) | (ReadMemoryValue((address + 1) & 0xFF) << 8);
                return finalAddress;
            }
            case AddressingMode.IndirectY:
            {
                address = ReadMemoryValue(ProgramCounter++);

                var finalAddress = ReadMemoryValue(address) + (ReadMemoryValue((address + 1) & 0xFF) << 8);

                if ((finalAddress & 0xFF) + YRegister > 0xFF && CurrentOpCode != 0x91)
                    ReadMemoryValue((finalAddress + YRegister - 0xFF) & 0xFFFF);

                return (finalAddress + YRegister) & 0xFFFF;
            }
            case AddressingMode.Relative:
            {
                return ProgramCounter;
            }
            case AddressingMode.ZeroPage:
            {
                address = ReadMemoryValue(ProgramCounter++);
                return address;
            }
            case AddressingMode.ZeroPageX:
            {
                address = ReadMemoryValue(ProgramCounter++);
                ReadMemoryValue(address);

                address += XRegister;
                address &= 0xFF; // Zero page wrapping

                return address;
            }
            case AddressingMode.ZeroPageY:
            {
                address = ReadMemoryValue(ProgramCounter++);
                ReadMemoryValue(address);

                address += YRegister;
                address &= 0xFF;

                return address;
            }
            default:
                throw new InvalidOperationException(
                    $"The Address Mode '{addressingMode}' does not require an address");
        }
    }

    private AddressingMode GetAddressingMode()
    {
        switch (CurrentOpCode)
        {
            case 0x0D: //ORA
            case 0x2D: //AND
            case 0x4D: //EOR
            case 0x6D: //ADC
            case 0x8D: //STA
            case 0xAD: //LDA
            case 0xCD: //CMP
            case 0xED: //SBC
            case 0x0E: //ASL
            case 0x2E: //ROL
            case 0x4E: //LSR
            case 0x6E: //ROR
            case 0x8E: //SDX
            case 0xAE: //LDX
            case 0xCE: //DEC
            case 0xEE: //INC
            case 0x2C: //Bit
            case 0x4C: //JMP
            case 0x8C: //STY
            case 0xAC: //LDY
            case 0xCC: //CPY
            case 0xEC: //CPX
            case 0x20: //JSR
            case 0x9C: //STZ (65C02)
            {
                return AddressingMode.Absolute;
            }
            case 0x1D: //ORA
            case 0x3D: //AND
            case 0x5D: //EOR
            case 0x7D: //ADC
            case 0x9D: //STA
            case 0xBD: //LDA
            case 0xDD: //CMP
            case 0xFD: //SBC
            case 0xBC: //LDY
            case 0xFE: //INC
            case 0x1E: //ASL
            case 0x3E: //ROL
            case 0x5E: //LSR
            case 0x7E: //ROR
            case 0xDE: //DEC
            case 0x9E: //STZ (65C02)
            {
                return AddressingMode.AbsoluteX;
            }
            case 0x19: //ORA
            case 0x39: //AND
            case 0x59: //EOR
            case 0x79: //ADC
            case 0x99: //STA
            case 0xB9: //LDA
            case 0xD9: //CMP
            case 0xF9: //SBC
            case 0xBE: //LDX
            {
                return AddressingMode.AbsoluteY;
            }
            case 0x0A: //ASL
            case 0x4A: //LSR
            case 0x2A: //ROL
            case 0x6A: //ROR
            {
                return AddressingMode.Accumulator;
            }

            case 0x09: //ORA
            case 0x29: //AND
            case 0x49: //EOR
            case 0x69: //ADC
            case 0xA0: //LDY
            case 0xC0: //CPY
            case 0xE0: //CPX
            case 0xA2: //LDX
            case 0xA9: //LDA
            case 0xC9: //CMP
            case 0xE9: //SBC
            {
                return AddressingMode.Immediate;
            }
            case 0x00: //BRK
            case 0x18: //CLC
            case 0xD8: //CLD
            case 0x58: //CLI
            case 0xB8: //CLV
            case 0xCA: //DEX
            case 0x88: //DEY
            case 0xE8: //INX
            case 0xC8: //INY
            case 0xEA: //NOP
            case 0x48: //PHA
            case 0x08: //PHP
            case 0x68: //PLA
            case 0x28: //PLP
            case 0x40: //RTI
            case 0x60: //RTS
            case 0x38: //SEC
            case 0xF8: //SED
            case 0x78: //SEI
            case 0xAA: //TAX
            case 0xA8: //TAY
            case 0xBA: //TSX
            case 0x8A: //TXA
            case 0x9A: //TXS
            case 0x98: //TYA
            case 0xDA: //PHX (65C02)
            case 0xFA: //PLX (65C02)
            case 0x5A: //PHY (65C02)
            case 0x7A: //PLY (65C02)
            case 0x1A: //INC A (65C02)
            case 0x3A: //DEC A (65C02)
            {
                return AddressingMode.Implied;
            }
            case 0x6C:
            {
                return AddressingMode.Indirect;
            }

            case 0x61: //ADC
            case 0x21: //AND
            case 0xC1: //CMP
            case 0x41: //EOR
            case 0xA1: //LDA
            case 0x01: //ORA
            case 0xE1: //SBC
            case 0x81: //STA
            {
                return AddressingMode.IndirectX;
            }
            case 0x71: //ADC
            case 0x31: //AND
            case 0xD1: //CMP
            case 0x51: //EOR
            case 0xB1: //LDA
            case 0x11: //ORA
            case 0xF1: //SBC
            case 0x91: //STA
            {
                return AddressingMode.IndirectY;
            }
            case 0x80: //BRA (65C02)
            case 0x90: //BCC
            case 0xB0: //BCS
            case 0xF0: //BEQ
            case 0x30: //BMI
            case 0xD0: //BNE
            case 0x10: //BPL
            case 0x50: //BVC
            case 0x70: //BVS
            {
                return AddressingMode.Relative;
            }
            case 0x65: //ADC
            case 0x25: //AND
            case 0x06: //ASL
            case 0x24: //BIT
            case 0xC5: //CMP
            case 0xE4: //CPX
            case 0xC4: //CPY
            case 0xC6: //DEC
            case 0x45: //EOR
            case 0xE6: //INC
            case 0xA5: //LDA
            case 0xA6: //LDX
            case 0xA4: //LDY
            case 0x46: //LSR
            case 0x05: //ORA
            case 0x26: //ROL
            case 0x66: //ROR
            case 0xE5: //SBC
            case 0x85: //STA
            case 0x86: //STX
            case 0x84: //STY
            case 0x64: //STZ (65C02)
            {
                return AddressingMode.ZeroPage;
            }
            case 0x75: //ADC
            case 0x35: //AND
            case 0x16: //ASL
            case 0xD5: //CMP
            case 0xD6: //DEC
            case 0x55: //EOR
            case 0xF6: //INC
            case 0xB5: //LDA
            case 0xB6: //LDX
            case 0xB4: //LDY
            case 0x56: //LSR
            case 0x15: //ORA
            case 0x36: //ROL
            case 0x76: //ROR
            case 0xF5: //SBC
            case 0x95: //STA
            case 0x96: //STX
            case 0x94: //STY
            case 0x74: //STZ (65C02)
            {
                return AddressingMode.ZeroPageX;
            }
            default:
                throw new NotSupportedException($"The OpCode {CurrentOpCode.ToString()} @ address {ProgramCounter.ToString()} is not supported.");
        }
    }
}
