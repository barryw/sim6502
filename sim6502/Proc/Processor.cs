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

using NLog;
using System;
using System.ComponentModel;
using System.Globalization;

namespace sim6502.Proc
{
    /// <summary>
    /// An Implementation of a 6502 Processor
    /// </summary>
    [Serializable]
    public partial class Processor
    {
        #region Public Methods

        /// <summary>
        /// Default Constructor, Instantiates a new instance of the processor.
        /// </summary>
        public Processor()
        {
            Logger.Info("6502 Simulator Copyright © 2013 Aaron Mell. All Rights Reserved.");
            Logger.Info("https://github.com/aaronmell/6502Net");
            ResetMemory();
            StackPointer = 0x100;

            CycleCountIncrementedAction = () => { };
        }

        /// <summary>
        /// Initializes the processor to its default state.
        /// </summary>
        public void Reset()
        {
            Logger.Debug("Initializing 6502 simulator.");
            
            ResetCycleCount();

            CycleCount = 0;

            StackPointer = 0x1FD;

            // Set the Program Counter to the Reset Vector Address.
            ProgramCounter = 0xFFFC;
            // Reset the Program Counter to the Address contained in the Reset Vector
            ProgramCounter = Memory[ProgramCounter] | (Memory[ProgramCounter + 1] << 8);

            CurrentOpCode = Memory[ProgramCounter];

            DisableInterruptFlag = true;
            _previousInterrupt = false;
            TriggerNmi = false;
            TriggerIrq = false;
            
            Logger.Debug("6502 simulator initialized and reset.");
        }

        /// <summary>
        /// Is the current instruction a BRK instruction?
        /// </summary>
        /// <returns></returns>
        public bool IsBrk()
        {
            return Memory[ProgramCounter] == 0x00;
        }

        /// <summary>
        /// Is the current instruction an RTS instruction?
        /// </summary>
        /// <returns></returns>
        public bool IsRts()
        {
            return Memory[ProgramCounter] == 0x60;
        }

        /// <summary>
        /// Is the current instruction a JSR instruction?
        /// </summary>
        /// <returns></returns>
        public bool IsJsr()
        {
            return Memory[ProgramCounter] == 0x20;
        }

        /// <summary>
        /// Used for the 6502 sim tester CLI to execute and test routines
        /// </summary>
        /// <param name="address">The address of the routine to test.</param>
        /// <param name="stopOnAddress">If > 0, this is the address we should stop at</param>
        /// <param name="stopOnRts">True if we should stop on RTS. Won't stop on any RTS encountered during subroutines</param>
        /// <param name="failOnBrk">True if an encountered BRK instruction should fail the associated test.</param>
        /// <returns>True if we're exiting cleanly, False otherwise</returns>
        public bool RunRoutine(int address, int stopOnAddress, bool stopOnRts = true, bool failOnBrk = true)
        {
            var keepRunning = true;
            var subroutineCount = 1;
            var exitCleanly = true;
            ProgramCounter = address;

            do
            {
                if (IsJsr())
                    subroutineCount++;

                if (IsRts())
                {
                    subroutineCount--;

                    if (subroutineCount == 0 && stopOnRts)
                        keepRunning = false;
                }

                if (IsBrk())
                {
                    keepRunning = false;
                    if (failOnBrk)
                        exitCleanly = false;
                }

                if (ProgramCounter == stopOnAddress && stopOnAddress > 0)
                {
                    keepRunning = false;
                }
                
                NextStep();
            } while (keepRunning);

            return exitCleanly;
        }

        /// <summary>
        /// Performs the next step on the processor
        /// </summary>
        public void NextStep()
        {
            CurrentOpCode = Memory[ProgramCounter];

            SetDisassembly();

            //Have to read this first otherwise it causes tests to fail on a NES
            CurrentOpCode = ReadMemoryValue(ProgramCounter);

            ProgramCounter++;

            ExecuteOpCode();

            if (!_previousInterrupt) return;

            if (TriggerNmi)
            {
                ProcessNmi();
                TriggerNmi = false;
            }
            else if (TriggerIrq)
            {
                ProcessIrq();
                TriggerIrq = false;
            }
        }

        /// <summary>
        /// The InterruptRequest or IRQ
        /// </summary>
        public void InterruptRequest()
        {
            TriggerIrq = true;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Executes an Opcode
        /// </summary>
        private void ExecuteOpCode()
        {
            //The x+ cycles denotes that if a page wrap occurs, then an additional cycle is consumed.
            //The x++ cycles denotes that 1 cycle is added when a branch occurs and it on the same page, and two cycles are added if its on a different page./
            //This is handled inside the GetValueFromMemory Method
            switch (CurrentOpCode)
            {
                #region Add / Subtract Operations

                //ADC Add With Carry, Immediate, 2 Bytes, 2 Cycles
                case 0x69:
                {
                    AddWithCarryOperation(AddressingMode.Immediate);
                    break;
                }
                //ADC Add With Carry, Zero Page, 2 Bytes, 3 Cycles
                case 0x65:
                {
                    AddWithCarryOperation(AddressingMode.ZeroPage);
                    break;
                }
                //ADC Add With Carry, Zero Page X, 2 Bytes, 4 Cycles
                case 0x75:
                {
                    AddWithCarryOperation(AddressingMode.ZeroPageX);
                    break;
                }
                //ADC Add With Carry, Absolute, 3 Bytes, 4 Cycles
                case 0x6D:
                {
                    AddWithCarryOperation(AddressingMode.Absolute);
                    break;
                }
                //ADC Add With Carry, Absolute X, 3 Bytes, 4+ Cycles
                case 0x7D:
                {
                    AddWithCarryOperation(AddressingMode.AbsoluteX);
                    break;
                }
                //ADC Add With Carry, Absolute Y, 3 Bytes, 4+ Cycles
                case 0x79:
                {
                    AddWithCarryOperation(AddressingMode.AbsoluteY);
                    break;
                }
                //ADC Add With Carry, Indexed Indirect, 2 Bytes, 6 Cycles
                case 0x61:
                {
                    AddWithCarryOperation(AddressingMode.IndirectX);
                    break;
                }
                //ADC Add With Carry, Indexed Indirect, 2 Bytes, 5+ Cycles
                case 0x71:
                {
                    AddWithCarryOperation(AddressingMode.IndirectY);
                    break;
                }
                //SBC Subtract with Borrow, Immediate, 2 Bytes, 2 Cycles
                case 0xE9:
                {
                    SubtractWithBorrowOperation(AddressingMode.Immediate);
                    break;
                }
                //SBC Subtract with Borrow, Zero Page, 2 Bytes, 3 Cycles
                case 0xE5:
                {
                    SubtractWithBorrowOperation(AddressingMode.ZeroPage);
                    break;
                }
                //SBC Subtract with Borrow, Zero Page X, 2 Bytes, 4 Cycles
                case 0xF5:
                {
                    SubtractWithBorrowOperation(AddressingMode.ZeroPageX);
                    break;
                }
                //SBC Subtract with Borrow, Absolute, 3 Bytes, 4 Cycles
                case 0xED:
                {
                    SubtractWithBorrowOperation(AddressingMode.Absolute);
                    break;
                }
                //SBC Subtract with Borrow, Absolute X, 3 Bytes, 4+ Cycles
                case 0xFD:
                {
                    SubtractWithBorrowOperation(AddressingMode.AbsoluteX);
                    break;
                }
                //SBC Subtract with Borrow, Absolute Y, 3 Bytes, 4+ Cycles
                case 0xF9:
                {
                    SubtractWithBorrowOperation(AddressingMode.AbsoluteY);
                    break;
                }
                //SBC Subtract with Borrow, Indexed Indirect, 2 Bytes, 6 Cycles
                case 0xE1:
                {
                    SubtractWithBorrowOperation(AddressingMode.IndirectX);
                    break;
                }
                //SBC Subtract with Borrow, Indexed Indirect, 2 Bytes, 5+ Cycles
                case 0xF1:
                {
                    SubtractWithBorrowOperation(AddressingMode.IndirectY);
                    break;
                }

                #endregion

                #region Branch Operations

                //BCC Branch if Carry is Clear, Relative, 2 Bytes, 2++ Cycles
                case 0x90:
                {
                    BranchOperation(!CarryFlag);
                    break;
                }
                //BCS Branch if Carry is Set, Relative, 2 Bytes, 2++ Cycles
                case 0xB0:
                {
                    BranchOperation(CarryFlag);
                    break;
                }
                //BEQ Branch if Zero is Set, Relative, 2 Bytes, 2++ Cycles
                case 0xF0:
                {
                    BranchOperation(ZeroFlag);
                    break;
                }

                // BMI Branch if Negative Set
                case 0x30:
                {
                    BranchOperation(NegativeFlag);
                    break;
                }
                //BNE Branch if Zero is Not Set, Relative, 2 Bytes, 2++ Cycles
                case 0xD0:
                {
                    BranchOperation(!ZeroFlag);
                    break;
                }
                // BPL Branch if Negative Clear, 2 Bytes, 2++ Cycles
                case 0x10:
                {
                    BranchOperation(!NegativeFlag);
                    break;
                }
                // BVC Branch if Overflow Clear, 2 Bytes, 2++ Cycles
                case 0x50:
                {
                    BranchOperation(!OverflowFlag);
                    break;
                }
                // BVS Branch if Overflow Set, 2 Bytes, 2++ Cycles
                case 0x70:
                {
                    BranchOperation(OverflowFlag);
                    break;
                }

                #endregion

                #region BitWise Comparison Operations

                //AND Compare Memory with Accumulator, Immediate, 2 Bytes, 2 Cycles
                case 0x29:
                {
                    AndOperation(AddressingMode.Immediate);
                    break;
                }
                //AND Compare Memory with Accumulator, Zero Page, 2 Bytes, 3 Cycles
                case 0x25:
                {
                    AndOperation(AddressingMode.ZeroPage);
                    break;
                }
                //AND Compare Memory with Accumulator, Zero PageX, 2 Bytes, 3 Cycles
                case 0x35:
                {
                    AndOperation(AddressingMode.ZeroPageX);
                    break;
                }
                //AND Compare Memory with Accumulator, Absolute,  3 Bytes, 4 Cycles
                case 0x2D:
                {
                    AndOperation(AddressingMode.Absolute);
                    break;
                }
                //AND Compare Memory with Accumulator, AbsolueteX 3 Bytes, 4+ Cycles
                case 0x3D:
                {
                    AndOperation(AddressingMode.AbsoluteX);
                    break;
                }
                //AND Compare Memory with Accumulator, AbsoluteY, 3 Bytes, 4+ Cycles
                case 0x39:
                {
                    AndOperation(AddressingMode.AbsoluteY);
                    break;
                }
                //AND Compare Memory with Accumulator, IndexedIndirect, 2 Bytes, 6 Cycles
                case 0x21:
                {
                    AndOperation(AddressingMode.IndirectX);
                    break;
                }
                //AND Compare Memory with Accumulator, IndirectIndexed, 2 Bytes, 5 Cycles
                case 0x31:
                {
                    AndOperation(AddressingMode.IndirectY);
                    break;
                }
                //BIT Compare Memory with Accumulator, Zero Page, 2 Bytes, 3 Cycles
                case 0x24:
                {
                    BitOperation(AddressingMode.ZeroPage);
                    break;
                }
                //BIT Compare Memory with Accumulator, Absolute, 2 Bytes, 4 Cycles
                case 0x2C:
                {
                    BitOperation(AddressingMode.Absolute);
                    break;
                }
                //EOR Exclusive OR Memory with Accumulator, Immediate, 2 Bytes, 2 Cycles
                case 0x49:
                {
                    EorOperation(AddressingMode.Immediate);
                    break;
                }
                //EOR Exclusive OR Memory with Accumulator, Zero Page, 2 Bytes, 3 Cycles
                case 0x45:
                {
                    EorOperation(AddressingMode.ZeroPage);
                    break;
                }
                //EOR Exclusive OR Memory with Accumulator, Zero Page X, 2 Bytes, 4 Cycles
                case 0x55:
                {
                    EorOperation(AddressingMode.ZeroPageX);
                    break;
                }
                //EOR Exclusive OR Memory with Accumulator, Absolute, 3 Bytes, 4 Cycles
                case 0x4D:
                {
                    EorOperation(AddressingMode.Absolute);
                    break;
                }
                //EOR Exclusive OR Memory with Accumulator, Absolute X, 3 Bytes, 4+ Cycles
                case 0x5D:
                {
                    EorOperation(AddressingMode.AbsoluteX);
                    break;
                }
                //EOR Exclusive OR Memory with Accumulator, Absolute Y, 3 Bytes, 4+ Cycles
                case 0x59:
                {
                    EorOperation(AddressingMode.AbsoluteY);
                    break;
                }
                //EOR Exclusive OR Memory with Accumulator, IndexedIndirect, 2 Bytes 6 Cycles
                case 0x41:
                {
                    EorOperation(AddressingMode.IndirectX);
                    break;
                }
                //EOR Exclusive OR Memory with Accumulator, IndirectIndexed, 2 Bytes 5 Cycles
                case 0x51:
                {
                    EorOperation(AddressingMode.IndirectY);
                    break;
                }
                //ORA Compare Memory with Accumulator, Immediate, 2 Bytes, 2 Cycles
                case 0x09:
                {
                    OrOperation(AddressingMode.Immediate);
                    break;
                }
                //ORA Compare Memory with Accumulator, Zero Page, 2 Bytes, 2 Cycles
                case 0x05:
                {
                    OrOperation(AddressingMode.ZeroPage);
                    break;
                }
                //ORA Compare Memory with Accumulator, Zero PageX, 2 Bytes, 4 Cycles
                case 0x15:
                {
                    OrOperation(AddressingMode.ZeroPageX);
                    break;
                }
                //ORA Compare Memory with Accumulator, Absolute,  3 Bytes, 4 Cycles
                case 0x0D:
                {
                    OrOperation(AddressingMode.Absolute);
                    break;
                }
                //ORA Compare Memory with Accumulator, AbsolueteX 3 Bytes, 4+ Cycles
                case 0x1D:
                {
                    OrOperation(AddressingMode.AbsoluteX);
                    break;
                }
                //ORA Compare Memory with Accumulator, AbsoluteY, 3 Bytes, 4+ Cycles
                case 0x19:
                {
                    OrOperation(AddressingMode.AbsoluteY);
                    break;
                }
                //ORA Compare Memory with Accumulator, IndexedIndirect, 2 Bytes, 6 Cycles
                case 0x01:
                {
                    OrOperation(AddressingMode.IndirectX);
                    break;
                }
                //ORA Compare Memory with Accumulator, IndirectIndexed, 2 Bytes, 5 Cycles
                case 0x11:
                {
                    OrOperation(AddressingMode.IndirectY);
                    break;
                }

                #endregion

                #region Clear Flag Operations

                //CLC Clear Carry Flag, Implied, 1 Byte, 2 Cycles
                case 0x18:
                {
                    CarryFlag = false;
                    IncrementCycleCount();
                    break;
                }
                //CLD Clear Decimal Flag, Implied, 1 Byte, 2 Cycles
                case 0xD8:
                {
                    DecimalFlag = false;
                    IncrementCycleCount();
                    break;
                }
                //CLI Clear Interrupt Flag, Implied, 1 Byte, 2 Cycles
                case 0x58:
                {
                    DisableInterruptFlag = false;
                    IncrementCycleCount();
                    break;
                }
                //CLV Clear Overflow Flag, Implied, 1 Byte, 2 Cycles
                case 0xB8:
                {
                    OverflowFlag = false;
                    IncrementCycleCount();
                    break;
                }

                #endregion

                #region Compare Operations

                //CMP Compare Accumulator with Memory, Immediate, 2 Bytes, 2 Cycles
                case 0xC9:
                {
                    CompareOperation(AddressingMode.Immediate, Accumulator);
                    break;
                }
                //CMP Compare Accumulator with Memory, Zero Page, 2 Bytes, 3 Cycles
                case 0xC5:
                {
                    CompareOperation(AddressingMode.ZeroPage, Accumulator);
                    break;
                }
                //CMP Compare Accumulator with Memory, Zero Page x, 2 Bytes, 4 Cycles
                case 0xD5:
                {
                    CompareOperation(AddressingMode.ZeroPageX, Accumulator);
                    break;
                }
                //CMP Compare Accumulator with Memory, Absolute, 3 Bytes, 4 Cycles
                case 0xCD:
                {
                    CompareOperation(AddressingMode.Absolute, Accumulator);
                    break;
                }
                //CMP Compare Accumulator with Memory, Absolute X, 2 Bytes, 4 Cycles
                case 0xDD:
                {
                    CompareOperation(AddressingMode.AbsoluteX, Accumulator);
                    break;
                }
                //CMP Compare Accumulator with Memory, Absolute Y, 2 Bytes, 4 Cycles
                case 0xD9:
                {
                    CompareOperation(AddressingMode.AbsoluteY, Accumulator);
                    break;
                }
                //CMP Compare Accumulator with Memory, Indirect X, 2 Bytes, 6 Cycles
                case 0xC1:
                {
                    CompareOperation(AddressingMode.IndirectX, Accumulator);
                    break;
                }
                //CMP Compare Accumulator with Memory, Indirect Y, 2 Bytes, 5 Cycles
                case 0xD1:
                {
                    CompareOperation(AddressingMode.IndirectY, Accumulator);
                    break;
                }
                //CPX Compare Accumulator with X Register, Immediate, 2 Bytes, 2 Cycles
                case 0xE0:
                {
                    CompareOperation(AddressingMode.Immediate, XRegister);
                    break;
                }
                //CPX Compare Accumulator with X Register, Zero Page, 2 Bytes, 3 Cycles
                case 0xE4:
                {
                    CompareOperation(AddressingMode.ZeroPage, XRegister);
                    break;
                }
                //CPX Compare Accumulator with X Register, Absolute, 3 Bytes, 4 Cycles
                case 0xEC:
                {
                    CompareOperation(AddressingMode.Absolute, XRegister);
                    break;
                }
                //CPY Compare Accumulator with Y Register, Immediate, 2 Bytes, 2 Cycles
                case 0xC0:
                {
                    CompareOperation(AddressingMode.Immediate, YRegister);
                    break;
                }
                //CPY Compare Accumulator with Y Register, Zero Page, 2 Bytes, 3 Cycles
                case 0xC4:
                {
                    CompareOperation(AddressingMode.ZeroPage, YRegister);
                    break;
                }
                //CPY Compare Accumulator with Y Register, Absolute, 3 Bytes, 4 Cycles
                case 0xCC:
                {
                    CompareOperation(AddressingMode.Absolute, YRegister);
                    break;
                }

                #endregion

                #region Increment/Decrement Operations

                //DEC Decrement Memory by One, Zero Page, 2 Bytes, 5 Cycles
                case 0xC6:
                {
                    ChangeMemoryByOne(AddressingMode.ZeroPage, true);
                    break;
                }
                //DEC Decrement Memory by One, Zero Page X, 2 Bytes, 6 Cycles
                case 0xD6:
                {
                    ChangeMemoryByOne(AddressingMode.ZeroPageX, true);
                    break;
                }
                //DEC Decrement Memory by One, Absolute, 3 Bytes, 6 Cycles
                case 0xCE:
                {
                    ChangeMemoryByOne(AddressingMode.Absolute, true);
                    break;
                }
                //DEC Decrement Memory by One, Absolute X, 3 Bytes, 7 Cycles
                case 0xDE:
                {
                    ChangeMemoryByOne(AddressingMode.AbsoluteX, true);
                    IncrementCycleCount();
                    break;
                }
                //DEX Decrement X Register by One, Implied, 1 Bytes, 2 Cycles
                case 0xCA:
                {
                    ChangeRegisterByOne(true, true);
                    break;
                }
                //DEY Decrement Y Register by One, Implied, 1 Bytes, 2 Cycles
                case 0x88:
                {
                    ChangeRegisterByOne(false, true);
                    break;
                }
                //INC Increment Memory by One, Zero Page, 2 Bytes, 5 Cycles
                case 0xE6:
                {
                    ChangeMemoryByOne(AddressingMode.ZeroPage, false);
                    break;
                }
                //INC Increment Memory by One, Zero Page X, 2 Bytes, 6 Cycles
                case 0xF6:
                {
                    ChangeMemoryByOne(AddressingMode.ZeroPageX, false);
                    break;
                }
                //INC Increment Memory by One, Absolute, 3 Bytes, 6 Cycles
                case 0xEE:
                {
                    ChangeMemoryByOne(AddressingMode.Absolute, false);
                    break;
                }
                //INC Increment Memory by One, Absolute X, 3 Bytes, 7 Cycles
                case 0xFE:
                {
                    ChangeMemoryByOne(AddressingMode.AbsoluteX, false);
                    IncrementCycleCount();
                    break;
                }
                //INX Increment X Register by One, Implied, 1 Bytes, 2 Cycles
                case 0xE8:
                {
                    ChangeRegisterByOne(true, false);
                    break;
                }
                //INY Increment Y Register by One, Implied, 1 Bytes, 2 Cycles
                case 0xC8:
                {
                    ChangeRegisterByOne(false, false);
                    break;
                }

                #endregion

                #region GOTO and GOSUB Operations

                //JMP Jump to New Location, Absolute 3 Bytes, 3 Cycles
                case 0x4C:
                {
                    ProgramCounter = GetAddressByAddressingMode(AddressingMode.Absolute);
                    break;
                }
                //JMP Jump to New Location, Indirect 3 Bytes, 5 Cycles
                case 0x6C:
                {
                    ProgramCounter = GetAddressByAddressingMode(AddressingMode.Absolute);

                    if ((ProgramCounter & 0xFF) == 0xFF)
                    {
                        //Get the first half of the address
                        int address = ReadMemoryValue(ProgramCounter);

                        //Get the second half of the address, due to the issue with page boundary it reads from the wrong location!
                        address += 256 * ReadMemoryValue(ProgramCounter - 255);
                        ProgramCounter = address;
                    }
                    else
                    {
                        ProgramCounter = GetAddressByAddressingMode(AddressingMode.Absolute);
                    }

                    break;
                }
                //JSR Jump to SubRoutine, Absolute, 3 Bytes, 6 Cycles
                case 0x20:
                {
                    JumpToSubRoutineOperation();
                    break;
                }
                //BRK Simulate IRQ, Implied, 1 Byte, 7 Cycles
                case 0x00:
                {
                    BreakOperation(true, 0xFFFE);
                    break;
                }
                //RTI Return From Interrupt, Implied, 1 Byte, 6 Cycles
                case 0x40:
                {
                    ReturnFromInterruptOperation();
                    break;
                }
                //RTS Return From Subroutine, Implied, 1 Byte, 6 Cycles
                case 0x60:
                {
                    ReturnFromSubRoutineOperation();
                    break;
                }

                #endregion

                #region Load Value From Memory Operations

                //LDA Load Accumulator with Memory, Immediate, 2 Bytes, 2 Cycles
                case 0xA9:
                {
                    Accumulator = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.Immediate));
                    SetZeroFlag(Accumulator);
                    SetNegativeFlag(Accumulator);
                    break;
                }
                //LDA Load Accumulator with Memory, Zero Page, 2 Bytes, 3 Cycles
                case 0xA5:
                {
                    Accumulator = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.ZeroPage));
                    SetZeroFlag(Accumulator);
                    SetNegativeFlag(Accumulator);
                    break;
                }
                //LDA Load Accumulator with Memory, Zero Page X, 2 Bytes, 4 Cycles
                case 0xB5:
                {
                    Accumulator = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.ZeroPageX));
                    SetZeroFlag(Accumulator);
                    SetNegativeFlag(Accumulator);
                    break;
                }
                //LDA Load Accumulator with Memory, Absolute, 3 Bytes, 4 Cycles
                case 0xAD:
                {
                    Accumulator = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.Absolute));
                    SetZeroFlag(Accumulator);
                    SetNegativeFlag(Accumulator);
                    break;
                }
                //LDA Load Accumulator with Memory, Absolute X, 3 Bytes, 4+ Cycles
                case 0xBD:
                {
                    Accumulator = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.AbsoluteX));
                    SetZeroFlag(Accumulator);
                    SetNegativeFlag(Accumulator);
                    break;
                }
                //LDA Load Accumulator with Memory, Absolute Y, 3 Bytes, 4+ Cycles
                case 0xB9:
                {
                    Accumulator = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.AbsoluteY));
                    SetZeroFlag(Accumulator);
                    SetNegativeFlag(Accumulator);
                    break;
                }
                //LDA Load Accumulator with Memory, Index Indirect, 2 Bytes, 6 Cycles
                case 0xA1:
                {
                    Accumulator = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.IndirectX));
                    SetZeroFlag(Accumulator);
                    SetNegativeFlag(Accumulator);
                    break;
                }
                //LDA Load Accumulator with Memory, Indirect Index, 2 Bytes, 5+ Cycles
                case 0xB1:
                {
                    Accumulator = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.IndirectY));
                    SetZeroFlag(Accumulator);
                    SetNegativeFlag(Accumulator);
                    break;
                }
                //LDX Load X with memory, Immediate, 2 Bytes, 2 Cycles
                case 0xA2:
                {
                    XRegister = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.Immediate));
                    SetZeroFlag(XRegister);
                    SetNegativeFlag(XRegister);
                    break;
                }
                //LDX Load X with memory, Zero Page, 2 Bytes, 3 Cycles
                case 0xA6:
                {
                    XRegister = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.ZeroPage));
                    SetZeroFlag(XRegister);
                    SetNegativeFlag(XRegister);
                    break;
                }
                //LDX Load X with memory, Zero Page Y, 2 Bytes, 4 Cycles
                case 0xB6:
                {
                    XRegister = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.ZeroPageY));
                    SetZeroFlag(XRegister);
                    SetNegativeFlag(XRegister);
                    break;
                }
                //LDX Load X with memory, Absolute, 3 Bytes, 4 Cycles
                case 0xAE:
                {
                    XRegister = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.Absolute));
                    SetZeroFlag(XRegister);
                    SetNegativeFlag(XRegister);
                    break;
                }
                //LDX Load X with memory, Absolute Y, 3 Bytes, 4+ Cycles
                case 0xBE:
                {
                    XRegister = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.AbsoluteY));
                    SetZeroFlag(XRegister);
                    SetNegativeFlag(XRegister);
                    break;
                }
                //LDY Load Y with memory, Immediate, 2 Bytes, 2 Cycles
                case 0xA0:
                {
                    YRegister = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.Immediate));
                    SetZeroFlag(YRegister);
                    SetNegativeFlag(YRegister);
                    break;
                }
                //LDY Load Y with memory, Zero Page, 2 Bytes, 3 Cycles
                case 0xA4:
                {
                    YRegister = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.ZeroPage));
                    SetZeroFlag(YRegister);
                    SetNegativeFlag(YRegister);
                    break;
                }
                //LDY Load Y with memory, Zero Page X, 2 Bytes, 4 Cycles
                case 0xB4:
                {
                    YRegister = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.ZeroPageX));
                    SetZeroFlag(YRegister);
                    SetNegativeFlag(YRegister);
                    break;
                }
                //LDY Load Y with memory, Absolute, 3 Bytes, 4 Cycles
                case 0xAC:
                {
                    YRegister = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.Absolute));
                    SetZeroFlag(YRegister);
                    SetNegativeFlag(YRegister);
                    break;
                }
                //LDY Load Y with memory, Absolute X, 3 Bytes, 4+ Cycles
                case 0xBC:
                {
                    YRegister = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.AbsoluteX));
                    SetZeroFlag(YRegister);
                    SetNegativeFlag(YRegister);
                    break;
                }

                #endregion

                #region Push/Pull Stack

                //PHA Push Accumulator onto Stack, Implied, 1 Byte, 3 Cycles
                case 0x48:
                {
                    ReadMemoryValue(ProgramCounter + 1);

                    PokeStack((byte) Accumulator);
                    StackPointer--;
                    IncrementCycleCount();
                    break;
                }
                //PHP Push Flags onto Stack, Implied, 1 Byte, 3 Cycles
                case 0x08:
                {
                    ReadMemoryValue(ProgramCounter + 1);

                    PushFlagsOperation();
                    StackPointer--;
                    IncrementCycleCount();
                    break;
                }
                //PLA Pull Accumulator from Stack, Implied, 1 Byte, 4 Cycles
                case 0x68:
                {
                    ReadMemoryValue(ProgramCounter + 1);
                    StackPointer++;
                    IncrementCycleCount();

                    Accumulator = PeekStack();
                    SetNegativeFlag(Accumulator);
                    SetZeroFlag(Accumulator);

                    IncrementCycleCount();
                    break;
                }
                //PLP Pull Flags from Stack, Implied, 1 Byte, 4 Cycles
                case 0x28:
                {
                    ReadMemoryValue(ProgramCounter + 1);

                    StackPointer++;
                    IncrementCycleCount();

                    PullFlagsOperation();

                    IncrementCycleCount();
                    break;
                }
                //TSX Transfer Stack Pointer to X Register, 1 Bytes, 2 Cycles
                case 0xBA:
                {
                    XRegister = StackPointer;

                    SetNegativeFlag(XRegister);
                    SetZeroFlag(XRegister);
                    IncrementCycleCount();
                    break;
                }
                //TXS Transfer X Register to Stack Pointer, 1 Bytes, 2 Cycles
                case 0x9A:
                {
                    StackPointer = (byte) XRegister;
                    IncrementCycleCount();
                    break;
                }

                #endregion

                #region Set Flag Operations

                //SEC Set Carry, Implied, 1 Bytes, 2 Cycles
                case 0x38:
                {
                    CarryFlag = true;
                    IncrementCycleCount();
                    break;
                }
                //SED Set Interrupt, Implied, 1 Bytes, 2 Cycles
                case 0xF8:
                {
                    DecimalFlag = true;
                    IncrementCycleCount();
                    break;
                }
                //SEI Set Interrupt, Implied, 1 Bytes, 2 Cycles
                case 0x78:
                {
                    DisableInterruptFlag = true;
                    IncrementCycleCount();
                    break;
                }

                #endregion

                #region Shift/Rotate Operations

                //ASL Shift Left 1 Bit Memory or Accumulator, Accumulator, 1 Bytes, 2 Cycles
                case 0x0A:
                {
                    AslOperation(AddressingMode.Accumulator);
                    break;
                }
                //ASL Shift Left 1 Bit Memory or Accumulator, Zero Page, 2 Bytes, 5 Cycles
                case 0x06:
                {
                    AslOperation(AddressingMode.ZeroPage);
                    break;
                }
                //ASL Shift Left 1 Bit Memory or Accumulator, Zero PageX, 2 Bytes, 6 Cycles
                case 0x16:
                {
                    AslOperation(AddressingMode.ZeroPageX);
                    break;
                }
                //ASL Shift Left 1 Bit Memory or Accumulator, Absolute, 3 Bytes, 6 Cycles
                case 0x0E:
                {
                    AslOperation(AddressingMode.Absolute);
                    break;
                }
                //ASL Shift Left 1 Bit Memory or Accumulator, AbsoluteX, 3 Bytes, 7 Cycles
                case 0x1E:
                {
                    AslOperation(AddressingMode.AbsoluteX);
                    IncrementCycleCount();
                    break;
                }
                //LSR Shift Left 1 Bit Memory or Accumulator, Accumulator, 1 Bytes, 2 Cycles
                case 0x4A:
                {
                    LsrOperation(AddressingMode.Accumulator);
                    break;
                }
                //LSR Shift Left 1 Bit Memory or Accumulator, Zero Page, 2 Bytes, 5 Cycles
                case 0x46:
                {
                    LsrOperation(AddressingMode.ZeroPage);
                    break;
                }
                //LSR Shift Left 1 Bit Memory or Accumulator, Zero PageX, 2 Bytes, 6 Cycles
                case 0x56:
                {
                    LsrOperation(AddressingMode.ZeroPageX);
                    break;
                }
                //LSR Shift Left 1 Bit Memory or Accumulator, Absolute, 3 Bytes, 6 Cycles
                case 0x4E:
                {
                    LsrOperation(AddressingMode.Absolute);
                    break;
                }
                //LSR Shift Left 1 Bit Memory or Accumulator, AbsoluteX, 3 Bytes, 7 Cycles
                case 0x5E:
                {
                    LsrOperation(AddressingMode.AbsoluteX);
                    IncrementCycleCount();
                    break;
                }
                //ROL Rotate Left 1 Bit Memory or Accumulator, Accumulator, 1 Bytes, 2 Cycles
                case 0x2A:
                {
                    RolOperation(AddressingMode.Accumulator);
                    break;
                }
                //ROL Rotate Left 1 Bit Memory or Accumulator, Zero Page, 2 Bytes, 5 Cycles
                case 0x26:
                {
                    RolOperation(AddressingMode.ZeroPage);
                    break;
                }
                //ROL Rotate Left 1 Bit Memory or Accumulator, Zero PageX, 2 Bytes, 6 Cycles
                case 0x36:
                {
                    RolOperation(AddressingMode.ZeroPageX);
                    break;
                }
                //ROL Rotate Left 1 Bit Memory or Accumulator, Absolute, 3 Bytes, 6 Cycles
                case 0x2E:
                {
                    RolOperation(AddressingMode.Absolute);
                    break;
                }
                //ROL Rotate Left 1 Bit Memory or Accumulator, AbsoluteX, 3 Bytes, 7 Cycles
                case 0x3E:
                {
                    RolOperation(AddressingMode.AbsoluteX);
                    IncrementCycleCount();
                    break;
                }
                //ROR Rotate Right 1 Bit Memory or Accumulator, Accumulator, 1 Bytes, 2 Cycles
                case 0x6A:
                {
                    RorOperation(AddressingMode.Accumulator);
                    break;
                }
                //ROR Rotate Right 1 Bit Memory or Accumulator, Zero Page, 2 Bytes, 5 Cycles
                case 0x66:
                {
                    RorOperation(AddressingMode.ZeroPage);
                    break;
                }
                //ROR Rotate Right 1 Bit Memory or Accumulator, Zero PageX, 2 Bytes, 6 Cycles
                case 0x76:
                {
                    RorOperation(AddressingMode.ZeroPageX);
                    break;
                }
                //ROR Rotate Right 1 Bit Memory or Accumulator, Absolute, 3 Bytes, 6 Cycles
                case 0x6E:
                {
                    RorOperation(AddressingMode.Absolute);
                    break;
                }
                //ROR Rotate Right 1 Bit Memory or Accumulator, AbsoluteX, 3 Bytes, 7 Cycles
                case 0x7E:
                {
                    RorOperation(AddressingMode.AbsoluteX);
                    IncrementCycleCount();
                    break;
                }

                #endregion

                #region Store Value In Memory Operations

                //STA Store Accumulator In Memory, Zero Page, 2 Bytes, 3 Cycles
                case 0x85:
                {
                    WriteMemoryValue(GetAddressByAddressingMode(AddressingMode.ZeroPage), (byte) Accumulator);
                    break;
                }
                //STA Store Accumulator In Memory, Zero Page X, 2 Bytes, 4 Cycles
                case 0x95:
                {
                    WriteMemoryValue(GetAddressByAddressingMode(AddressingMode.ZeroPageX), (byte) Accumulator);
                    break;
                }
                //STA Store Accumulator In Memory, Absolute, 3 Bytes, 4 Cycles
                case 0x8D:
                {
                    WriteMemoryValue(GetAddressByAddressingMode(AddressingMode.Absolute), (byte) Accumulator);
                    break;
                }
                //STA Store Accumulator In Memory, Absolute X, 3 Bytes, 5 Cycles
                case 0x9D:
                {
                    WriteMemoryValue(GetAddressByAddressingMode(AddressingMode.AbsoluteX), (byte) Accumulator);
                    IncrementCycleCount();
                    break;
                }
                //STA Store Accumulator In Memory, Absolute Y, 3 Bytes, 5 Cycles
                case 0x99:
                {
                    WriteMemoryValue(GetAddressByAddressingMode(AddressingMode.AbsoluteY), (byte) Accumulator);
                    IncrementCycleCount();
                    break;
                }
                //STA Store Accumulator In Memory, Indexed Indirect, 2 Bytes, 6 Cycles
                case 0x81:
                {
                    WriteMemoryValue(GetAddressByAddressingMode(AddressingMode.IndirectX), (byte) Accumulator);
                    break;
                }
                //STA Store Accumulator In Memory, Indirect Indexed, 2 Bytes, 6 Cycles
                case 0x91:
                {
                    WriteMemoryValue(GetAddressByAddressingMode(AddressingMode.IndirectY), (byte) Accumulator);
                    IncrementCycleCount();
                    break;
                }
                //STX Store Index X, Zero Page, 2 Bytes, 3 Cycles
                case 0x86:
                {
                    WriteMemoryValue(GetAddressByAddressingMode(AddressingMode.ZeroPage), (byte) XRegister);
                    break;
                }
                //STX Store Index X, Zero Page Y, 2 Bytes, 4 Cycles
                case 0x96:
                {
                    WriteMemoryValue(GetAddressByAddressingMode(AddressingMode.ZeroPageY), (byte) XRegister);
                    break;
                }
                //STX Store Index X, Absolute, 3 Bytes, 4 Cycles
                case 0x8E:
                {
                    WriteMemoryValue(GetAddressByAddressingMode(AddressingMode.Absolute), (byte) XRegister);
                    break;
                }
                //STY Store Index Y, Zero Page, 2 Bytes, 3 Cycles
                case 0x84:
                {
                    WriteMemoryValue(GetAddressByAddressingMode(AddressingMode.ZeroPage), (byte) YRegister);
                    break;
                }
                //STY Store Index Y, Zero Page X, 2 Bytes, 4 Cycles
                case 0x94:
                {
                    WriteMemoryValue(GetAddressByAddressingMode(AddressingMode.ZeroPageX), (byte) YRegister);
                    break;
                }
                //STY Store Index Y, Absolute, 2 Bytes, 4 Cycles
                case 0x8C:
                {
                    WriteMemoryValue(GetAddressByAddressingMode(AddressingMode.Absolute), (byte) YRegister);
                    break;
                }

                #endregion

                #region Transfer Operations

                //TAX Transfer Accumulator to X Register, Implied, 1 Bytes, 2 Cycles
                case 0xAA:
                {
                    IncrementCycleCount();
                    XRegister = Accumulator;

                    SetNegativeFlag(XRegister);
                    SetZeroFlag(XRegister);
                    break;
                }
                //TAY Transfer Accumulator to Y Register, 1 Bytes, 2 Cycles
                case 0xA8:
                {
                    IncrementCycleCount();
                    YRegister = Accumulator;

                    SetNegativeFlag(YRegister);
                    SetZeroFlag(YRegister);
                    break;
                }
                //TXA Transfer X Register to Accumulator, Implied, 1 Bytes, 2 Cycles
                case 0x8A:
                {
                    IncrementCycleCount();
                    Accumulator = XRegister;

                    SetNegativeFlag(Accumulator);
                    SetZeroFlag(Accumulator);
                    break;
                }
                //TYA Transfer Y Register to Accumulator, Implied, 1 Bytes, 2 Cycles
                case 0x98:
                {
                    IncrementCycleCount();
                    Accumulator = YRegister;

                    SetNegativeFlag(Accumulator);
                    SetZeroFlag(Accumulator);
                    break;
                }

                #endregion

                //NOP Operation, Implied, 1 Byte, 2 Cycles
                case 0xEA:
                {
                    IncrementCycleCount();
                    break;
                }

                default:
                    throw new NotSupportedException($"The OpCode {CurrentOpCode.ToString()} @ address {ProgramCounter.ToString()} is not supported.");
            }
        }

        /// <summary>
        /// Uses the AddressingMode to return the correct address based on the mode.
        /// Note: This method will not increment the program counter for any mode.
        /// Note: This method will return an error if called for either the immediate or accumulator modes.
        /// </summary>
        /// <param name="addressingMode">The addressing Mode to use</param>
        /// <returns>The memory Location</returns>

        /// <summary>
        /// Moves the ProgramCounter in a given direction based on the value inputted
        /// </summary>
        private void MoveProgramCounterByRelativeValue(byte valueToMove)
        {
            var movement = valueToMove > 127 ? valueToMove - 255 : valueToMove;

            var newProgramCounter = ProgramCounter + movement;

            //This makes sure that we always land on the correct spot for a positive number
            if (movement >= 0)
                newProgramCounter++;

            //We Crossed a Page Boundary. So we increment the cycle counter by one. The +1 is because we always check from the end of the instruction not the beginning
            if ((((ProgramCounter + 1) ^ newProgramCounter) & 0xff00) != 0x0000) IncrementCycleCount();

            ProgramCounter = newProgramCounter;
            ReadMemoryValue(ProgramCounter);
        }

        /// <summary>
        /// Returns a the value from the stack without changing the position of the stack pointer
        /// </summary>
        /// <returns>The value at the current Stack Pointer</returns>
        private byte PeekStack()
        {
            //The stack lives at 0x100-0x1FF, but the value is only a byte so it needs to be translated
            return Memory[StackPointer + 0x100];
        }

        /// <summary>
        /// Write a value directly to the stack without modifying the Stack Pointer
        /// </summary>
        /// <param name="value">The value to be written to the stack</param>
        private void PokeStack(byte value)
        {
            //The stack lives at 0x100-0x1FF, but the value is only a byte so it needs to be translated
            Memory[StackPointer + 0x100] = value;
        }

        #endregion
    }
}