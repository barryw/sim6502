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
        public Processor() : this(ProcessorType.MOS6502)
        {
        }

        /// <summary>
        /// Constructor that creates a processor of a specific type.
        /// </summary>
        /// <param name="processorType">The processor variant to emulate</param>
        public Processor(ProcessorType processorType)
        {
            ProcessorType = processorType;
            Logger.Info($"{GetProcessorName()} Simulator Copyright © 2013 Aaron Mell. All Rights Reserved.");
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

            // Initialize 6510 I/O port registers
            if (ProcessorType == ProcessorType.MOS6510)
            {
                _6510DataDirection = 0x00;
                _6510DataPort = 0x00;
            }

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
            var opcodeInfo = OpcodeRegistry.GetOpcode((byte)CurrentOpCode, ProcessorType);

            if (opcodeInfo == null)
            {
                throw new NotSupportedException(
                    $"The OpCode {CurrentOpCode:X2} @ address {ProgramCounter:X4} is not supported on {ProcessorType}.");
            }

            opcodeInfo.Handler(this);
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