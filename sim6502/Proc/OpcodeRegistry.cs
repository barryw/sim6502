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

namespace sim6502.Proc;

/// <summary>
/// Processor partial class containing the opcode registry
/// </summary>
public partial class Processor
{
    /// <summary>
    /// Static registry containing all 6502 opcode definitions and handlers.
    /// This nested class has access to protected Processor methods for opcode handlers.
    /// </summary>
    public static class OpcodeRegistry
    {
        // Processor-specific opcode dictionaries
        private static readonly Dictionary<ProcessorType, Dictionary<byte, OpcodeInfo>> _processorOpcodes = new();

        // Legacy single dictionary for backward compatibility (references 6502)
        private static readonly Dictionary<byte, OpcodeInfo> _opcodes;

        /// <summary>
        /// Static constructor initializes the opcode registry
        /// </summary>
        static OpcodeRegistry()
        {
            // Initialize dictionaries for each processor type
            _processorOpcodes[ProcessorType.MOS6502] = new Dictionary<byte, OpcodeInfo>();
            _processorOpcodes[ProcessorType.MOS6510] = _processorOpcodes[ProcessorType.MOS6502]; // 6510 shares 6502's ISA
            _processorOpcodes[ProcessorType.WDC65C02] = new Dictionary<byte, OpcodeInfo>(); // 65C02 will have its own set

            RegisterAllOpcodes();

            // Legacy _opcodes dictionary points to 6502 for backward compatibility
            _opcodes = _processorOpcodes[ProcessorType.MOS6502];
        }

        /// <summary>
        /// Gets the opcode information for a given opcode byte (defaults to 6502 for backward compatibility)
        /// </summary>
        /// <param name="opcode">The opcode byte value (0x00-0xFF)</param>
        /// <returns>The OpcodeInfo if found, null otherwise</returns>
        public static OpcodeInfo? GetOpcode(byte opcode)
        {
            return GetOpcode(opcode, ProcessorType.MOS6502);
        }

        /// <summary>
        /// Gets the opcode information for a given opcode byte and processor type
        /// </summary>
        /// <param name="opcode">The opcode byte value (0x00-0xFF)</param>
        /// <param name="processorType">The processor type</param>
        /// <returns>The OpcodeInfo if found, null otherwise</returns>
        public static OpcodeInfo? GetOpcode(byte opcode, ProcessorType processorType)
        {
            if (_processorOpcodes.TryGetValue(processorType, out var opcodeDict))
            {
                return opcodeDict.TryGetValue(opcode, out var info) ? info : null;
            }
            return null;
        }

        /// <summary>
        /// Gets the total number of registered opcodes (defaults to 6502 for backward compatibility)
        /// </summary>
        public static int Count => _opcodes.Count;

        /// <summary>
        /// Gets the total number of registered opcodes for a specific processor type
        /// </summary>
        /// <param name="processorType">The processor type</param>
        /// <returns>The number of opcodes registered for that processor</returns>
        public static int GetOpcodeCount(ProcessorType processorType)
        {
            if (_processorOpcodes.TryGetValue(processorType, out var opcodeDict))
            {
                return opcodeDict.Count;
            }
            return 0;
        }

        /// <summary>
        /// Registers all 151 official 6502 opcodes and 65C02 extensions
        /// </summary>
        private static void RegisterAllOpcodes()
        {
            Register6502Opcodes();
            Register65C02Opcodes();
        }

        /// <summary>
        /// Registers 6502 opcodes
        /// </summary>
        private static void Register6502Opcodes()
        {
        #region Add With Carry (ADC)

        Register(0x69, "ADC", AddressingMode.Immediate, 2, 2,
            p => p.AddWithCarryOperation(AddressingMode.Immediate));

        Register(0x65, "ADC", AddressingMode.ZeroPage, 2, 3,
            p => p.AddWithCarryOperation(AddressingMode.ZeroPage));

        Register(0x75, "ADC", AddressingMode.ZeroPageX, 2, 4,
            p => p.AddWithCarryOperation(AddressingMode.ZeroPageX));

        Register(0x6D, "ADC", AddressingMode.Absolute, 3, 4,
            p => p.AddWithCarryOperation(AddressingMode.Absolute));

        Register(0x7D, "ADC", AddressingMode.AbsoluteX, 3, 4,
            p => p.AddWithCarryOperation(AddressingMode.AbsoluteX));

        Register(0x79, "ADC", AddressingMode.AbsoluteY, 3, 4,
            p => p.AddWithCarryOperation(AddressingMode.AbsoluteY));

        Register(0x61, "ADC", AddressingMode.IndirectX, 2, 6,
            p => p.AddWithCarryOperation(AddressingMode.IndirectX));

        Register(0x71, "ADC", AddressingMode.IndirectY, 2, 5,
            p => p.AddWithCarryOperation(AddressingMode.IndirectY));

        #endregion

        #region Logical AND (AND)

        Register(0x29, "AND", AddressingMode.Immediate, 2, 2,
            p => p.AndOperation(AddressingMode.Immediate));

        Register(0x25, "AND", AddressingMode.ZeroPage, 2, 3,
            p => p.AndOperation(AddressingMode.ZeroPage));

        Register(0x35, "AND", AddressingMode.ZeroPageX, 2, 4,
            p => p.AndOperation(AddressingMode.ZeroPageX));

        Register(0x2D, "AND", AddressingMode.Absolute, 3, 4,
            p => p.AndOperation(AddressingMode.Absolute));

        Register(0x3D, "AND", AddressingMode.AbsoluteX, 3, 4,
            p => p.AndOperation(AddressingMode.AbsoluteX));

        Register(0x39, "AND", AddressingMode.AbsoluteY, 3, 4,
            p => p.AndOperation(AddressingMode.AbsoluteY));

        Register(0x21, "AND", AddressingMode.IndirectX, 2, 6,
            p => p.AndOperation(AddressingMode.IndirectX));

        Register(0x31, "AND", AddressingMode.IndirectY, 2, 5,
            p => p.AndOperation(AddressingMode.IndirectY));

        #endregion

        #region Arithmetic Shift Left (ASL)

        Register(0x0A, "ASL", AddressingMode.Accumulator, 1, 2,
            p => p.AslOperation(AddressingMode.Accumulator));

        Register(0x06, "ASL", AddressingMode.ZeroPage, 2, 5,
            p => p.AslOperation(AddressingMode.ZeroPage));

        Register(0x16, "ASL", AddressingMode.ZeroPageX, 2, 6,
            p => p.AslOperation(AddressingMode.ZeroPageX));

        Register(0x0E, "ASL", AddressingMode.Absolute, 3, 6,
            p => p.AslOperation(AddressingMode.Absolute));

        Register(0x1E, "ASL", AddressingMode.AbsoluteX, 3, 7,
            p => { p.AslOperation(AddressingMode.AbsoluteX); p.IncrementCycleCount(); });

        #endregion

        #region Branch Instructions

        Register(0x90, "BCC", AddressingMode.Relative, 2, 2,
            p => p.BranchOperation(!p.CarryFlag));

        Register(0xB0, "BCS", AddressingMode.Relative, 2, 2,
            p => p.BranchOperation(p.CarryFlag));

        Register(0xF0, "BEQ", AddressingMode.Relative, 2, 2,
            p => p.BranchOperation(p.ZeroFlag));

        Register(0x30, "BMI", AddressingMode.Relative, 2, 2,
            p => p.BranchOperation(p.NegativeFlag));

        Register(0xD0, "BNE", AddressingMode.Relative, 2, 2,
            p => p.BranchOperation(!p.ZeroFlag));

        Register(0x10, "BPL", AddressingMode.Relative, 2, 2,
            p => p.BranchOperation(!p.NegativeFlag));

        Register(0x50, "BVC", AddressingMode.Relative, 2, 2,
            p => p.BranchOperation(!p.OverflowFlag));

        Register(0x70, "BVS", AddressingMode.Relative, 2, 2,
            p => p.BranchOperation(p.OverflowFlag));

        #endregion

        #region Bit Test (BIT)

        Register(0x24, "BIT", AddressingMode.ZeroPage, 2, 3,
            p => p.BitOperation(AddressingMode.ZeroPage));

        Register(0x2C, "BIT", AddressingMode.Absolute, 3, 4,
            p => p.BitOperation(AddressingMode.Absolute));

        #endregion

        #region Break (BRK)

        Register(0x00, "BRK", AddressingMode.Implied, 1, 7,
            p => p.BreakOperation(true, 0xFFFE));

        #endregion

        #region Clear Flag Instructions

        Register(0x18, "CLC", AddressingMode.Implied, 1, 2,
            p => { p.CarryFlag = false; p.IncrementCycleCount(); });

        Register(0xD8, "CLD", AddressingMode.Implied, 1, 2,
            p => { p.DecimalFlag = false; p.IncrementCycleCount(); });

        Register(0x58, "CLI", AddressingMode.Implied, 1, 2,
            p => { p.DisableInterruptFlag = false; p.IncrementCycleCount(); });

        Register(0xB8, "CLV", AddressingMode.Implied, 1, 2,
            p => { p.OverflowFlag = false; p.IncrementCycleCount(); });

        #endregion

        #region Compare Accumulator (CMP)

        Register(0xC9, "CMP", AddressingMode.Immediate, 2, 2,
            p => p.CompareOperation(AddressingMode.Immediate, p.Accumulator));

        Register(0xC5, "CMP", AddressingMode.ZeroPage, 2, 3,
            p => p.CompareOperation(AddressingMode.ZeroPage, p.Accumulator));

        Register(0xD5, "CMP", AddressingMode.ZeroPageX, 2, 4,
            p => p.CompareOperation(AddressingMode.ZeroPageX, p.Accumulator));

        Register(0xCD, "CMP", AddressingMode.Absolute, 3, 4,
            p => p.CompareOperation(AddressingMode.Absolute, p.Accumulator));

        Register(0xDD, "CMP", AddressingMode.AbsoluteX, 3, 4,
            p => p.CompareOperation(AddressingMode.AbsoluteX, p.Accumulator));

        Register(0xD9, "CMP", AddressingMode.AbsoluteY, 3, 4,
            p => p.CompareOperation(AddressingMode.AbsoluteY, p.Accumulator));

        Register(0xC1, "CMP", AddressingMode.IndirectX, 2, 6,
            p => p.CompareOperation(AddressingMode.IndirectX, p.Accumulator));

        Register(0xD1, "CMP", AddressingMode.IndirectY, 2, 5,
            p => p.CompareOperation(AddressingMode.IndirectY, p.Accumulator));

        #endregion

        #region Compare X Register (CPX)

        Register(0xE0, "CPX", AddressingMode.Immediate, 2, 2,
            p => p.CompareOperation(AddressingMode.Immediate, p.XRegister));

        Register(0xE4, "CPX", AddressingMode.ZeroPage, 2, 3,
            p => p.CompareOperation(AddressingMode.ZeroPage, p.XRegister));

        Register(0xEC, "CPX", AddressingMode.Absolute, 3, 4,
            p => p.CompareOperation(AddressingMode.Absolute, p.XRegister));

        #endregion

        #region Compare Y Register (CPY)

        Register(0xC0, "CPY", AddressingMode.Immediate, 2, 2,
            p => p.CompareOperation(AddressingMode.Immediate, p.YRegister));

        Register(0xC4, "CPY", AddressingMode.ZeroPage, 2, 3,
            p => p.CompareOperation(AddressingMode.ZeroPage, p.YRegister));

        Register(0xCC, "CPY", AddressingMode.Absolute, 3, 4,
            p => p.CompareOperation(AddressingMode.Absolute, p.YRegister));

        #endregion

        #region Decrement Memory (DEC)

        Register(0xC6, "DEC", AddressingMode.ZeroPage, 2, 5,
            p => p.ChangeMemoryByOne(AddressingMode.ZeroPage, true));

        Register(0xD6, "DEC", AddressingMode.ZeroPageX, 2, 6,
            p => p.ChangeMemoryByOne(AddressingMode.ZeroPageX, true));

        Register(0xCE, "DEC", AddressingMode.Absolute, 3, 6,
            p => p.ChangeMemoryByOne(AddressingMode.Absolute, true));

        Register(0xDE, "DEC", AddressingMode.AbsoluteX, 3, 7,
            p => { p.ChangeMemoryByOne(AddressingMode.AbsoluteX, true); p.IncrementCycleCount(); });

        #endregion

        #region Decrement Register Instructions

        Register(0xCA, "DEX", AddressingMode.Implied, 1, 2,
            p => p.ChangeRegisterByOne(true, true));

        Register(0x88, "DEY", AddressingMode.Implied, 1, 2,
            p => p.ChangeRegisterByOne(false, true));

        #endregion

        #region Exclusive OR (EOR)

        Register(0x49, "EOR", AddressingMode.Immediate, 2, 2,
            p => p.EorOperation(AddressingMode.Immediate));

        Register(0x45, "EOR", AddressingMode.ZeroPage, 2, 3,
            p => p.EorOperation(AddressingMode.ZeroPage));

        Register(0x55, "EOR", AddressingMode.ZeroPageX, 2, 4,
            p => p.EorOperation(AddressingMode.ZeroPageX));

        Register(0x4D, "EOR", AddressingMode.Absolute, 3, 4,
            p => p.EorOperation(AddressingMode.Absolute));

        Register(0x5D, "EOR", AddressingMode.AbsoluteX, 3, 4,
            p => p.EorOperation(AddressingMode.AbsoluteX));

        Register(0x59, "EOR", AddressingMode.AbsoluteY, 3, 4,
            p => p.EorOperation(AddressingMode.AbsoluteY));

        Register(0x41, "EOR", AddressingMode.IndirectX, 2, 6,
            p => p.EorOperation(AddressingMode.IndirectX));

        Register(0x51, "EOR", AddressingMode.IndirectY, 2, 5,
            p => p.EorOperation(AddressingMode.IndirectY));

        #endregion

        #region Increment Memory (INC)

        Register(0xE6, "INC", AddressingMode.ZeroPage, 2, 5,
            p => p.ChangeMemoryByOne(AddressingMode.ZeroPage, false));

        Register(0xF6, "INC", AddressingMode.ZeroPageX, 2, 6,
            p => p.ChangeMemoryByOne(AddressingMode.ZeroPageX, false));

        Register(0xEE, "INC", AddressingMode.Absolute, 3, 6,
            p => p.ChangeMemoryByOne(AddressingMode.Absolute, false));

        Register(0xFE, "INC", AddressingMode.AbsoluteX, 3, 7,
            p => { p.ChangeMemoryByOne(AddressingMode.AbsoluteX, false); p.IncrementCycleCount(); });

        #endregion

        #region Increment Register Instructions

        Register(0xE8, "INX", AddressingMode.Implied, 1, 2,
            p => p.ChangeRegisterByOne(true, false));

        Register(0xC8, "INY", AddressingMode.Implied, 1, 2,
            p => p.ChangeRegisterByOne(false, false));

        #endregion

        #region Jump Instructions (JMP)

        Register(0x4C, "JMP", AddressingMode.Absolute, 3, 3,
            p => p.ProgramCounter = p.GetAddressByAddressingMode(AddressingMode.Absolute));

        Register(0x6C, "JMP", AddressingMode.Indirect, 3, 5,
            p =>
            {
                p.ProgramCounter = p.GetAddressByAddressingMode(AddressingMode.Absolute);
                if ((p.ProgramCounter & 0xFF) == 0xFF)
                {
                    int address = p.ReadMemoryValue(p.ProgramCounter);
                    address += 256 * p.ReadMemoryValue(p.ProgramCounter - 255);
                    p.ProgramCounter = address;
                }
                else
                {
                    p.ProgramCounter = p.GetAddressByAddressingMode(AddressingMode.Absolute);
                }
            });

        #endregion

        #region Jump to Subroutine (JSR)

        Register(0x20, "JSR", AddressingMode.Absolute, 3, 6,
            p => p.JumpToSubRoutineOperation());

        #endregion

        #region Load Accumulator (LDA)

        Register(0xA9, "LDA", AddressingMode.Immediate, 2, 2,
            p =>
            {
                p.Accumulator = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.Immediate));
                p.SetZeroFlag(p.Accumulator);
                p.SetNegativeFlag(p.Accumulator);
            });

        Register(0xA5, "LDA", AddressingMode.ZeroPage, 2, 3,
            p =>
            {
                p.Accumulator = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.ZeroPage));
                p.SetZeroFlag(p.Accumulator);
                p.SetNegativeFlag(p.Accumulator);
            });

        Register(0xB5, "LDA", AddressingMode.ZeroPageX, 2, 4,
            p =>
            {
                p.Accumulator = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.ZeroPageX));
                p.SetZeroFlag(p.Accumulator);
                p.SetNegativeFlag(p.Accumulator);
            });

        Register(0xAD, "LDA", AddressingMode.Absolute, 3, 4,
            p =>
            {
                p.Accumulator = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.Absolute));
                p.SetZeroFlag(p.Accumulator);
                p.SetNegativeFlag(p.Accumulator);
            });

        Register(0xBD, "LDA", AddressingMode.AbsoluteX, 3, 4,
            p =>
            {
                p.Accumulator = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.AbsoluteX));
                p.SetZeroFlag(p.Accumulator);
                p.SetNegativeFlag(p.Accumulator);
            });

        Register(0xB9, "LDA", AddressingMode.AbsoluteY, 3, 4,
            p =>
            {
                p.Accumulator = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.AbsoluteY));
                p.SetZeroFlag(p.Accumulator);
                p.SetNegativeFlag(p.Accumulator);
            });

        Register(0xA1, "LDA", AddressingMode.IndirectX, 2, 6,
            p =>
            {
                p.Accumulator = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.IndirectX));
                p.SetZeroFlag(p.Accumulator);
                p.SetNegativeFlag(p.Accumulator);
            });

        Register(0xB1, "LDA", AddressingMode.IndirectY, 2, 5,
            p =>
            {
                p.Accumulator = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.IndirectY));
                p.SetZeroFlag(p.Accumulator);
                p.SetNegativeFlag(p.Accumulator);
            });

        #endregion

        #region Load X Register (LDX)

        Register(0xA2, "LDX", AddressingMode.Immediate, 2, 2,
            p =>
            {
                p.XRegister = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.Immediate));
                p.SetZeroFlag(p.XRegister);
                p.SetNegativeFlag(p.XRegister);
            });

        Register(0xA6, "LDX", AddressingMode.ZeroPage, 2, 3,
            p =>
            {
                p.XRegister = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.ZeroPage));
                p.SetZeroFlag(p.XRegister);
                p.SetNegativeFlag(p.XRegister);
            });

        Register(0xB6, "LDX", AddressingMode.ZeroPageY, 2, 4,
            p =>
            {
                p.XRegister = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.ZeroPageY));
                p.SetZeroFlag(p.XRegister);
                p.SetNegativeFlag(p.XRegister);
            });

        Register(0xAE, "LDX", AddressingMode.Absolute, 3, 4,
            p =>
            {
                p.XRegister = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.Absolute));
                p.SetZeroFlag(p.XRegister);
                p.SetNegativeFlag(p.XRegister);
            });

        Register(0xBE, "LDX", AddressingMode.AbsoluteY, 3, 4,
            p =>
            {
                p.XRegister = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.AbsoluteY));
                p.SetZeroFlag(p.XRegister);
                p.SetNegativeFlag(p.XRegister);
            });

        #endregion

        #region Load Y Register (LDY)

        Register(0xA0, "LDY", AddressingMode.Immediate, 2, 2,
            p =>
            {
                p.YRegister = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.Immediate));
                p.SetZeroFlag(p.YRegister);
                p.SetNegativeFlag(p.YRegister);
            });

        Register(0xA4, "LDY", AddressingMode.ZeroPage, 2, 3,
            p =>
            {
                p.YRegister = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.ZeroPage));
                p.SetZeroFlag(p.YRegister);
                p.SetNegativeFlag(p.YRegister);
            });

        Register(0xB4, "LDY", AddressingMode.ZeroPageX, 2, 4,
            p =>
            {
                p.YRegister = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.ZeroPageX));
                p.SetZeroFlag(p.YRegister);
                p.SetNegativeFlag(p.YRegister);
            });

        Register(0xAC, "LDY", AddressingMode.Absolute, 3, 4,
            p =>
            {
                p.YRegister = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.Absolute));
                p.SetZeroFlag(p.YRegister);
                p.SetNegativeFlag(p.YRegister);
            });

        Register(0xBC, "LDY", AddressingMode.AbsoluteX, 3, 4,
            p =>
            {
                p.YRegister = p.ReadMemoryValue(p.GetAddressByAddressingMode(AddressingMode.AbsoluteX));
                p.SetZeroFlag(p.YRegister);
                p.SetNegativeFlag(p.YRegister);
            });

        #endregion

        #region Logical Shift Right (LSR)

        Register(0x4A, "LSR", AddressingMode.Accumulator, 1, 2,
            p => p.LsrOperation(AddressingMode.Accumulator));

        Register(0x46, "LSR", AddressingMode.ZeroPage, 2, 5,
            p => p.LsrOperation(AddressingMode.ZeroPage));

        Register(0x56, "LSR", AddressingMode.ZeroPageX, 2, 6,
            p => p.LsrOperation(AddressingMode.ZeroPageX));

        Register(0x4E, "LSR", AddressingMode.Absolute, 3, 6,
            p => p.LsrOperation(AddressingMode.Absolute));

        Register(0x5E, "LSR", AddressingMode.AbsoluteX, 3, 7,
            p => { p.LsrOperation(AddressingMode.AbsoluteX); p.IncrementCycleCount(); });

        #endregion

        #region No Operation (NOP)

        Register(0xEA, "NOP", AddressingMode.Implied, 1, 2,
            p => p.IncrementCycleCount());

        #endregion

        #region Logical OR (ORA)

        Register(0x09, "ORA", AddressingMode.Immediate, 2, 2,
            p => p.OrOperation(AddressingMode.Immediate));

        Register(0x05, "ORA", AddressingMode.ZeroPage, 2, 3,
            p => p.OrOperation(AddressingMode.ZeroPage));

        Register(0x15, "ORA", AddressingMode.ZeroPageX, 2, 4,
            p => p.OrOperation(AddressingMode.ZeroPageX));

        Register(0x0D, "ORA", AddressingMode.Absolute, 3, 4,
            p => p.OrOperation(AddressingMode.Absolute));

        Register(0x1D, "ORA", AddressingMode.AbsoluteX, 3, 4,
            p => p.OrOperation(AddressingMode.AbsoluteX));

        Register(0x19, "ORA", AddressingMode.AbsoluteY, 3, 4,
            p => p.OrOperation(AddressingMode.AbsoluteY));

        Register(0x01, "ORA", AddressingMode.IndirectX, 2, 6,
            p => p.OrOperation(AddressingMode.IndirectX));

        Register(0x11, "ORA", AddressingMode.IndirectY, 2, 5,
            p => p.OrOperation(AddressingMode.IndirectY));

        #endregion

        #region Push/Pull Stack Instructions

        Register(0x48, "PHA", AddressingMode.Implied, 1, 3,
            p =>
            {
                p.ReadMemoryValue(p.ProgramCounter + 1);
                p.PokeStack((byte)p.Accumulator);
                p.StackPointer--;
                p.IncrementCycleCount();
            });

        Register(0x08, "PHP", AddressingMode.Implied, 1, 3,
            p =>
            {
                p.ReadMemoryValue(p.ProgramCounter + 1);
                p.PushFlagsOperation();
                p.StackPointer--;
                p.IncrementCycleCount();
            });

        Register(0x68, "PLA", AddressingMode.Implied, 1, 4,
            p =>
            {
                p.ReadMemoryValue(p.ProgramCounter + 1);
                p.StackPointer++;
                p.IncrementCycleCount();
                p.Accumulator = p.PeekStack();
                p.SetNegativeFlag(p.Accumulator);
                p.SetZeroFlag(p.Accumulator);
                p.IncrementCycleCount();
            });

        Register(0x28, "PLP", AddressingMode.Implied, 1, 4,
            p =>
            {
                p.ReadMemoryValue(p.ProgramCounter + 1);
                p.StackPointer++;
                p.IncrementCycleCount();
                p.PullFlagsOperation();
                p.IncrementCycleCount();
            });

        #endregion

        #region Rotate Left (ROL)

        Register(0x2A, "ROL", AddressingMode.Accumulator, 1, 2,
            p => p.RolOperation(AddressingMode.Accumulator));

        Register(0x26, "ROL", AddressingMode.ZeroPage, 2, 5,
            p => p.RolOperation(AddressingMode.ZeroPage));

        Register(0x36, "ROL", AddressingMode.ZeroPageX, 2, 6,
            p => p.RolOperation(AddressingMode.ZeroPageX));

        Register(0x2E, "ROL", AddressingMode.Absolute, 3, 6,
            p => p.RolOperation(AddressingMode.Absolute));

        Register(0x3E, "ROL", AddressingMode.AbsoluteX, 3, 7,
            p => { p.RolOperation(AddressingMode.AbsoluteX); p.IncrementCycleCount(); });

        #endregion

        #region Rotate Right (ROR)

        Register(0x6A, "ROR", AddressingMode.Accumulator, 1, 2,
            p => p.RorOperation(AddressingMode.Accumulator));

        Register(0x66, "ROR", AddressingMode.ZeroPage, 2, 5,
            p => p.RorOperation(AddressingMode.ZeroPage));

        Register(0x76, "ROR", AddressingMode.ZeroPageX, 2, 6,
            p => p.RorOperation(AddressingMode.ZeroPageX));

        Register(0x6E, "ROR", AddressingMode.Absolute, 3, 6,
            p => p.RorOperation(AddressingMode.Absolute));

        Register(0x7E, "ROR", AddressingMode.AbsoluteX, 3, 7,
            p => { p.RorOperation(AddressingMode.AbsoluteX); p.IncrementCycleCount(); });

        #endregion

        #region Return from Interrupt (RTI)

        Register(0x40, "RTI", AddressingMode.Implied, 1, 6,
            p => p.ReturnFromInterruptOperation());

        #endregion

        #region Return from Subroutine (RTS)

        Register(0x60, "RTS", AddressingMode.Implied, 1, 6,
            p => p.ReturnFromSubRoutineOperation());

        #endregion

        #region Subtract with Borrow (SBC)

        Register(0xE9, "SBC", AddressingMode.Immediate, 2, 2,
            p => p.SubtractWithBorrowOperation(AddressingMode.Immediate));

        Register(0xE5, "SBC", AddressingMode.ZeroPage, 2, 3,
            p => p.SubtractWithBorrowOperation(AddressingMode.ZeroPage));

        Register(0xF5, "SBC", AddressingMode.ZeroPageX, 2, 4,
            p => p.SubtractWithBorrowOperation(AddressingMode.ZeroPageX));

        Register(0xED, "SBC", AddressingMode.Absolute, 3, 4,
            p => p.SubtractWithBorrowOperation(AddressingMode.Absolute));

        Register(0xFD, "SBC", AddressingMode.AbsoluteX, 3, 4,
            p => p.SubtractWithBorrowOperation(AddressingMode.AbsoluteX));

        Register(0xF9, "SBC", AddressingMode.AbsoluteY, 3, 4,
            p => p.SubtractWithBorrowOperation(AddressingMode.AbsoluteY));

        Register(0xE1, "SBC", AddressingMode.IndirectX, 2, 6,
            p => p.SubtractWithBorrowOperation(AddressingMode.IndirectX));

        Register(0xF1, "SBC", AddressingMode.IndirectY, 2, 5,
            p => p.SubtractWithBorrowOperation(AddressingMode.IndirectY));

        #endregion

        #region Set Flag Instructions

        Register(0x38, "SEC", AddressingMode.Implied, 1, 2,
            p => { p.CarryFlag = true; p.IncrementCycleCount(); });

        Register(0xF8, "SED", AddressingMode.Implied, 1, 2,
            p => { p.DecimalFlag = true; p.IncrementCycleCount(); });

        Register(0x78, "SEI", AddressingMode.Implied, 1, 2,
            p => { p.DisableInterruptFlag = true; p.IncrementCycleCount(); });

        #endregion

        #region Store Accumulator (STA)

        Register(0x85, "STA", AddressingMode.ZeroPage, 2, 3,
            p => p.WriteMemoryValue(p.GetAddressByAddressingMode(AddressingMode.ZeroPage), (byte)p.Accumulator));

        Register(0x95, "STA", AddressingMode.ZeroPageX, 2, 4,
            p => p.WriteMemoryValue(p.GetAddressByAddressingMode(AddressingMode.ZeroPageX), (byte)p.Accumulator));

        Register(0x8D, "STA", AddressingMode.Absolute, 3, 4,
            p => p.WriteMemoryValue(p.GetAddressByAddressingMode(AddressingMode.Absolute), (byte)p.Accumulator));

        Register(0x9D, "STA", AddressingMode.AbsoluteX, 3, 5,
            p => { p.WriteMemoryValue(p.GetAddressByAddressingMode(AddressingMode.AbsoluteX), (byte)p.Accumulator); p.IncrementCycleCount(); });

        Register(0x99, "STA", AddressingMode.AbsoluteY, 3, 5,
            p => { p.WriteMemoryValue(p.GetAddressByAddressingMode(AddressingMode.AbsoluteY), (byte)p.Accumulator); p.IncrementCycleCount(); });

        Register(0x81, "STA", AddressingMode.IndirectX, 2, 6,
            p => p.WriteMemoryValue(p.GetAddressByAddressingMode(AddressingMode.IndirectX), (byte)p.Accumulator));

        Register(0x91, "STA", AddressingMode.IndirectY, 2, 6,
            p => { p.WriteMemoryValue(p.GetAddressByAddressingMode(AddressingMode.IndirectY), (byte)p.Accumulator); p.IncrementCycleCount(); });

        #endregion

        #region Store X Register (STX)

        Register(0x86, "STX", AddressingMode.ZeroPage, 2, 3,
            p => p.WriteMemoryValue(p.GetAddressByAddressingMode(AddressingMode.ZeroPage), (byte)p.XRegister));

        Register(0x96, "STX", AddressingMode.ZeroPageY, 2, 4,
            p => p.WriteMemoryValue(p.GetAddressByAddressingMode(AddressingMode.ZeroPageY), (byte)p.XRegister));

        Register(0x8E, "STX", AddressingMode.Absolute, 3, 4,
            p => p.WriteMemoryValue(p.GetAddressByAddressingMode(AddressingMode.Absolute), (byte)p.XRegister));

        #endregion

        #region Store Y Register (STY)

        Register(0x84, "STY", AddressingMode.ZeroPage, 2, 3,
            p => p.WriteMemoryValue(p.GetAddressByAddressingMode(AddressingMode.ZeroPage), (byte)p.YRegister));

        Register(0x94, "STY", AddressingMode.ZeroPageX, 2, 4,
            p => p.WriteMemoryValue(p.GetAddressByAddressingMode(AddressingMode.ZeroPageX), (byte)p.YRegister));

        Register(0x8C, "STY", AddressingMode.Absolute, 3, 4,
            p => p.WriteMemoryValue(p.GetAddressByAddressingMode(AddressingMode.Absolute), (byte)p.YRegister));

        #endregion

        #region Transfer Instructions

        Register(0xAA, "TAX", AddressingMode.Implied, 1, 2,
            p =>
            {
                p.IncrementCycleCount();
                p.XRegister = p.Accumulator;
                p.SetNegativeFlag(p.XRegister);
                p.SetZeroFlag(p.XRegister);
            });

        Register(0xA8, "TAY", AddressingMode.Implied, 1, 2,
            p =>
            {
                p.IncrementCycleCount();
                p.YRegister = p.Accumulator;
                p.SetNegativeFlag(p.YRegister);
                p.SetZeroFlag(p.YRegister);
            });

        Register(0xBA, "TSX", AddressingMode.Implied, 1, 2,
            p =>
            {
                p.XRegister = p.StackPointer;
                p.SetNegativeFlag(p.XRegister);
                p.SetZeroFlag(p.XRegister);
                p.IncrementCycleCount();
            });

        Register(0x8A, "TXA", AddressingMode.Implied, 1, 2,
            p =>
            {
                p.IncrementCycleCount();
                p.Accumulator = p.XRegister;
                p.SetNegativeFlag(p.Accumulator);
                p.SetZeroFlag(p.Accumulator);
            });

        Register(0x9A, "TXS", AddressingMode.Implied, 1, 2,
            p =>
            {
                p.StackPointer = (byte)p.XRegister;
                p.IncrementCycleCount();
            });

        Register(0x98, "TYA", AddressingMode.Implied, 1, 2,
            p =>
            {
                p.IncrementCycleCount();
                p.Accumulator = p.YRegister;
                p.SetNegativeFlag(p.Accumulator);
                p.SetZeroFlag(p.Accumulator);
            });

        #endregion
        }

        /// <summary>
        /// Helper method to register an opcode in the 6502 dictionary
        /// </summary>
        private static void Register(byte opcode, string mnemonic, AddressingMode mode, int bytes, int cycles, OpcodeHandler handler)
        {
            var opcodeInfo = new OpcodeInfo(opcode, mnemonic, mode, bytes, cycles, handler);
            _processorOpcodes[ProcessorType.MOS6502][opcode] = opcodeInfo;
            // Copy to 65C02 as well (it's a superset - we'll add 65C02-specific opcodes later)
            _processorOpcodes[ProcessorType.WDC65C02][opcode] = opcodeInfo;
        }

        /// <summary>
        /// Helper method to register a 65C02-specific opcode (not available on 6502)
        /// </summary>
        private static void Register65C02(byte opcode, string mnemonic, AddressingMode mode, int bytes, int cycles, OpcodeHandler handler)
        {
            var opcodeInfo = new OpcodeInfo(opcode, mnemonic, mode, bytes, cycles, handler);
            // Only add to 65C02, not to 6502
            _processorOpcodes[ProcessorType.WDC65C02][opcode] = opcodeInfo;
        }

        /// <summary>
        /// Registers 65C02-specific opcodes
        /// </summary>
        private static void Register65C02Opcodes()
        {
            #region 65C02 Stack Operations

            // PHX - Push X Register (65C02 only)
            Register65C02(0xDA, "PHX", AddressingMode.Implied, 1, 3,
                p => p.PushXOperation());

            // PLX - Pull X Register (65C02 only)
            Register65C02(0xFA, "PLX", AddressingMode.Implied, 1, 4,
                p => p.PullXOperation());

            // PHY - Push Y Register (65C02 only)
            Register65C02(0x5A, "PHY", AddressingMode.Implied, 1, 3,
                p => p.PushYOperation());

            // PLY - Pull Y Register (65C02 only)
            Register65C02(0x7A, "PLY", AddressingMode.Implied, 1, 4,
                p => p.PullYOperation());

            #endregion
        }
    }
}
