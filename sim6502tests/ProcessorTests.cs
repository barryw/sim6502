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
using FluentAssertions;
using sim6502;
using sim6502.Proc;
using Xunit;

namespace sim6502tests
{
    public class ProcessorTests
    {
        #region Initialization Tests

        [Fact]
        public void Processor_Status_Flags_Initialized_Correctly()
        {
            var processor = new Processor();
            processor.CarryFlag.Should().BeFalse();
            processor.ZeroFlag.Should().BeFalse();
            processor.DisableInterruptFlag.Should().BeFalse();
            processor.DecimalFlag.Should().BeFalse();
            processor.OverflowFlag.Should().BeFalse();
            processor.NegativeFlag.Should().BeFalse();
        }

        [Fact]
        public void Processor_Registers_Initialized_Correctly()
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0);
            processor.XRegister.Should().Be(0);
            processor.YRegister.Should().Be(0);
            processor.CurrentOpCode.Should().Be(0);
            processor.ProgramCounter.Should().Be(0);
        }

        [Fact]
        public void ProgramCounter_Correct_When_Program_Loaded()
        {
            var processor = new Processor();
            processor.LoadProgram(0, new byte[1], 0x01);
            processor.ProgramCounter.Should().Be(0x01);
        }

        [Fact]
        public void Throws_Exception_When_OpCode_Is_Invalid()
        {
            var processor = new Processor();
            processor.LoadProgram(0x00, new byte[] {0xFF}, 0x00);
            FluentActions.Invoking(() => processor.NextStep()).Should().Throw<NotSupportedException>();
        }

        [Fact]
        public void Stack_Pointer_Initializes_To_Default_Value_After_Reset()
        {
            var processor = new Processor();
            processor.Reset();

            processor.StackPointer.Should().Be(0xFD);
        }

        #endregion

        #region ADC - Add with Carry Tests

        [Theory]
        [InlineData(0, 0, false, 0)]
        [InlineData(0, 1, false, 1)]
        [InlineData(1, 2, false, 3)]
        [InlineData(255, 1, false, 0)]
        [InlineData(254, 1, false, 255)]
        [InlineData(255, 0, false, 255)]
        [InlineData(0, 0, true, 1)]
        [InlineData(0, 1, true, 2)]
        [InlineData(1, 2, true, 4)]
        [InlineData(254, 1, true, 0)]
        [InlineData(253, 1, true, 255)]
        [InlineData(254, 0, true, 255)]
        [InlineData(255, 255, true, 255)]
        public void ADC_Accumulator_Correct_When_Not_In_BDC_Mode(byte accumulatorInitialValue, byte amountToAdd,
            bool carryFlagSet, byte expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            if (carryFlagSet)
            {
                processor.LoadProgram(0, new byte[] {0x38, 0xA9, accumulatorInitialValue, 0x69, amountToAdd}, 0x00);
                processor.NextStep();
            }
            else
            {
                processor.LoadProgram(0, new byte[] {0xA9, accumulatorInitialValue, 0x69, amountToAdd}, 0x00);
            }

            processor.NextStep();
            processor.Accumulator.Should().Be(accumulatorInitialValue);

            processor.NextStep();
            processor.Accumulator.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x99, 0x99, false, 0x98)]
        [InlineData(0x99, 0x99, true, 0x99)]
        [InlineData(0x90, 0x99, false, 0x89)]
        public void ADC_Accumulator_Correct_When_In_BDC_Mode(byte accumulatorInitialValue, byte amountToAdd,
            bool setCarryFlag, byte expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            if (setCarryFlag)
            {
                processor.LoadProgram(0, new byte[] {0x38, 0xF8, 0xA9, accumulatorInitialValue, 0x69, amountToAdd},
                    0x00);
                processor.NextStep();
            }
            else
            {
                processor.LoadProgram(0, new byte[] {0xF8, 0xA9, accumulatorInitialValue, 0x69, amountToAdd}, 0x00);
            }

            processor.NextStep();
            processor.NextStep();
            processor.Accumulator.Should().Be(accumulatorInitialValue);

            processor.NextStep();
            processor.Accumulator.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(254, 1, false, false)]
        [InlineData(254, 1, true, true)]
        [InlineData(253, 1, true, false)]
        [InlineData(255, 1, false, true)]
        [InlineData(255, 1, true, true)]
        public void ADC_Carry_Correct_When_Not_In_BDC_Mode(byte accumulatorInitialValue, byte amountToAdd,
            bool setCarryFlag,
            bool expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            if (setCarryFlag)
            {
                processor.LoadProgram(0, new byte[] {0x38, 0xA9, accumulatorInitialValue, 0x69, amountToAdd}, 0x00);
                processor.NextStep();
            }
            else
            {
                processor.LoadProgram(0, new byte[] {0xA9, accumulatorInitialValue, 0x69, amountToAdd}, 0x00);
            }

            processor.NextStep();
            processor.Accumulator.Should().Be(accumulatorInitialValue);

            processor.NextStep();
            processor.CarryFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(98, 1, false, false)]
        [InlineData(98, 1, true, false)]
        [InlineData(99, 1, false, false)]
        [InlineData(99, 1, true, false)]
        public void ADC_Carry_Correct_When_In_BDC_Mode(byte accumulatorInitialValue, byte amountToAdd,
            bool setCarryFlag,
            bool expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0, new byte[] {0xF8, 0xA9, accumulatorInitialValue, 0x69, amountToAdd}, 0x00);

            processor.NextStep();
            processor.NextStep();
            processor.Accumulator.Should().Be(accumulatorInitialValue);

            processor.NextStep();
            processor.CarryFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0, 0, true)]
        [InlineData(255, 1, true)]
        [InlineData(0, 1, false)]
        [InlineData(1, 0, false)]
        public void ADC_Zero_Flag_Correct_When_Not_In_BDC_Mode(byte accumulatorInitialValue, byte amountToAdd,
            bool expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorInitialValue, 0x69, amountToAdd}, 0x00);

            processor.NextStep();
            processor.Accumulator.Should().Be(accumulatorInitialValue);

            processor.NextStep();
            processor.ZeroFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(126, 1, false)]
        [InlineData(1, 126, false)]
        [InlineData(1, 127, true)]
        [InlineData(127, 1, true)]
        [InlineData(1, 254, true)]
        [InlineData(254, 1, true)]
        [InlineData(1, 255, false)]
        [InlineData(255, 1, false)]
        public void ADC_Negative_Flag_Correct(byte accumulatorInitialValue, byte amountToAdd, bool expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);


            processor.LoadProgram(0, new byte[] {0xA9, accumulatorInitialValue, 0x69, amountToAdd}, 0x00);

            processor.NextStep();
            processor.Accumulator.Should().Be(accumulatorInitialValue);

            processor.NextStep();
            processor.NegativeFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0, 127, false, false)]
        [InlineData(0, 128, false, false)]
        [InlineData(1, 127, false, true)]
        [InlineData(1, 128, false, false)]
        [InlineData(127, 1, false, true)]
        [InlineData(127, 127, false, true)]
        [InlineData(128, 127, false, false)]
        [InlineData(128, 128, false, true)]
        [InlineData(128, 129, false, true)]
        [InlineData(128, 255, false, true)]
        [InlineData(255, 0, false, false)]
        [InlineData(255, 1, false, false)]
        [InlineData(255, 127, false, false)]
        [InlineData(255, 128, false, true)]
        [InlineData(255, 255, false, false)]
        [InlineData(0, 127, true, true)]
        [InlineData(0, 128, true, false)]
        [InlineData(1, 127, true, true)]
        [InlineData(1, 128, true, false)]
        [InlineData(127, 1, true, true)]
        [InlineData(127, 127, true, true)]
        [InlineData(128, 127, true, false)]
        [InlineData(128, 128, true, true)]
        [InlineData(128, 129, true, true)]
        [InlineData(128, 255, true, false)]
        [InlineData(255, 0, true, false)]
        [InlineData(255, 1, true, false)]
        [InlineData(255, 127, true, false)]
        [InlineData(255, 128, true, false)]
        [InlineData(255, 255, true, false)]
        public void ADC_Overflow_Flag_Correct(byte accumulatorInitialValue, byte amountToAdd, bool setCarryFlag,
            bool expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            if (setCarryFlag)
            {
                processor.LoadProgram(0, new byte[] {0x38, 0xA9, accumulatorInitialValue, 0x69, amountToAdd}, 0x00);
                processor.NextStep();
            }
            else
            {
                processor.LoadProgram(0, new byte[] {0xA9, accumulatorInitialValue, 0x69, amountToAdd}, 0x00);
            }

            processor.NextStep();
            processor.Accumulator.Should().Be(accumulatorInitialValue);

            processor.NextStep();
            processor.OverflowFlag.Should().Be(expectedValue);
        }

        #endregion

        #region AND - Compare Memory with Accumulator

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(255, 255, 255)]
        [InlineData(255, 254, 254)]
        [InlineData(170, 85, 0)]
        public void AND_Accumulator_Correct(byte accumulatorInitialValue, byte amountToAnd, byte expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorInitialValue, 0x29, amountToAnd}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.Accumulator.Should().Be(expectedResult);
        }

        #endregion

        #region ASL - Arithmetic Shift Left

        [Theory]
        [InlineData(0x0A, 109, 218, 0)]
        [InlineData(0x0A, 108, 216, 0)]
        [InlineData(0x06, 109, 218, 0x01)]
        [InlineData(0x16, 109, 218, 0x01)]
        [InlineData(0x0E, 109, 218, 0x01)]
        [InlineData(0x1E, 109, 218, 0x01)]
        public void ASL_Correct_Value_Stored(byte operation, byte valueToShift, byte expectedValue,
            byte expectedLocation)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0, new byte[] {0xA9, valueToShift, operation, expectedLocation}, 0x00);
            processor.NextStep();
            processor.NextStep();

            (operation == 0x0A
                    ? processor.Accumulator
                    : processor.ReadMemoryValue(expectedLocation)).Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(127, false)]
        [InlineData(128, true)]
        [InlineData(255, true)]
        [InlineData(0, false)]
        public void ASL_Carry_Set_Correctly(byte valueToShift, bool expectedValue)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0, new byte[] {0xA9, valueToShift, 0x0A}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.CarryFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(63, false)]
        [InlineData(64, true)]
        [InlineData(127, true)]
        [InlineData(128, false)]
        [InlineData(0, false)]
        public void ASL_Negative_Set_Correctly(byte valueToShift, bool expectedValue)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0, new byte[] {0xA9, valueToShift, 0x0A}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(127, false)]
        [InlineData(128, true)]
        [InlineData(0, true)]
        public void ASL_Zero_Set_Correctly(byte valueToShift, bool expectedValue)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0, new byte[] {0xA9, valueToShift, 0x0A}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedValue);
        }

        #endregion

        #region BCC - Branch On Carry Clear

        [Theory]
        [InlineData(0, 1, 3)]
        [InlineData(0x80, 0x80, 2)]
        [InlineData(0, 0xFD, 0xFFFF)]
        [InlineData(0x7D, 0x80, 0xFFFF)]
        public void BCC_Program_Counter_Correct(int programCounterInitialValue, byte offset, int expectedValue)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(programCounterInitialValue, new byte[] {0x90, offset}, programCounterInitialValue);
            processor.NextStep();

            processor.ProgramCounter.Should().Be(expectedValue);
        }

        #endregion

        #region BCS - Branch on Carry Set

        [Theory]
        [InlineData(0, 1, 4)]
        [InlineData(0x80, 0x80, 3)]
        [InlineData(0, 0xFC, 0xFFFF)]
        [InlineData(0x7C, 0x80, 0xFFFF)]
        public void BCS_Program_Counter_Correct(int programCounterInitialValue, byte offset, int expectedValue)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(programCounterInitialValue, new byte[] {0x38, 0xB0, offset},
                programCounterInitialValue);
            processor.NextStep();
            processor.NextStep();

            processor.ProgramCounter.Should().Be(expectedValue);
        }

        #endregion

        #region BEQ - Branch on Zero Set

        [Theory]
        [InlineData(0, 1, 5)]
        [InlineData(0x80, 0x80, 4)]
        [InlineData(0, 0xFB, 0xFFFF)]
        [InlineData(0x7B, 0x80, 0xFFFF)]
        [InlineData(2, 0xFE, 4)]
        public void BEQ_Program_Counter_Correct(int programCounterInitialValue, byte offset, int expectedValue)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(programCounterInitialValue, new byte[] {0xA9, 0x00, 0xF0, offset},
                programCounterInitialValue);
            processor.NextStep();
            processor.NextStep();

            processor.ProgramCounter.Should().Be(expectedValue);
        }

        #endregion

        #region BIT - Compare Memory with Accumulator

        [Theory]
        [InlineData(0x24, 0x7f, 0x7F, false)]
        [InlineData(0x24, 0x80, 0x7F, false)]
        [InlineData(0x24, 0x7F, 0x80, true)]
        [InlineData(0x24, 0x80, 0xFF, true)]
        [InlineData(0x24, 0xFF, 0x80, true)]
        [InlineData(0x2C, 0x7F, 0x7F, false)]
        [InlineData(0x2C, 0x80, 0x7F, false)]
        [InlineData(0x2C, 0x7F, 0x80, true)]
        [InlineData(0x2C, 0x80, 0xFF, true)]
        [InlineData(0x2C, 0xFF, 0x80, true)]
        public void BIT_Negative_Set_When_Comparison_Is_Negative_Number(byte operation, byte accumulatorValue,
            byte valueToTest, bool expectedResult)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0x00, new byte[] {0xA9, accumulatorValue, operation, 0x06, 0x00, 0x00, valueToTest},
                0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x24, 0x3F, 0x3F, false)]
        [InlineData(0x24, 0x3F, 0x40, true)]
        [InlineData(0x24, 0x40, 0x3F, false)]
        [InlineData(0x24, 0x40, 0x7F, true)]
        [InlineData(0x24, 0x7F, 0x40, true)]
        [InlineData(0x24, 0x7F, 0x80, false)]
        [InlineData(0x24, 0x80, 0x7F, true)]
        [InlineData(0x24, 0xC0, 0xDF, true)]
        [InlineData(0x24, 0xDF, 0xC0, true)]
        [InlineData(0x24, 0x3F, 0x3F, false)]
        [InlineData(0x24, 0xC0, 0xFF, true)]
        [InlineData(0x24, 0xFF, 0xC0, true)]
        [InlineData(0x24, 0x40, 0xFF, true)]
        [InlineData(0x24, 0xFF, 0x40, true)]
        [InlineData(0x24, 0xC0, 0x7F, true)]
        [InlineData(0x24, 0x7F, 0xC0, true)]
        [InlineData(0x2C, 0x3F, 0x3F, false)]
        [InlineData(0x2C, 0x3F, 0x40, true)]
        [InlineData(0x2C, 0x40, 0x3F, false)]
        [InlineData(0x2C, 0x40, 0x7F, true)]
        [InlineData(0x2C, 0x7F, 0x40, true)]
        [InlineData(0x2C, 0x7F, 0x80, false)]
        [InlineData(0x2C, 0x80, 0x7F, true)]
        [InlineData(0x2C, 0xC0, 0xDF, true)]
        [InlineData(0x2C, 0xDF, 0xC0, true)]
        [InlineData(0x2C, 0x3F, 0x3F, false)]
        [InlineData(0x2C, 0xC0, 0xFF, true)]
        [InlineData(0x2C, 0xFF, 0xC0, true)]
        [InlineData(0x2C, 0x40, 0xFF, true)]
        [InlineData(0x2C, 0xFF, 0x40, true)]
        [InlineData(0x2C, 0xC0, 0x7F, true)]
        [InlineData(0x2C, 0x7F, 0xC0, true)]
        public void BIT_Overflow_Set_By_Bit_Six(byte operation, byte accumulatorValue, byte valueToTest,
            bool expectedResult)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0x00, new byte[] {0xA9, accumulatorValue, operation, 0x06, 0x00, 0x00, valueToTest},
                0x00);
            processor.NextStep();
            processor.NextStep();

            processor.OverflowFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x24, 0x00, 0x00, true)]
        [InlineData(0x24, 0xFF, 0xFF, false)]
        [InlineData(0x24, 0xAA, 0x55, true)]
        [InlineData(0x24, 0x55, 0xAA, true)]
        [InlineData(0x2C, 0x00, 0x00, true)]
        [InlineData(0x2C, 0xFF, 0xFF, false)]
        [InlineData(0x2C, 0xAA, 0x55, true)]
        [InlineData(0x2C, 0x55, 0xAA, true)]
        public void BIT_Zero_Set_When_Comparison_Is_Zero(byte operation, byte accumulatorValue, byte valueToTest,
            bool expectedResult)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0x00, new byte[] {0xA9, accumulatorValue, operation, 0x06, 0x00, 0x00, valueToTest},
                0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        #endregion

        #region BMI - Branch if Negative Set

        [Theory]
        [InlineData(0, 1, 5)]
        [InlineData(0x80, 0x80, 4)]
        [InlineData(0, 0xFB, 0xFFFF)]
        [InlineData(0x7B, 0x80, 0xFFFF)]
        public void BMI_Program_Counter_Correct(int programCounterInitialValue, byte offset, int expectedValue)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(programCounterInitialValue, new byte[] {0xA9, 0x80, 0x30, offset},
                programCounterInitialValue);
            processor.NextStep();
            processor.NextStep();

            processor.ProgramCounter.Should().Be(expectedValue);
        }

        #endregion

        #region BNE - Branch On Result Not Zero

        [Theory]
        [InlineData(0, 1, 5)]
        [InlineData(0x80, 0x80, 4)]
        [InlineData(0, 0xFB, 0xFFFF)]
        [InlineData(0x7B, 0x80, 0xFFFF)]
        public void BNE_Program_Counter_Correct(int programCounterInitialValue, byte offset, int expectedValue)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(programCounterInitialValue, new byte[] {0xA9, 0x01, 0xD0, offset},
                programCounterInitialValue);
            processor.NextStep();
            processor.NextStep();

            processor.ProgramCounter.Should().Be(expectedValue);
        }

        #endregion

        #region BPL - Branch if Negative Clear

        [Theory]
        [InlineData(0, 1, 5)]
        [InlineData(0x80, 0x80, 4)]
        [InlineData(0, 0xFB, 0xFFFF)]
        [InlineData(0x7B, 0x80, 0xFFFF)]
        public void BPL_Program_Counter_Correct(int programCounterInitialValue, byte offset, int expectedValue)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(programCounterInitialValue, new byte[] {0xA9, 0x79, 0x10, offset},
                programCounterInitialValue);
            processor.NextStep();
            processor.NextStep();

            processor.ProgramCounter.Should().Be(expectedValue);
        }

        #endregion

        #region BRK - Simulate Interrupt Request (IRQ)

        [Fact]
        public void BRK_Program_Counter_Set_To_Address_At_Break_Vector_Address()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0x00}, 0x00);

            //Manually Write the Break Address
            processor.WriteMemoryValue(0xFFFE, 0xBC);
            processor.WriteMemoryValue(0xFFFF, 0xCD);

            processor.NextStep();

            processor.ProgramCounter.Should().Be(0xCDBC);
        }

        [Fact]
        public void BRK_Program_Counter_Stack_Correct()
        {
            var processor = new Processor();

            processor.LoadProgram(0xABCD, new byte[] {0x00}, 0xABCD);

            var stackLocation = processor.StackPointer;
            processor.NextStep();

            processor.ReadMemoryValue(stackLocation + 0x100).Should().Be(0xAB);
            processor.ReadMemoryValue(stackLocation + 0x100 - 1).Should().Be(0xCF);
        }

        [Fact]
        public void BRK_Stack_Pointer_Correct()
        {
            var processor = new Processor();

            processor.LoadProgram(0xABCD, new byte[] {0x00}, 0xABCD);

            var stackLocation = processor.StackPointer;
            processor.NextStep();

            processor.StackPointer.Should().Be(stackLocation - 3);
        }

        [Theory]
        [InlineData(0x038, 0x31)]
        [InlineData(0x0F8, 0x38)]
        [InlineData(0x078, 0x34)]
        public void BRK_Stack_Set_Flag_Operations_Correctly(byte operation, byte expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0x58, operation, 0x00}, 0x00);

            var stackLocation = processor.StackPointer;
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.ReadMemoryValue(stackLocation + 0x100 - 2).Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x01, 0x80, 0xB0)]
        [InlineData(0x01, 0x7F, 0xF0)]
        [InlineData(0x00, 0x00, 0x32)]
        public void BRK_Stack_Non_Set_Flag_Operations_Correctly(byte accumulatorValue, byte memoryValue,
            byte expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0x58, 0xA9, accumulatorValue, 0x69, memoryValue, 0x00}, 0x00);

            var stackLocation = processor.StackPointer;
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.ReadMemoryValue(stackLocation + 0x100 - 2).Should().Be(expectedValue);
        }

        #endregion

        #region BVC - Branch if Overflow Clear

        [Theory]
        [InlineData(0, 1, 3)]
        [InlineData(0x80, 0x80, 2)]
        [InlineData(0, 0xFD, 0xFFFF)]
        [InlineData(0x7D, 0x80, 0xFFFF)]
        public void BVC_Program_Counter_Correct(int programCounterInitialValue, byte offset, int expectedValue)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(programCounterInitialValue, new byte[] {0x50, offset}, programCounterInitialValue);
            processor.NextStep();

            processor.ProgramCounter.Should().Be(expectedValue);
        }

        #endregion

        #region BVS - Branch if Overflow Set

        [Theory]
        [InlineData(0, 1, 7)]
        [InlineData(0x80, 0x80, 6)]
        [InlineData(0, 0xF9, 0xFFFF)]
        [InlineData(0x79, 0x80, 0xFFFF)]
        public void BVS_Program_Counter_Correct(int programCounterInitialValue, byte offset, int expectedValue)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(programCounterInitialValue, new byte[] {0xA9, 0x01, 0x69, 0x7F, 0x70, offset},
                programCounterInitialValue);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            processor.ProgramCounter.Should().Be(expectedValue);
        }

        #endregion

        #region CLC - Clear Carry Flag

        [Fact]
        public void CLC_Carry_Flag_Cleared_Correctly()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0x18}, 0x00);
            processor.NextStep();

            processor.CarryFlag.Should().Be(false);
        }

        #endregion

        #region CLD - Clear Decimal Flag

        [Fact]
        public void CLD_Carry_Flag_Set_And_Cleared_Correctly()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xF8, 0xD8}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.DecimalFlag.Should().Be(false);
        }

        #endregion

        #region CLI - Clear Interrupt Flag

        [Fact]
        public void CLI_Interrupt_Flag_Cleared_Correctly()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0x58}, 0x00);
            processor.NextStep();

            processor.DisableInterruptFlag.Should().Be(false);
        }

        #endregion

        #region CLV - Clear Overflow Flag

        [Fact]
        public void CLV_Overflow_Flag_Cleared_Correctly()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xB8}, 0x00);
            processor.NextStep();

            processor.OverflowFlag.Should().Be(false);
        }

        #endregion

        #region CMP - Compare Memory With Accumulator

        [Theory]
        [InlineData(0x00, 0x00, true)]
        [InlineData(0xFF, 0x00, false)]
        [InlineData(0x00, 0xFF, false)]
        [InlineData(0xFF, 0xFF, true)]
        public void CMP_Zero_Flag_Set_When_Values_Match(byte accumulatorValue, byte memoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0xC9, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }


        [Theory]
        [InlineData(0x00, 0x00, true)]
        [InlineData(0xFF, 0x00, true)]
        [InlineData(0x00, 0xFF, false)]
        [InlineData(0x00, 0x01, false)]
        [InlineData(0xFF, 0xFF, true)]
        public void CMP_Carry_Flag_Set_When_Accumulator_Is_Greater_Than_Or_Equal(byte accumulatorValue,
            byte memoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0xC9, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.CarryFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0xFE, 0xFF, true)]
        [InlineData(0x81, 0x1, true)]
        [InlineData(0x81, 0x2, false)]
        [InlineData(0x79, 0x1, false)]
        [InlineData(0x00, 0x1, true)]
        public void CMP_Negative_Flag_Set_When_Result_Is_Negative(byte accumulatorValue, byte memoryValue,
            bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0xC9, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedResult);
        }

        #endregion

        #region CPX - Compare Memory With X Register

        [Theory]
        [InlineData(0x00, 0x00, true)]
        [InlineData(0xFF, 0x00, false)]
        [InlineData(0x00, 0xFF, false)]
        [InlineData(0xFF, 0xFF, true)]
        public void CPX_Zero_Flag_Set_When_Values_Match(byte xValue, byte memoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, xValue, 0xE0, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x00, 0x00, true)]
        [InlineData(0xFF, 0x00, true)]
        [InlineData(0x00, 0xFF, false)]
        [InlineData(0x00, 0x01, false)]
        [InlineData(0xFF, 0xFF, true)]
        public void CPX_Carry_Flag_Set_When_Accumulator_Is_Greater_Than_Or_Equal(byte xValue, byte memoryValue,
            bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, xValue, 0xE0, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.CarryFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0xFE, 0xFF, true)]
        [InlineData(0x81, 0x1, true)]
        [InlineData(0x81, 0x2, false)]
        [InlineData(0x79, 0x1, false)]
        [InlineData(0x00, 0x1, true)]
        public void CPX_Negative_Flag_Set_When_Result_Is_Negative(byte xValue, byte memoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, xValue, 0xE0, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedResult);
        }

        #endregion

        #region CPY - Compare Memory With X Register

        [Theory]
        [InlineData(0x00, 0x00, true)]
        [InlineData(0xFF, 0x00, false)]
        [InlineData(0x00, 0xFF, false)]
        [InlineData(0xFF, 0xFF, true)]
        public void CPY_Zero_Flag_Set_When_Values_Match(byte xValue, byte memoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, xValue, 0xC0, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x00, 0x00, true)]
        [InlineData(0xFF, 0x00, true)]
        [InlineData(0x00, 0xFF, false)]
        [InlineData(0x00, 0x01, false)]
        [InlineData(0xFF, 0xFF, true)]
        public void CPY_Carry_Flag_Set_When_Accumulator_Is_Greater_Than_Or_Equal(byte xValue, byte memoryValue,
            bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, xValue, 0xC0, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.CarryFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0xFE, 0xFF, true)]
        [InlineData(0x81, 0x1, true)]
        [InlineData(0x81, 0x2, false)]
        [InlineData(0x79, 0x1, false)]
        [InlineData(0x00, 0x1, true)]
        public void CPY_Negative_Flag_Set_When_Result_Is_Negative(byte xValue, byte memoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, xValue, 0xC0, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedResult);
        }

        #endregion

        #region DEC - Decrement Memory by One

        [Theory]
        [InlineData(0x00, 0xFF)]
        [InlineData(0xFF, 0xFE)]
        public void DEC_Memory_Has_Correct_Value(byte initialMemoryValue, byte expectedMemoryValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xC6, 0x03, 0x00, initialMemoryValue}, 0x00);
            processor.NextStep();

            processor.ReadMemoryValue(0x03).Should().Be(expectedMemoryValue);
        }

        [Theory]
        [InlineData(0x00, false)]
        [InlineData(0x01, true)]
        [InlineData(0x02, false)]
        public void DEC_Zero_Has_Correct_Value(byte initialMemoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xC6, 0x03, 0x00, initialMemoryValue}, 0x00);
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x80, false)]
        [InlineData(0x81, true)]
        [InlineData(0x00, true)]
        public void DEC_Negative_Has_Correct_Value(byte initialMemoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xC6, 0x03, 0x00, initialMemoryValue}, 0x00);
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedResult);
        }

        #endregion

        #region DEX - Decrement X by One

        [Theory]
        [InlineData(0x00, 0xFF)]
        [InlineData(0xFF, 0xFE)]
        public void DEX_XRegister_Has_Correct_Value(byte initialXRegisterValue, byte expectedMemoryValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, initialXRegisterValue, 0xCA}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.XRegister.Should().Be(expectedMemoryValue);
        }

        [Theory]
        [InlineData(0x00, false)]
        [InlineData(0x01, true)]
        [InlineData(0x02, false)]
        public void DEX_Zero_Has_Correct_Value(byte initialXRegisterValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, initialXRegisterValue, 0xCA}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x80, false)]
        [InlineData(0x81, true)]
        [InlineData(0x00, true)]
        public void DEX_Negative_Has_Correct_Value(byte initialXRegisterValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, initialXRegisterValue, 0xCA}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedResult);
        }

        #endregion

        #region DEY - Decrement Y by One

        [Theory]
        [InlineData(0x00, 0xFF)]
        [InlineData(0xFF, 0xFE)]
        public void DEY_YRegister_Has_Correct_Value(byte initialYRegisterValue, byte expectedMemoryValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, initialYRegisterValue, 0x88}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.YRegister.Should().Be(expectedMemoryValue);
        }

        [Theory]
        [InlineData(0x00, false)]
        [InlineData(0x01, true)]
        [InlineData(0x02, false)]
        public void DEY_Zero_Has_Correct_Value(byte initialYRegisterValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, initialYRegisterValue, 0x88}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x80, false)]
        [InlineData(0x81, true)]
        [InlineData(0x00, true)]
        public void DEY_Negative_Has_Correct_Value(byte initialYRegisterValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, initialYRegisterValue, 0x88}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedResult);
        }

        #endregion

        #region EOR - Exclusive OR Compare Accumulator With Memory

        [Theory]
        [InlineData(0x00, 0x00, 0x00)]
        [InlineData(0xFF, 0x00, 0xFF)]
        [InlineData(0x00, 0xFF, 0xFF)]
        [InlineData(0x55, 0xAA, 0xFF)]
        [InlineData(0xFF, 0xFF, 0x00)]
        public void EOR_Accumulator_Correct(byte accumulatorValue, byte memoryValue, byte expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0x49, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.Accumulator.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0xFF, 0xFF, false)]
        [InlineData(0x80, 0x7F, true)]
        [InlineData(0x40, 0x3F, false)]
        [InlineData(0xFF, 0x7F, true)]
        public void EOR_Negative_Flag_Correct(byte accumulatorValue, byte memoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0x49, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0xFF, 0xFF, true)]
        [InlineData(0x80, 0x7F, false)]
        public void EOR_Zero_Flag_Correct(byte accumulatorValue, byte memoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0x49, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        #endregion

        #region INC - Increment Memory by One

        [Theory]
        [InlineData(0x00, 0x01)]
        [InlineData(0xFF, 0x00)]
        public void INC_Memory_Has_Correct_Value(byte initialMemoryValue, byte expectedMemoryValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xE6, 0x03, 0x00, initialMemoryValue}, 0x00);
            processor.NextStep();

            processor.ReadMemoryValue(0x03).Should().Be(expectedMemoryValue);
        }

        [Theory]
        [InlineData(0x00, false)]
        [InlineData(0xFF, true)]
        [InlineData(0xFE, false)]
        public void INC_Zero_Has_Correct_Value(byte initialMemoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xE6, 0x03, 0x00, initialMemoryValue}, 0x00);
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x78, false)]
        [InlineData(0x80, true)]
        [InlineData(0x00, false)]
        public void INC_Negative_Has_Correct_Value(byte initialMemoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xE6, 0x02, initialMemoryValue}, 0x00);
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedResult);
        }

        #endregion

        #region INX - Increment X by One

        [Theory]
        [InlineData(0x00, 0x01)]
        [InlineData(0xFF, 0x00)]
        public void INX_XRegister_Has_Correct_Value(byte initialXRegister, byte expectedMemoryValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, initialXRegister, 0xE8}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.XRegister.Should().Be(expectedMemoryValue);
        }

        [Theory]
        [InlineData(0x00, false)]
        [InlineData(0xFF, true)]
        [InlineData(0xFE, false)]
        public void INX_Zero_Has_Correct_Value(byte initialXRegister, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, initialXRegister, 0xE8}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x78, false)]
        [InlineData(0x80, true)]
        [InlineData(0x00, false)]
        public void INX_Negative_Has_Correct_Value(byte initialXRegister, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, initialXRegister, 0xE8}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedResult);
        }

        #endregion

        #region INY - Increment Y by One

        [Theory]
        [InlineData(0x00, 0x01)]
        [InlineData(0xFF, 0x00)]
        public void INY_YRegister_Has_Correct_Value(byte initialYRegister, byte expectedMemoryValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, initialYRegister, 0xC8}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.YRegister.Should().Be(expectedMemoryValue);
        }

        [Theory]
        [InlineData(0x00, false)]
        [InlineData(0xFF, true)]
        [InlineData(0xFE, false)]
        public void INY_Zero_Has_Correct_Value(byte initialYRegister, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, initialYRegister, 0xC8}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x78, false)]
        [InlineData(0x80, true)]
        [InlineData(0x00, false)]
        public void INY_Negative_Has_Correct_Value(byte initialYRegister, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, initialYRegister, 0xC8}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedResult);
        }

        #endregion

        #region JMP - Jump to New Location

        [Fact]
        public void JMP_Program_Counter_Set_Correctly_After_Jump()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0x4C, 0x08, 0x00}, 0x00);
            processor.NextStep();

            processor.ProgramCounter.Should().Be(0x08);
        }

        [Fact]
        public void JMP_Program_Counter_Set_Correctly_After_Indirect_Jump()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0x6C, 0x03, 0x00, 0x08, 0x00}, 0x00);
            processor.NextStep();

            processor.ProgramCounter.Should().Be(0x08);
        }

        [Fact]
        public void JMP_Indirect_Wraps_Correct_If_MSB_IS_FF()
        {
            var processor = new Processor();
            processor.WriteMemoryValue(0x01FE, 0x6C);
            processor.LoadProgram(0, new byte[] {0x6C, 0xFF, 0x01, 0x08, 0x00}, 0x00);

            processor.WriteMemoryValue(0x01FF, 0x03);
            processor.WriteMemoryValue(0x0100, 0x02);
            processor.NextStep();

            processor.ProgramCounter.Should().Be(0x0203);
        }

        #endregion

        #region JSR - Jump to SubRoutine

        [Fact]
        public void JSR_Stack_Loads_Correct_Value()
        {
            var processor = new Processor();

            processor.LoadProgram(0xBBAA, new byte[] {0x20, 0xCC, 0xCC}, 0xBBAA);

            var stackLocation = processor.StackPointer;
            processor.NextStep();


            processor.ReadMemoryValue(stackLocation + 0x100).Should().Be(0xBB);
            processor.ReadMemoryValue(stackLocation + 0x100 - 1).Should().Be(0xAC);
        }

        [Fact]
        public void JSR_Program_Counter_Correct()
        {
            var processor = new Processor();

            processor.LoadProgram(0xBBAA, new byte[] {0x20, 0xCC, 0xCC}, 0xBBAA);
            processor.NextStep();


            processor.ProgramCounter.Should().Be(0xCCCC);
        }


        [Fact]
        public void JSR_Stack_Pointer_Correct()
        {
            var processor = new Processor();

            processor.LoadProgram(0xBBAA, new byte[] {0x20, 0xCC, 0xCC}, 0xBBAA);

            var stackLocation = processor.StackPointer;
            processor.NextStep();


            processor.StackPointer.Should().Be(stackLocation - 2);
        }

        #endregion

        #region LDA - Load Accumulator with Memory

        [Fact]
        public void LDA_Accumulator_Has_Correct_Value()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, 0x03}, 0x00);
            processor.NextStep();

            processor.Accumulator.Should().Be(0x03);
        }

        [Theory]
        [InlineData(0x0, true)]
        [InlineData(0x3, false)]
        public void LDA_Zero_Set_Correctly(byte valueToLoad, bool expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, valueToLoad}, 0x00);
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x00, false)]
        [InlineData(0x79, false)]
        [InlineData(0x80, true)]
        [InlineData(0xFF, true)]
        public void LDA_Negative_Set_Correctly(byte valueToLoad, bool expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, valueToLoad}, 0x00);
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedValue);
        }

        #endregion

        #region LDX - Load X with Memory

        [Fact]
        public void LDX_XRegister_Value_Has_Correct_Value()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, 0x03}, 0x00);
            processor.NextStep();

            processor.XRegister.Should().Be(0x03);
        }

        [Theory]
        [InlineData(0x00, false)]
        [InlineData(0x79, false)]
        [InlineData(0x80, true)]
        [InlineData(0xFF, true)]
        public void LDX_Negative_Flag_Set_Correctly(byte valueToLoad, bool expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, valueToLoad}, 0x00);
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x0, true)]
        [InlineData(0x3, false)]
        public void LDX_Zero_Set_Correctly(byte valueToLoad, bool expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, valueToLoad}, 0x00);
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedValue);
        }

        #endregion

        #region LDY - Load Y with Memory

        [Fact]
        public void STY_YRegister_Value_Has_Correct_Value()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, 0x03}, 0x00);
            processor.NextStep();

            processor.YRegister.Should().Be(0x03);
        }

        [Theory]
        [InlineData(0x00, false)]
        [InlineData(0x79, false)]
        [InlineData(0x80, true)]
        [InlineData(0xFF, true)]
        public void LDY_Negative_Flag_Set_Correctly(byte valueToLoad, bool expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, valueToLoad}, 0x00);
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x0, true)]
        [InlineData(0x3, false)]
        public void LDY_Zero_Set_Correctly(byte valueToLoad, bool expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, valueToLoad}, 0x00);
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedValue);
        }

        #endregion

        #region LSR - Logical Shift Right

        [Theory]
        [InlineData(0xFF, false, false)]
        [InlineData(0xFE, false, false)]
        [InlineData(0xFF, true, false)]
        [InlineData(0x00, true, false)]
        public void LSR_Negative_Set_Correctly(byte accumulatorValue, bool carryBitSet, bool expectedValue)
        {
            var processor = new Processor();

            var carryOperation = carryBitSet ? 0x38 : 0x18;

            processor.LoadProgram(0, new byte[] {(byte) carryOperation, 0xA9, accumulatorValue, 0x4A}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x1, true)]
        [InlineData(0x2, false)]
        public void LSR_Zero_Set_Correctly(byte accumulatorValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0x4A}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x1, true)]
        [InlineData(0x2, false)]
        public void LSR_Carry_Flag_Set_Correctly(byte accumulatorValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0x4A}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.CarryFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x4A, 0xFF, 0x7F, 0x00)]
        [InlineData(0x4A, 0xFD, 0x7E, 0x00)]
        [InlineData(0x46, 0xFF, 0x7F, 0x01)]
        [InlineData(0x56, 0xFF, 0x7F, 0x01)]
        [InlineData(0x4E, 0xFF, 0x7F, 0x01)]
        [InlineData(0x5E, 0xFF, 0x7F, 0x01)]
        public void LSR_Correct_Value_Stored(byte operation, byte valueToShift, byte expectedValue,
            byte expectedLocation)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0, new byte[] {0xA9, valueToShift, operation, expectedLocation}, 0x00);
            processor.NextStep();
            processor.NextStep();

            (operation == 0x4A
                    ? processor.Accumulator
                    : processor.ReadMemoryValue(expectedLocation)).Should().Be(expectedValue);
        }

        #endregion

        #region ORA - Bitwise OR Compare Memory with Accumulator

        [Theory]
        [InlineData(0x00, 0x00, 0x00)]
        [InlineData(0xFF, 0xFF, 0xFF)]
        [InlineData(0x55, 0xAA, 0xFF)]
        [InlineData(0xAA, 0x55, 0xFF)]
        public void ORA_Accumulator_Correct(byte accumulatorValue, byte memoryValue, byte expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0x09, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.Accumulator.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x00, 0x00, true)]
        [InlineData(0xFF, 0xFF, false)]
        [InlineData(0x00, 0x01, false)]
        public void ORA_Zero_Flag_Correct(byte accumulatorValue, byte memoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0x09, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x7F, 0x80, true)]
        [InlineData(0x79, 0x00, false)]
        [InlineData(0xFF, 0xFF, true)]
        public void ORA_Negative_Flag_Correct(byte accumulatorValue, byte memoryValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0x09, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedResult);
        }

        #endregion

        #region PHA - Push Accumulator Onto Stack

        [Fact]
        public void PHA_Stack_Has_Correct_Value()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, 0x03, 0x48}, 0x00);

            var stackLocation = processor.StackPointer;

            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.ReadMemoryValue(stackLocation + 0x100).Should().Be(0x03);
        }

        [Fact]
        public void PHA_Stack_Pointer_Has_Correct_Value()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, 0x03, 0x48}, 0x00);

            var stackLocation = processor.StackPointer;
            processor.NextStep();
            processor.NextStep();

            //A Push will decrement the Pointer by 1
            processor.StackPointer.Should().Be(stackLocation - 1);
        }

        [Fact]
        public void PHA_Stack_Pointer_Has_Correct_Value_When_Wrapping()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0x9A, 0x48}, 0x00);
            processor.NextStep();
            processor.NextStep();


            processor.StackPointer.Should().Be(0xFF);
        }

        #endregion

        #region PHP - Push Flags Onto Stack

        [Theory]
        [InlineData(0x038, 0x31)]
        [InlineData(0x0F8, 0x38)]
        [InlineData(0x078, 0x34)]
        public void PHP_Stack_Set_Flag_Operations_Correctly(byte operation, byte expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0x58, operation, 0x08}, 0x00);

            var stackLocation = processor.StackPointer;
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.ReadMemoryValue(stackLocation + 0x100).Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x01, 0x80, 0xB0)]
        [InlineData(0x01, 0x7F, 0xF0)]
        [InlineData(0x00, 0x00, 0x32)]
        public void PHP_Stack_Non_Set_Flag_Operations_Correctly(byte accumulatorValue, byte memoryValue,
            byte expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0x58, 0xA9, accumulatorValue, 0x69, memoryValue, 0x08}, 0x00);

            var stackLocation = processor.StackPointer;
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.ReadMemoryValue(stackLocation + 0x100).Should().Be(expectedValue);
        }

        [Fact]
        public void PHP_Stack_Pointer_Has_Correct_Value()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0x08}, 0x00);

            var stackLocation = processor.StackPointer;
            processor.NextStep();

            //A Push will decrement the Pointer by 1
            processor.StackPointer.Should().Be(stackLocation - 1);
        }

        #endregion

        #region PLA - Pull From Stack to Accumulator

        [Fact]
        public void PLA_Accumulator_Has_Correct_Value()
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Read From stack
            processor.LoadProgram(0, new byte[] {0xA9, 0x03, 0x48, 0xA9, 0x00, 0x68}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.Accumulator.Should().Be(0x03);
        }

        [Theory]
        [InlineData(0x00, true)]
        [InlineData(0x01, false)]
        [InlineData(0xFF, false)]
        public void PLA_Zero_Flag_Has_Correct_Value(byte valueToLoad, bool expectedResult)
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Read From stack
            processor.LoadProgram(0, new byte[] {0xA9, valueToLoad, 0x48, 0x68}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.ZeroFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x7F, false)]
        [InlineData(0x80, true)]
        [InlineData(0xFF, true)]
        public void PLA_Negative_Flag_Has_Correct_Value(byte valueToLoad, bool expectedResult)
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Read From stack
            processor.LoadProgram(0, new byte[] {0xA9, valueToLoad, 0x48, 0x68}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.NegativeFlag.Should().Be(expectedResult);
        }

        #endregion

        #region PLP - Pull From Stack to Flags

        [Fact]
        public void PLP_Carry_Flag_Set_Correctly()
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Read From stack
            processor.LoadProgram(0, new byte[] {0xA9, 0x01, 0x48, 0x28}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.CarryFlag.Should().Be(true);
        }

        [Fact]
        public void PLP_Zero_Flag_Set_Correctly()
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Read From stack
            processor.LoadProgram(0, new byte[] {0xA9, 0x02, 0x48, 0x28}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.ZeroFlag.Should().Be(true);
        }

        [Fact]
        public void PLP_Decimal_Flag_Set_Correctly()
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Read From stack
            processor.LoadProgram(0, new byte[] {0xA9, 0x08, 0x48, 0x28}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.DecimalFlag.Should().Be(true);
        }

        [Fact]
        public void PLP_Interrupt_Flag_Set_Correctly()
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Read From stack
            processor.LoadProgram(0, new byte[] {0xA9, 0x04, 0x48, 0x28}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.DisableInterruptFlag.Should().Be(true);
        }

        [Fact]
        public void PLP_Overflow_Flag_Set_Correctly()
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Read From stack
            processor.LoadProgram(0, new byte[] {0xA9, 0x40, 0x48, 0x28}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.OverflowFlag.Should().Be(true);
        }

        [Fact]
        public void PLP_Negative_Flag_Set_Correctly()
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Read From stack
            processor.LoadProgram(0, new byte[] {0xA9, 0x80, 0x48, 0x28}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.NegativeFlag.Should().Be(true);
        }

        #endregion

        #region ROL - Rotate Left

        [Theory]
        [InlineData(0x40, true)]
        [InlineData(0x3F, false)]
        [InlineData(0x80, false)]
        public void ROL_Negative_Set_Correctly(byte accumulatorValue, bool expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0x2A}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void ROL_Zero_Set_Correctly(bool carryFlagSet, bool expectedResult)
        {
            var processor = new Processor();

            var carryOperation = carryFlagSet ? 0x38 : 0x18;

            processor.LoadProgram(0, new byte[] {(byte) carryOperation, 0x2A}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x80, true)]
        [InlineData(0x7F, false)]
        public void ROL_Carry_Flag_Set_Correctly(byte accumulatorValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0x2A}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.CarryFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x2A, 0x55, 0xAA, 0x00)]
        [InlineData(0x2A, 0x55, 0xAA, 0x00)]
        [InlineData(0x26, 0x55, 0xAA, 0x01)]
        [InlineData(0x36, 0x55, 0xAA, 0x01)]
        [InlineData(0x2E, 0x55, 0xAA, 0x01)]
        [InlineData(0x3E, 0x55, 0xAA, 0x01)]
        public void ROL_Correct_Value_Stored(byte operation, byte valueToRotate, byte expectedValue,
            byte expectedLocation)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0, new byte[] {0xA9, valueToRotate, operation, expectedLocation}, 0x00);
            processor.NextStep();
            processor.NextStep();

            (operation == 0x2A
                    ? processor.Accumulator
                    : processor.ReadMemoryValue(expectedLocation)).Should().Be(expectedValue);
        }

        #endregion

        #region ROR - Rotate Left

        [Theory]
        [InlineData(0xFF, false, false)]
        [InlineData(0xFE, false, false)]
        [InlineData(0xFF, true, true)]
        [InlineData(0x00, true, true)]
        public void ROR_Negative_Set_Correctly(byte accumulatorValue, bool carryBitSet, bool expectedValue)
        {
            var processor = new Processor();

            var carryOperation = carryBitSet ? 0x38 : 0x18;

            processor.LoadProgram(0, new byte[] {(byte) carryOperation, 0xA9, accumulatorValue, 0x6A}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x00, false, true)]
        [InlineData(0x00, true, false)]
        [InlineData(0x01, false, true)]
        [InlineData(0x01, true, false)]
        public void ROR_Zero_Set_Correctly(byte accumulatorValue, bool carryBitSet, bool expectedResult)
        {
            var processor = new Processor();

            var carryOperation = carryBitSet ? 0x38 : 0x18;

            processor.LoadProgram(0, new byte[] {(byte) carryOperation, 0xA9, accumulatorValue, 0x6A}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x01, true)]
        [InlineData(0x02, false)]
        public void ROR_Carry_Flag_Set_Correctly(byte accumulatorValue, bool expectedResult)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorValue, 0x6A}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.CarryFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0x6A, 0xAA, 0x55, 0x00)]
        [InlineData(0x6A, 0xAA, 0x55, 0x00)]
        [InlineData(0x66, 0xAA, 0x55, 0x01)]
        [InlineData(0x76, 0xAA, 0x55, 0x01)]
        [InlineData(0x6E, 0xAA, 0x55, 0x01)]
        [InlineData(0x7E, 0xAA, 0x55, 0x01)]
        public void ROR_Correct_Value_Stored(byte operation, byte valueToRotate, byte expectedValue,
            byte expectedLocation)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0, new byte[] {0xA9, valueToRotate, operation, expectedLocation}, 0x00);
            processor.NextStep();
            processor.NextStep();

            (operation == 0x6A
                    ? processor.Accumulator
                    : processor.ReadMemoryValue(expectedLocation)).Should().Be(expectedValue);
        }

        #endregion

        #region RTI - Return from Interrupt

        [Fact]
        public void RTI_Program_Counter_Correct()
        {
            var processor = new Processor();

            processor.LoadProgram(0xABCD, new byte[] {0x00}, 0xABCD);
            //The Reset Vector Points to 0x0000 by default, so load the RTI instruction there.
            processor.WriteMemoryValue(0x00, 0x40);

            processor.NextStep();
            processor.NextStep();

            processor.ProgramCounter.Should().Be(0xABCF);
        }

        [Fact]
        public void RTI_Carry_Flag_Set_Correctly()
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Return from Interrupt
            processor.LoadProgram(0, new byte[] {0xA9, 0x01, 0x48, 0x40}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.CarryFlag.Should().Be(true);
        }

        [Fact]
        public void RTI_Zero_Flag_Set_Correctly()
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Return from Interrupt
            processor.LoadProgram(0, new byte[] {0xA9, 0x02, 0x48, 0x40}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.ZeroFlag.Should().Be(true);
        }

        [Fact]
        public void RTI_Decimal_Flag_Set_Correctly()
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Return from Interrupt
            processor.LoadProgram(0, new byte[] {0xA9, 0x08, 0x48, 0x40}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.DecimalFlag.Should().Be(true);
        }

        [Fact]
        public void RTI_Interrupt_Flag_Set_Correctly()
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Return from Interrupt
            processor.LoadProgram(0, new byte[] {0xA9, 0x04, 0x48, 0x40}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.DisableInterruptFlag.Should().Be(true);
        }

        [Fact]
        public void RTI_Overflow_Flag_Set_Correctly()
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Return from Interrupt
            processor.LoadProgram(0, new byte[] {0xA9, 0x40, 0x48, 0x40}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.OverflowFlag.Should().Be(true);
        }

        [Fact]
        public void RTI_Negative_Flag_Set_Correctly()
        {
            var processor = new Processor();

            //Load Accumulator and Transfer to Stack, Clear Accumulator, and Return from Interrupt
            processor.LoadProgram(0, new byte[] {0xA9, 0x80, 0x48, 0x40}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            //Accounting for the Offset in memory
            processor.NegativeFlag.Should().Be(true);
        }

        #endregion

        #region RTS - Return from SubRoutine

        [Fact]
        public void RTS_Program_Counter_Has_Correct_Value()
        {
            var processor = new Processor();

            processor.LoadProgram(0x00, new byte[] {0x20, 0x04, 0x00, 0x00, 0x60}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ProgramCounter.Should().Be(0x03);
        }

        [Fact]
        public void RTS_Stack_Pointer_Has_Correct_Value()
        {
            var processor = new Processor();

            processor.LoadProgram(0xBBAA, new byte[] {0x60}, 0xBBAA);

            var stackLocation = processor.StackPointer;
            processor.NextStep();


            processor.StackPointer.Should().Be(stackLocation + 2);
        }

        #endregion

        #region SBC - Subtraction With Borrow

        [Theory]
        [InlineData(0x0, 0x0, false, 0xFF)]
        [InlineData(0x0, 0x0, true, 0x00)]
        [InlineData(0x50, 0xf0, false, 0x5F)]
        [InlineData(0x50, 0xB0, true, 0xA0)]
        [InlineData(0xff, 0xff, false, 0xff)]
        [InlineData(0xff, 0xff, true, 0x00)]
        [InlineData(0xff, 0x80, false, 0x7e)]
        [InlineData(0xff, 0x80, true, 0x7f)]
        [InlineData(0x80, 0xff, false, 0x80)]
        [InlineData(0x80, 0xff, true, 0x81)]
        public void SBC_Accumulator_Correct_When_Not_In_BDC_Mode(byte accumulatorInitialValue, byte amountToSubtract,
            bool carryFlagSet, byte expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            if (carryFlagSet)
            {
                processor.LoadProgram(0, new byte[] {0x38, 0xA9, accumulatorInitialValue, 0xE9, amountToSubtract},
                    0x00);
                processor.NextStep();
            }
            else
            {
                processor.LoadProgram(0, new byte[] {0xA9, accumulatorInitialValue, 0xE9, amountToSubtract}, 0x00);
            }

            processor.NextStep();
            processor.Accumulator.Should().Be(accumulatorInitialValue);

            processor.NextStep();
            processor.Accumulator.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0, 0x99, false, 0)]
        [InlineData(0, 0x99, true, 1)]
        public void SBC_Accumulator_Correct_When_In_BDC_Mode(byte accumulatorInitialValue, byte amountToAdd,
            bool setCarryFlag, byte expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            if (setCarryFlag)
            {
                processor.LoadProgram(0, new byte[] {0x38, 0xF8, 0xA9, accumulatorInitialValue, 0xE9, amountToAdd},
                    0x00);
                processor.NextStep();
            }
            else
            {
                processor.LoadProgram(0, new byte[] {0xF8, 0xA9, accumulatorInitialValue, 0xE9, amountToAdd}, 0x00);
            }

            processor.NextStep();
            processor.NextStep();
            processor.Accumulator.Should().Be(accumulatorInitialValue);

            processor.NextStep();
            processor.Accumulator.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0xFF, 1, false, false)]
        [InlineData(0xFF, 0, false, false)]
        [InlineData(0x80, 0, false, true)]
        [InlineData(0x80, 0, true, false)]
        [InlineData(0x81, 1, false, true)]
        [InlineData(0x81, 1, true, false)]
        [InlineData(0, 0x80, false, false)]
        [InlineData(0, 0x80, true, true)]
        [InlineData(1, 0x80, true, true)]
        [InlineData(1, 0x7F, false, false)]
        public void SBC_Overflow_Correct_When_Not_In_BDC_Mode(byte accumulatorInitialValue, byte amountToSubtract,
            bool setCarryFlag,
            bool expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            if (setCarryFlag)
            {
                processor.LoadProgram(0, new byte[] {0x38, 0xA9, accumulatorInitialValue, 0xE9, amountToSubtract},
                    0x00);
                processor.NextStep();
            }
            else
            {
                processor.LoadProgram(0, new byte[] {0xA9, accumulatorInitialValue, 0xE9, amountToSubtract}, 0x00);
            }

            processor.NextStep();
            processor.Accumulator.Should().Be(accumulatorInitialValue);

            processor.NextStep();
            processor.OverflowFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(99, 1, false, false)]
        [InlineData(99, 0, false, false)]
        //[TestCase(0, 1, false, true)]
        //[TestCase(1, 1, true, true)]
        //[TestCase(2, 1, true, false)]
        //[TestCase(1, 1, false, false)]
        public void SBC_Overflow_Correct_When_In_BDC_Mode(byte accumulatorInitialValue, byte amountToSubtract,
            bool setCarryFlag,
            bool expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            if (setCarryFlag)
            {
                processor.LoadProgram(0, new byte[] {0x38, 0xF8, 0xA9, accumulatorInitialValue, 0xE9, amountToSubtract},
                    0x00);
                processor.NextStep();
            }
            else
            {
                processor.LoadProgram(0, new byte[] {0xF8, 0xA9, accumulatorInitialValue, 0xE9, amountToSubtract},
                    0x00);
            }


            processor.NextStep();
            processor.NextStep();
            processor.Accumulator.Should().Be(accumulatorInitialValue);

            processor.NextStep();
            processor.OverflowFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(0, 1, false)]
        [InlineData(1, 0, true)]
        [InlineData(2, 1, true)]
        public void SBC_Carry_Correct(byte accumulatorInitialValue, byte amountToSubtract, bool expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorInitialValue, 0xE9, amountToSubtract}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.CarryFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(0, 1, false)]
        [InlineData(1, 0, true)]
        [InlineData(1, 1, false)]
        public void SBC_Zero_Correct(byte accumulatorInitialValue, byte amountToSubtract, bool expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorInitialValue, 0xE9, amountToSubtract}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x80, 0x01, false)]
        [InlineData(0x81, 0x01, false)]
        [InlineData(0x00, 0x01, true)]
        [InlineData(0x01, 0x01, true)]
        public void SBC_Negative_Correct(byte accumulatorInitialValue, byte amountToSubtract, bool expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorInitialValue, 0xE9, amountToSubtract}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedValue);
        }

        #endregion

        #region SEC - Set Carry Flag

        [Fact]
        public void SEC_Carry_Flag_Set_Correctly()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0x38}, 0x00);
            processor.NextStep();

            processor.CarryFlag.Should().Be(true);
        }

        #endregion

        #region SED - Set Decimal Mode

        [Fact]
        public void SED_Decimal_Mode_Set_Correctly()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xF8}, 0x00);
            processor.NextStep();

            processor.DecimalFlag.Should().Be(true);
        }

        #endregion

        #region SEI - Set Interrup Flag

        [Fact]
        public void SEI_Interrupt_Flag_Set_Correctly()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0x78}, 0x00);
            processor.NextStep();

            processor.DisableInterruptFlag.Should().Be(true);
        }

        #endregion

        #region STA - Store Accumulator In Memory

        [Fact]
        public void STA_Memory_Has_Correct_Value()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA9, 0x03, 0x85, 0x05}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ReadMemoryValue(0x05).Should().Be(0x03);
        }

        #endregion

        #region STX - Set Memory To X

        [Fact]
        public void STX_Memory_Has_Correct_Value()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, 0x03, 0x86, 0x05}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ReadMemoryValue(0x05).Should().Be(0x03);
        }

        #endregion

        #region STY - Set Memory To Y

        [Fact]
        public void STY_Memory_Has_Correct_Value()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, 0x03, 0x84, 0x05}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ReadMemoryValue(0x05).Should().Be(0x03);
        }

        #endregion

        #region TAX, TAY, TSX, TSY Tests

        [Theory]
        [InlineData(0xAA, RegisterMode.Accumulator, RegisterMode.XRegister)]
        [InlineData(0xA8, RegisterMode.Accumulator, RegisterMode.YRegister)]
        [InlineData(0x8A, RegisterMode.XRegister, RegisterMode.Accumulator)]
        [InlineData(0x98, RegisterMode.YRegister, RegisterMode.Accumulator)]
        public void Transfer_Correct_Value_Set(byte operation, RegisterMode transferFrom, RegisterMode transferTo)
        {
            var processor = new Processor();
            byte loadOperation;

            switch (transferFrom)
            {
                case RegisterMode.Accumulator:
                    loadOperation = 0xA9;
                    break;
                case RegisterMode.XRegister:
                    loadOperation = 0xA2;
                    break;
                default:
                    loadOperation = 0xA0;
                    break;
            }

            processor.LoadProgram(0, new[] {loadOperation, (byte) 0x03, operation}, 0x00);
            processor.NextStep();
            processor.NextStep();


            switch (transferTo)
            {
                case RegisterMode.Accumulator:
                    processor.Accumulator.Should().Be(0x03);
                    break;
                case RegisterMode.XRegister:
                    processor.XRegister.Should().Be(0x03);
                    break;
                default:
                    processor.YRegister.Should().Be(0x03);
                    break;
            }
        }

        [Theory]
        [InlineData(0xAA, 0x80, RegisterMode.Accumulator, true)]
        [InlineData(0xA8, 0x80, RegisterMode.Accumulator, true)]
        [InlineData(0x8A, 0x80, RegisterMode.XRegister, true)]
        [InlineData(0x98, 0x80, RegisterMode.YRegister, true)]
        [InlineData(0xAA, 0xFF, RegisterMode.Accumulator, true)]
        [InlineData(0xA8, 0xFF, RegisterMode.Accumulator, true)]
        [InlineData(0x8A, 0xFF, RegisterMode.XRegister, true)]
        [InlineData(0x98, 0xFF, RegisterMode.YRegister, true)]
        [InlineData(0xAA, 0x7F, RegisterMode.Accumulator, false)]
        [InlineData(0xA8, 0x7F, RegisterMode.Accumulator, false)]
        [InlineData(0x8A, 0x7F, RegisterMode.XRegister, false)]
        [InlineData(0x98, 0x7F, RegisterMode.YRegister, false)]
        [InlineData(0xAA, 0x00, RegisterMode.Accumulator, false)]
        [InlineData(0xA8, 0x00, RegisterMode.Accumulator, false)]
        [InlineData(0x8A, 0x00, RegisterMode.XRegister, false)]
        [InlineData(0x98, 0x00, RegisterMode.YRegister, false)]
        public void Transfer_Negative_Value_Set(byte operation, byte value, RegisterMode transferFrom,
            bool expectedResult)
        {
            var processor = new Processor();
            byte loadOperation;

            switch (transferFrom)
            {
                case RegisterMode.Accumulator:
                    loadOperation = 0xA9;
                    break;
                case RegisterMode.XRegister:
                    loadOperation = 0xA2;
                    break;
                default:
                    loadOperation = 0xA0;
                    break;
            }

            processor.LoadProgram(0, new[] {loadOperation, value, operation}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(0xAA, 0xFF, RegisterMode.Accumulator, false)]
        [InlineData(0xA8, 0xFF, RegisterMode.Accumulator, false)]
        [InlineData(0x8A, 0xFF, RegisterMode.XRegister, false)]
        [InlineData(0x98, 0xFF, RegisterMode.YRegister, false)]
        [InlineData(0xAA, 0x00, RegisterMode.Accumulator, true)]
        [InlineData(0xA8, 0x00, RegisterMode.Accumulator, true)]
        [InlineData(0x8A, 0x00, RegisterMode.XRegister, true)]
        [InlineData(0x98, 0x00, RegisterMode.YRegister, true)]
        public void Transfer_Zero_Value_Set(byte operation, byte value, RegisterMode transferFrom, bool expectedResult)
        {
            var processor = new Processor();
            byte loadOperation;

            switch (transferFrom)
            {
                case RegisterMode.Accumulator:
                    loadOperation = 0xA9;
                    break;
                case RegisterMode.XRegister:
                    loadOperation = 0xA2;
                    break;
                default:
                    loadOperation = 0xA0;
                    break;
            }

            processor.LoadProgram(0, new[] {loadOperation, value, operation}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedResult);
        }

        #endregion

        #region TSX - Transfer Stack Pointer to X Register

        [Fact]
        public void TSX_XRegister_Set_Correctly()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xBA}, 0x00);

            var stackPointer = processor.StackPointer;
            processor.NextStep();

            processor.XRegister.Should().Be(stackPointer);
        }

        [Theory]
        [InlineData(0x00, false)]
        [InlineData(0x7F, false)]
        [InlineData(0x80, true)]
        [InlineData(0xFF, true)]
        public void TSX_Negative_Set_Correctly(byte valueToLoad, bool expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, valueToLoad, 0x9A, 0xBA}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            processor.NegativeFlag.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x00, true)]
        [InlineData(0x01, false)]
        [InlineData(0xFF, false)]
        public void TSX_Zero_Set_Correctly(byte valueToLoad, bool expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, valueToLoad, 0x9A, 0xBA}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(expectedValue);
        }

        #endregion

        #region TXS - Transfer X Register to Stack Pointer

        [Fact]
        public void TXS_Stack_Pointer_Set_Correctly()
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA2, 0xAA, 0x9A}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.StackPointer.Should().Be(0xAA);
        }

        #endregion

        #region Accumulator Address Tests

        [Theory]
        [InlineData(0x69, 0x01, 0x01, 0x02)]
        [InlineData(0x29, 0x03, 0x03, 0x03)]
        [InlineData(0xA9, 0x04, 0x03, 0x03)]
        [InlineData(0x49, 0x55, 0xAA, 0xFF)]
        [InlineData(0x09, 0x55, 0xAA, 0xFF)]
        [InlineData(0xE9, 0x03, 0x01, 0x01)]
        public void Immediate_Mode_Accumulator_Has_Correct_Result(byte operation, byte accumulatorInitialValue,
            byte valueToTest, byte expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorInitialValue, operation, valueToTest}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.Accumulator.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x65, 0x01, 0x01, 0x02)]
        [InlineData(0x25, 0x03, 0x03, 0x03)]
        [InlineData(0xA5, 0x04, 0x03, 0x03)]
        [InlineData(0x45, 0x55, 0xAA, 0xFF)]
        [InlineData(0x05, 0x55, 0xAA, 0xFF)]
        [InlineData(0xE5, 0x03, 0x01, 0x01)]
        public void ZeroPage_Mode_Accumulator_Has_Correct_Result(byte operation, byte accumulatorInitialValue,
            byte valueToTest, byte expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0, new byte[] {0xA9, accumulatorInitialValue, operation, 0x05, 0x00, valueToTest},
                0x00);
            processor.NextStep();
            processor.NextStep();

            processor.Accumulator.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x75, 0x00, 0x03, 0x03)]
        [InlineData(0x35, 0x03, 0x03, 0x03)]
        [InlineData(0xB5, 0x04, 0x03, 0x03)]
        [InlineData(0x55, 0x55, 0xAA, 0xFF)]
        [InlineData(0x15, 0x55, 0xAA, 0xFF)]
        [InlineData(0xF5, 0x03, 0x01, 0x01)]
        public void ZeroPageX_Mode_Accumulator_Has_Correct_Result(byte operation, byte accumulatorInitialValue,
            byte valueToTest, byte expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            //Just remember that my value's for the STX and ADC were added to the end of the byte array. In a real program this would be invalid, as an opcode would be next and 0x03 would be somewhere else
            processor.LoadProgram(0,
                new byte[] {0xA9, accumulatorInitialValue, 0xA2, 0x01, operation, 0x06, 0x00, valueToTest}, 0x00);
            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            processor.Accumulator.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x6D, 0x00, 0x03, 0x03)]
        [InlineData(0x2D, 0x03, 0x03, 0x03)]
        [InlineData(0xAD, 0x04, 0x03, 0x03)]
        [InlineData(0x4D, 0x55, 0xAA, 0xFF)]
        [InlineData(0x0D, 0x55, 0xAA, 0xFF)]
        [InlineData(0xED, 0x03, 0x01, 0x01)]
        public void Absolute_Mode_Accumulator_Has_Correct_Result(byte operation, byte accumulatorInitialValue,
            byte valueToTest, byte expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0,
                new byte[] {0xA9, accumulatorInitialValue, operation, 0x06, 0x00, 0x00, valueToTest}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.Accumulator.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x7D, 0x01, 0x01, false, 0x02)]
        [InlineData(0x3D, 0x03, 0x03, false, 0x03)]
        [InlineData(0xBD, 0x04, 0x03, false, 0x03)]
        [InlineData(0x5D, 0x55, 0xAA, false, 0xFF)]
        [InlineData(0x1D, 0x55, 0xAA, false, 0xFF)]
        [InlineData(0xFD, 0x03, 0x01, false, 0x01)]
        [InlineData(0x7D, 0x01, 0x01, true, 0x02)]
        [InlineData(0x3D, 0x03, 0x03, true, 0x03)]
        [InlineData(0xBD, 0x04, 0x03, true, 0x03)]
        [InlineData(0x5D, 0x55, 0xAA, true, 0xFF)]
        [InlineData(0x1D, 0x55, 0xAA, true, 0xFF)]
        [InlineData(0xFD, 0x03, 0x01, true, 0x01)]
        public void AbsoluteX_Mode_Accumulator_Has_Correct_Result(byte operation, byte accumulatorInitialValue,
            byte valueToTest, bool addressWraps, byte expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0, addressWraps
                    ? new byte[] {0xA9, accumulatorInitialValue, 0xA2, 0x09, operation, 0xff, 0xff, 0x00, valueToTest}
                    : new byte[] {0xA9, accumulatorInitialValue, 0xA2, 0x01, operation, 0x07, 0x00, 0x00, valueToTest},
                0x00);

            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            processor.Accumulator.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x79, 0x01, 0x01, false, 0x02)]
        [InlineData(0x39, 0x03, 0x03, false, 0x03)]
        [InlineData(0xB9, 0x04, 0x03, false, 0x03)]
        [InlineData(0x59, 0x55, 0xAA, false, 0xFF)]
        [InlineData(0x19, 0x55, 0xAA, false, 0xFF)]
        [InlineData(0xF9, 0x03, 0x01, false, 0x01)]
        [InlineData(0x79, 0x01, 0x01, true, 0x02)]
        [InlineData(0x39, 0x03, 0x03, true, 0x03)]
        [InlineData(0xB9, 0x04, 0x03, true, 0x03)]
        [InlineData(0x59, 0x55, 0xAA, true, 0xFF)]
        [InlineData(0x19, 0x55, 0xAA, true, 0xFF)]
        [InlineData(0xF9, 0x03, 0x01, true, 0x01)]
        public void AbsoluteY_Mode_Accumulator_Has_Correct_Result(byte operation, byte accumulatorInitialValue,
            byte valueToTest, bool addressWraps, byte expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0, addressWraps
                    ? new byte[] {0xA9, accumulatorInitialValue, 0xA0, 0x09, operation, 0xff, 0xff, 0x00, valueToTest}
                    : new byte[] {0xA9, accumulatorInitialValue, 0xA0, 0x01, operation, 0x07, 0x00, 0x00, valueToTest},
                0x00);

            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            processor.Accumulator.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x61, 0x01, 0x01, false, 0x02)]
        [InlineData(0x21, 0x03, 0x03, false, 0x03)]
        [InlineData(0xA1, 0x04, 0x03, false, 0x03)]
        [InlineData(0x41, 0x55, 0xAA, false, 0xFF)]
        [InlineData(0x01, 0x55, 0xAA, false, 0xFF)]
        [InlineData(0xE1, 0x03, 0x01, false, 0x01)]
        [InlineData(0x61, 0x01, 0x01, true, 0x02)]
        [InlineData(0x21, 0x03, 0x03, true, 0x03)]
        [InlineData(0xA1, 0x04, 0x03, true, 0x03)]
        [InlineData(0x41, 0x55, 0xAA, true, 0xFF)]
        [InlineData(0x01, 0x55, 0xAA, true, 0xFF)]
        [InlineData(0xE1, 0x03, 0x01, true, 0x01)]
        public void Indexed_Indirect_Mode_Accumulator_Has_Correct_Result(byte operation, byte accumulatorInitialValue,
            byte valueToTest, bool addressWraps, byte expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0,
                addressWraps
                    ? new byte[]
                        {0xA9, accumulatorInitialValue, 0xA6, 0x06, operation, 0xff, 0x08, 0x9, 0x00, valueToTest}
                    : new byte[]
                        {0xA9, accumulatorInitialValue, 0xA6, 0x06, operation, 0x01, 0x06, 0x9, 0x00, valueToTest},
                0x00);

            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            processor.Accumulator.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0x71, 0x01, 0x01, false, 0x02)]
        [InlineData(0x31, 0x03, 0x03, false, 0x03)]
        [InlineData(0xB1, 0x04, 0x03, false, 0x03)]
        [InlineData(0x51, 0x55, 0xAA, false, 0xFF)]
        [InlineData(0x11, 0x55, 0xAA, false, 0xFF)]
        [InlineData(0xF1, 0x03, 0x01, false, 0x01)]
        [InlineData(0x71, 0x01, 0x01, true, 0x02)]
        [InlineData(0x31, 0x03, 0x03, true, 0x03)]
        [InlineData(0xB1, 0x04, 0x03, true, 0x03)]
        [InlineData(0x51, 0x55, 0xAA, true, 0xFF)]
        [InlineData(0x11, 0x55, 0xAA, true, 0xFF)]
        [InlineData(0xF1, 0x03, 0x01, true, 0x01)]
        public void Indirect_Indexed_Mode_Accumulator_Has_Correct_Result(byte operation, byte accumulatorInitialValue,
            byte valueToTest, bool addressWraps, byte expectedValue)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0,
                addressWraps
                    ? new byte[]
                        {0xA9, accumulatorInitialValue, 0xA0, 0x0A, operation, 0x07, 0x00, 0xFF, 0xFF, valueToTest}
                    : new byte[]
                        {0xA9, accumulatorInitialValue, 0xA0, 0x01, operation, 0x07, 0x00, 0x08, 0x00, valueToTest},
                0x00);

            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            processor.Accumulator.Should().Be(expectedValue);
        }

        #endregion

        #region Index Address Tests

        [Theory]
        [InlineData(0xA6, 0x03, true)]
        [InlineData(0xB6, 0x03, true)]
        [InlineData(0xA4, 0x03, false)]
        [InlineData(0xB4, 0x03, false)]
        public void ZeroPage_Mode_Index_Has_Correct_Result(byte operation, byte valueToLoad, bool testXRegister)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0, new byte[] {operation, 0x03, 0x00, valueToLoad}, 0x00);
            processor.NextStep();

            (testXRegister ? processor.XRegister : processor.YRegister).Should().Be(valueToLoad);
        }


        [Theory]
        [InlineData(0xB6, 0x03, true)]
        [InlineData(0xB4, 0x03, false)]
        public void ZeroPage_Mode_Index_Has_Correct_Result_When_Wrapped(byte operation, byte valueToLoad,
            bool testXRegister)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0,
                new byte[] {testXRegister ? (byte) 0xA0 : (byte) 0xA2, 0xFF, operation, 0x06, 0x00, valueToLoad}, 0x00);
            processor.NextStep();
            processor.NextStep();

            (testXRegister ? processor.XRegister : processor.YRegister).Should().Be(valueToLoad);
        }

        [Theory]
        [InlineData(0xAE, 0x03, true)]
        [InlineData(0xAC, 0x03, false)]
        public void Absolute_Mode_Index_Has_Correct_Result(byte operation, byte valueToLoad, bool testXRegister)
        {
            var processor = new Processor();
            processor.Accumulator.Should().Be(0x00);

            processor.LoadProgram(0, new byte[] {operation, 0x04, 0x00, 0x00, valueToLoad}, 0x00);
            processor.NextStep();


            (testXRegister ? processor.XRegister : processor.YRegister).Should().Be(valueToLoad);
        }

        #endregion

        #region Compare Address Tests

        [Theory]
        [InlineData(0xC9, 0xFF, 0x00, RegisterMode.Accumulator)]
        [InlineData(0xE0, 0xFF, 0x00, RegisterMode.XRegister)]
        [InlineData(0xC0, 0xFF, 0x00, RegisterMode.YRegister)]
        public void Immediate_Mode_Compare_Operation_Has_Correct_Result(byte operation, byte accumulatorValue,
            byte memoryValue, RegisterMode mode)
        {
            var processor = new Processor();
            byte loadOperation;

            switch (mode)
            {
                case RegisterMode.Accumulator:
                    loadOperation = 0xA9;
                    break;
                case RegisterMode.XRegister:
                    loadOperation = 0xA2;
                    break;
                default:
                    loadOperation = 0xA0;
                    break;
            }

            processor.LoadProgram(0, new[] {loadOperation, accumulatorValue, operation, memoryValue}, 0x00);

            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(false);
            processor.NegativeFlag.Should().Be(true);
            processor.CarryFlag.Should().Be(true);
        }

        [Theory]
        [InlineData(0xC5, 0xFF, 0x00, RegisterMode.Accumulator)]
        [InlineData(0xD5, 0xFF, 0x00, RegisterMode.Accumulator)]
        [InlineData(0xE4, 0xFF, 0x00, RegisterMode.XRegister)]
        [InlineData(0xC4, 0xFF, 0x00, RegisterMode.YRegister)]
        public void ZeroPage_Modes_Compare_Operation_Has_Correct_Result(byte operation, byte accumulatorValue,
            byte memoryValue, RegisterMode mode)
        {
            var processor = new Processor();

            byte loadOperation;

            switch (mode)
            {
                case RegisterMode.Accumulator:
                    loadOperation = 0xA9;
                    break;
                case RegisterMode.XRegister:
                    loadOperation = 0xA2;
                    break;
                default:
                    loadOperation = 0xA0;
                    break;
            }

            processor.LoadProgram(0, new byte[] {loadOperation, accumulatorValue, operation, 0x04, memoryValue}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(false);
            processor.NegativeFlag.Should().Be(true);
            processor.CarryFlag.Should().Be(true);
        }

        [Theory]
        [InlineData(0xCD, 0xFF, 0x00, RegisterMode.Accumulator)]
        [InlineData(0xDD, 0xFF, 0x00, RegisterMode.Accumulator)]
        [InlineData(0xEC, 0xFF, 0x00, RegisterMode.XRegister)]
        [InlineData(0xCC, 0xFF, 0x00, RegisterMode.YRegister)]
        public void Absolute_Modes_Compare_Operation_Has_Correct_Result(byte operation, byte accumulatorValue,
            byte memoryValue, RegisterMode mode)
        {
            var processor = new Processor();

            byte loadOperation;

            switch (mode)
            {
                case RegisterMode.Accumulator:
                    loadOperation = 0xA9;
                    break;
                case RegisterMode.XRegister:
                    loadOperation = 0xA2;
                    break;
                default:
                    loadOperation = 0xA0;
                    break;
            }

            processor.LoadProgram(0, new byte[] {loadOperation, accumulatorValue, operation, 0x05, 0x00, memoryValue},
                0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(false);
            processor.NegativeFlag.Should().Be(true);
            processor.CarryFlag.Should().Be(true);
        }

        [Theory]
        [InlineData(0xC1, 0xFF, 0x00, true)]
        [InlineData(0xC1, 0xFF, 0x00, false)]
        public void Indexed_Indirect_Mode_CMP_Operation_Has_Correct_Result(byte operation, byte accumulatorValue,
            byte memoryValue, bool addressWraps)
        {
            var processor = new Processor();

            processor.LoadProgram(0,
                addressWraps
                    ? new byte[] {0xA9, accumulatorValue, 0xA6, 0x06, operation, 0xff, 0x08, 0x9, 0x00, memoryValue}
                    : new byte[] {0xA9, accumulatorValue, 0xA6, 0x06, operation, 0x01, 0x06, 0x9, 0x00, memoryValue},
                0x00);


            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(false);
            processor.NegativeFlag.Should().Be(true);
            processor.CarryFlag.Should().Be(true);
        }

        [Theory]
        [InlineData(0xD1, 0xFF, 0x00, true)]
        [InlineData(0xD1, 0xFF, 0x00, false)]
        public void Indirect_Indexed_Mode_CMP_Operation_Has_Correct_Result(byte operation, byte accumulatorValue,
            byte memoryValue, bool addressWraps)
        {
            var processor = new Processor();

            processor.LoadProgram(0,
                addressWraps
                    ? new byte[] {0xA9, accumulatorValue, 0x84, 0x06, operation, 0x07, 0x0A, 0xFF, 0xFF, memoryValue}
                    : new byte[] {0xA9, accumulatorValue, 0x84, 0x06, operation, 0x07, 0x01, 0x08, 0x00, memoryValue},
                0x00);

            processor.NextStep();
            processor.NextStep();
            processor.NextStep();

            processor.ZeroFlag.Should().Be(false);
            processor.NegativeFlag.Should().Be(true);
            processor.CarryFlag.Should().Be(true);
        }

        #endregion

        #region Decrement/Increment Address Tests

        [Theory]
        [InlineData(0xC6, 0xFF, 0xFE)]
        [InlineData(0xD6, 0xFF, 0xFE)]
        [InlineData(0xE6, 0xFF, 0x00)]
        [InlineData(0xF6, 0xFF, 0x00)]
        public void Zero_Page_DEC_INC_Has_Correct_Result(byte operation, byte memoryValue, byte expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {operation, 0x02, memoryValue}, 0x00);
            processor.NextStep();

            processor.ReadMemoryValue(0x02).Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(0xCE, 0xFF, 0xFE)]
        [InlineData(0xDE, 0xFF, 0xFE)]
        [InlineData(0xEE, 0xFF, 0x00)]
        [InlineData(0xFE, 0xFF, 0x00)]
        public void Absolute_DEC_INC_Has_Correct_Result(byte operation, byte memoryValue, byte expectedValue)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {operation, 0x03, 0x00, memoryValue}, 0x00);
            processor.NextStep();

            processor.ReadMemoryValue(0x03).Should().Be(expectedValue);
        }

        #endregion

        #region Store In Memory Address Tests

        [Theory]
        [InlineData(0x85, RegisterMode.Accumulator)]
        [InlineData(0x95, RegisterMode.Accumulator)]
        [InlineData(0x86, RegisterMode.XRegister)]
        [InlineData(0x96, RegisterMode.XRegister)]
        [InlineData(0x84, RegisterMode.YRegister)]
        [InlineData(0x94, RegisterMode.YRegister)]
        public void ZeroPage_Mode_Memory_Has_Correct_Result(byte operation, RegisterMode mode)
        {
            var processor = new Processor();

            byte loadOperation;
            switch (mode)
            {
                case RegisterMode.Accumulator:
                    loadOperation = 0xA9;
                    break;
                case RegisterMode.XRegister:
                    loadOperation = 0xA2;
                    break;
                default:
                    loadOperation = 0xA0;
                    break;
            }

            processor.LoadProgram(0, new byte[] {loadOperation, 0x04, operation, 0x00, 0x05}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ReadMemoryValue(0x04).Should().Be(0x05);
        }

        [Theory]
        [InlineData(0x8D, 0x03, RegisterMode.Accumulator)]
        [InlineData(0x9D, 0x03, RegisterMode.Accumulator)]
        [InlineData(0x99, 0x03, RegisterMode.Accumulator)]
        [InlineData(0x8E, 0x03, RegisterMode.XRegister)]
        [InlineData(0x8C, 0x03, RegisterMode.YRegister)]
        public void Absolute_Mode_Memory_Has_Correct_Result(byte operation, byte valueToLoad, RegisterMode mode)
        {
            var processor = new Processor();

            byte loadOperation;
            switch (mode)
            {
                case RegisterMode.Accumulator:
                    loadOperation = 0xA9;
                    break;
                case RegisterMode.XRegister:
                    loadOperation = 0xA2;
                    break;
                default:
                    loadOperation = 0xA0;
                    break;
            }

            processor.LoadProgram(0, new byte[] {loadOperation, valueToLoad, operation, 0x04}, 0x00);
            processor.NextStep();
            processor.NextStep();

            processor.ReadMemoryValue(0x04).Should().Be(valueToLoad);
        }

        #endregion

        #region Cycle Tests

        [Theory]
        [InlineData(0x69, 2)]
        [InlineData(0x65, 3)]
        [InlineData(0x75, 4)]
        [InlineData(0x6D, 4)]
        [InlineData(0x7D, 4)]
        [InlineData(0x79, 4)]
        [InlineData(0x61, 6)]
        [InlineData(0x71, 5)]
        [InlineData(0x29, 2)]
        [InlineData(0x25, 3)]
        [InlineData(0x35, 4)]
        [InlineData(0x2D, 4)]
        [InlineData(0x3D, 4)]
        [InlineData(0x39, 4)]
        [InlineData(0x21, 6)]
        [InlineData(0x31, 5)]
        [InlineData(0x0A, 2)]
        [InlineData(0x06, 5)]
        [InlineData(0x16, 6)]
        [InlineData(0x0E, 6)]
        [InlineData(0x1E, 7)]
        [InlineData(0x24, 3)]
        [InlineData(0x2C, 4)]
        [InlineData(0x00, 7)]
        [InlineData(0x18, 2)]
        [InlineData(0xD8, 2)]
        [InlineData(0x58, 2)]
        [InlineData(0xB8, 2)]
        [InlineData(0xC9, 2)]
        [InlineData(0xC5, 3)]
        [InlineData(0xD5, 4)]
        [InlineData(0xCD, 4)]
        [InlineData(0xDD, 4)]
        [InlineData(0xD9, 4)]
        [InlineData(0xC1, 6)]
        [InlineData(0xD1, 5)]
        [InlineData(0xE0, 2)]
        [InlineData(0xE4, 3)]
        [InlineData(0xEC, 4)]
        [InlineData(0xC0, 2)]
        [InlineData(0xC4, 3)]
        [InlineData(0xCC, 4)]
        [InlineData(0xC6, 5)]
        [InlineData(0xD6, 6)]
        [InlineData(0xCE, 6)]
        [InlineData(0xDE, 7)]
        [InlineData(0xCA, 2)]
        [InlineData(0x88, 2)]
        [InlineData(0x49, 2)]
        [InlineData(0x45, 3)]
        [InlineData(0x55, 4)]
        [InlineData(0x4D, 4)]
        [InlineData(0x5D, 4)]
        [InlineData(0x59, 4)]
        [InlineData(0x41, 6)]
        [InlineData(0x51, 5)]
        [InlineData(0xE6, 5)]
        [InlineData(0xF6, 6)]
        [InlineData(0xEE, 6)]
        [InlineData(0xFE, 7)]
        [InlineData(0xE8, 2)]
        [InlineData(0xC8, 2)]
        [InlineData(0x4C, 3)]
        [InlineData(0x6C, 5)]
        [InlineData(0x20, 6)]
        [InlineData(0xA9, 2)]
        [InlineData(0xA5, 3)]
        [InlineData(0xB5, 4)]
        [InlineData(0xAD, 4)]
        [InlineData(0xBD, 4)]
        [InlineData(0xB9, 4)]
        [InlineData(0xA1, 6)]
        [InlineData(0xB1, 5)]
        [InlineData(0xA2, 2)]
        [InlineData(0xA6, 3)]
        [InlineData(0xB6, 4)]
        [InlineData(0xAE, 4)]
        [InlineData(0xBE, 4)]
        [InlineData(0xA0, 2)]
        [InlineData(0xA4, 3)]
        [InlineData(0xB4, 4)]
        [InlineData(0xAC, 4)]
        [InlineData(0xBC, 4)]
        [InlineData(0x4A, 2)]
        [InlineData(0x46, 5)]
        [InlineData(0x56, 6)]
        [InlineData(0x4E, 6)]
        [InlineData(0x5E, 7)]
        [InlineData(0xEA, 2)]
        [InlineData(0x09, 2)]
        [InlineData(0x05, 3)]
        [InlineData(0x15, 4)]
        [InlineData(0x0D, 4)]
        [InlineData(0x1D, 4)]
        [InlineData(0x19, 4)]
        [InlineData(0x01, 6)]
        [InlineData(0x11, 5)]
        [InlineData(0x48, 3)]
        [InlineData(0x08, 3)]
        [InlineData(0x68, 4)]
        [InlineData(0x28, 4)]
        [InlineData(0x2A, 2)]
        [InlineData(0x26, 5)]
        [InlineData(0x36, 6)]
        [InlineData(0x2E, 6)]
        [InlineData(0x3E, 7)]
        [InlineData(0x6A, 2)]
        [InlineData(0x66, 5)]
        [InlineData(0x76, 6)]
        [InlineData(0x6E, 6)]
        [InlineData(0x7E, 7)]
        [InlineData(0x40, 6)]
        [InlineData(0x60, 6)]
        [InlineData(0xE9, 2)]
        [InlineData(0xE5, 3)]
        [InlineData(0xF5, 4)]
        [InlineData(0xED, 4)]
        [InlineData(0xFD, 4)]
        [InlineData(0xF9, 4)]
        [InlineData(0xE1, 6)]
        [InlineData(0xF1, 5)]
        [InlineData(0x38, 2)]
        [InlineData(0xF8, 2)]
        [InlineData(0x78, 2)]
        [InlineData(0x85, 3)]
        [InlineData(0x95, 4)]
        [InlineData(0x8D, 4)]
        [InlineData(0x9D, 5)]
        [InlineData(0x99, 5)]
        [InlineData(0x81, 6)]
        [InlineData(0x91, 6)]
        [InlineData(0x86, 3)]
        [InlineData(0x96, 4)]
        [InlineData(0x8E, 4)]
        [InlineData(0x84, 3)]
        [InlineData(0x94, 4)]
        [InlineData(0x8C, 4)]
        [InlineData(0xAA, 2)]
        [InlineData(0xA8, 2)]
        [InlineData(0xBA, 2)]
        [InlineData(0x8A, 2)]
        [InlineData(0x9A, 2)]
        [InlineData(0x98, 2)]
        public void NumberOfCyclesRemaining_Correct_After_Operations_That_Do_Not_Wrap(byte operation,
            int numberOfCyclesUsed)
        {
            var processor = new Processor();
            processor.LoadProgram(0, new byte[] {operation, 0x00}, 0x00);

            var startingNumberOfCycles = processor.CycleCount;
            processor.NextStep();

            processor.CycleCount.Should().Be(startingNumberOfCycles + numberOfCyclesUsed);
        }

        [Theory]
        [InlineData(0x07D, true, 5)]
        [InlineData(0x079, false, 5)]
        [InlineData(0x03D, true, 5)]
        [InlineData(0x039, false, 5)]
        [InlineData(0x1E, true, 7)]
        [InlineData(0xDD, true, 5)]
        [InlineData(0xD9, false, 5)]
        [InlineData(0xDE, true, 7)]
        [InlineData(0x05D, true, 5)]
        [InlineData(0x059, false, 5)]
        [InlineData(0xFE, true, 7)]
        [InlineData(0xBD, true, 5)]
        [InlineData(0xB9, false, 5)]
        [InlineData(0xBE, false, 5)]
        [InlineData(0xBC, true, 5)]
        [InlineData(0x5E, true, 7)]
        [InlineData(0x1D, true, 5)]
        [InlineData(0x19, false, 5)]
        [InlineData(0x3E, true, 7)]
        [InlineData(0x7E, true, 7)]
        [InlineData(0xFD, true, 5)]
        [InlineData(0xF9, false, 5)]
        [InlineData(0x9D, true, 5)]
        [InlineData(0x99, true, 5)]
        public void NumberOfCyclesRemaining_Correct_When_In_AbsoluteX_Or_AbsoluteY_And_Wrap(byte operation,
            bool isAbsoluteX, int numberOfCyclesUsed)
        {
            var processor = new Processor();

            processor.LoadProgram(0, isAbsoluteX
                ? new byte[] {0xA6, 0x06, operation, 0xff, 0xff, 0x00, 0x03}
                : new byte[] {0xA4, 0x06, operation, 0xff, 0xff, 0x00, 0x03}, 0x00);

            processor.NextStep();

            //Get the number of cycles after the register has been loaded, so we can isolate the operation under test
            var startingNumberOfCycles = processor.CycleCount;
            processor.NextStep();

            processor.CycleCount.Should().Be(startingNumberOfCycles + numberOfCyclesUsed);
        }

        [Theory]
        [InlineData(0x071, 6)]
        [InlineData(0x031, 6)]
        [InlineData(0xB1, 6)]
        [InlineData(0xD1, 6)]
        [InlineData(0x51, 6)]
        [InlineData(0x11, 6)]
        [InlineData(0xF1, 6)]
        [InlineData(0x91, 6)]
        public void NumberOfCyclesRemaining_Correct_When_In_IndirectIndexed_And_Wrap(byte operation,
            int numberOfCyclesUsed)
        {
            var processor = new Processor();

            processor.LoadProgram(0, new byte[] {0xA0, 0x04, operation, 0x05, 0x08, 0xFF, 0xFF, 0x03}, 0x00);
            processor.NextStep();
            //Get the number of cycles after the register has been loaded, so we can isolate the operation under test
            var startingNumberOfCycles = processor.CycleCount;
            processor.NextStep();

            processor.CycleCount.Should().Be(startingNumberOfCycles + numberOfCyclesUsed);
        }

        [Theory]
        [InlineData(0x90, 2, true)]
        [InlineData(0x90, 3, false)]
        [InlineData(0xB0, 2, false)]
        [InlineData(0xB0, 3, true)]
        public void NumberOfCyclesRemaining_Correct_When_Relative_And_Branch_On_Carry(byte operation,
            int numberOfCyclesUsed, bool isCarrySet)
        {
            var processor = new Processor();


            processor.LoadProgram(0, isCarrySet
                ? new byte[] {0x38, operation, 0x00}
                : new byte[] {0x18, operation, 0x00}, 0x00);
            processor.NextStep();


            //Get the number of cycles after the register has been loaded, so we can isolate the operation under test
            var startingNumberOfCycles = processor.CycleCount;
            processor.NextStep();

            processor.CycleCount.Should().Be(startingNumberOfCycles + numberOfCyclesUsed);
        }

        [Theory]
        [InlineData(0x90, 4, false, true)]
        [InlineData(0x90, 4, false, false)]
        [InlineData(0xB0, 4, true, true)]
        [InlineData(0xB0, 4, true, false)]
        public void NumberOfCyclesRemaining_Correct_When_Relative_And_Branch_On_Carry_And_Wrap(byte operation,
            int numberOfCyclesUsed, bool isCarrySet, bool wrapRight)
        {
            var processor = new Processor();

            var carryOperation = isCarrySet ? 0x38 : 0x18;
            var initialAddress = wrapRight ? 0xFFF0 : 0x00;
            var amountToMove = wrapRight ? 0x0F : 0x84;

            processor.LoadProgram(initialAddress,
                new byte[] {(byte) carryOperation, operation, (byte) amountToMove, 0x00}, initialAddress);
            processor.NextStep();

            //Get the number of cycles after the register has been loaded, so we can isolate the operation under test
            var startingNumberOfCycles = processor.CycleCount;
            processor.NextStep();

            processor.CycleCount.Should().Be(startingNumberOfCycles + numberOfCyclesUsed);
        }

        [Theory]
        [InlineData(0xF0, 3, true)]
        [InlineData(0xF0, 2, false)]
        [InlineData(0xD0, 3, false)]
        [InlineData(0xD0, 2, true)]
        public void NumberOfCyclesRemaining_Correct_When_Relative_And_Branch_On_Zero(byte operation,
            int numberOfCyclesUsed, bool isZeroSet)
        {
            var processor = new Processor();

            processor.LoadProgram(0, isZeroSet
                ? new byte[] {0xA9, 0x00, operation, 0x00}
                : new byte[] {0xA9, 0x01, operation, 0x00}, 0x00);

            processor.NextStep();


            //Get the number of cycles after the register has been loaded, so we can isolate the operation under test
            var startingNumberOfCycles = processor.CycleCount;
            processor.NextStep();

            processor.CycleCount.Should().Be(startingNumberOfCycles + numberOfCyclesUsed);
        }

        [Theory]
        [InlineData(0xF0, 4, true, true)]
        [InlineData(0xF0, 4, true, false)]
        [InlineData(0xD0, 4, false, true)]
        [InlineData(0xD0, 4, false, false)]
        public void NumberOfCyclesRemaining_Correct_When_Relative_And_Branch_On_Zero_And_Wrap(byte operation,
            int numberOfCyclesUsed, bool isZeroSet, bool wrapRight)
        {
            var processor = new Processor();

            var newAccumulatorValue = isZeroSet ? 0x00 : 0x01;
            var initialAddress = wrapRight ? 0xFFF0 : 0x00;
            var amountToMove = wrapRight ? 0x0D : 0x84;

            processor.LoadProgram(initialAddress,
                new byte[] {0xA9, (byte) newAccumulatorValue, operation, (byte) amountToMove, 0x00}, initialAddress);
            processor.NextStep();

            //Get the number of cycles after the register has been loaded, so we can isolate the operation under test
            var startingNumberOfCycles = processor.CycleCount;
            processor.NextStep();

            processor.CycleCount.Should().Be(startingNumberOfCycles + numberOfCyclesUsed);
        }

        [Theory]
        [InlineData(0x30, 3, true)]
        [InlineData(0x30, 2, false)]
        [InlineData(0x10, 3, false)]
        [InlineData(0x10, 2, true)]
        public void NumberOfCyclesRemaining_Correct_When_Relative_And_Branch_On_Negative(byte operation,
            int numberOfCyclesUsed, bool isNegativeSet)
        {
            var processor = new Processor();

            processor.LoadProgram(0, isNegativeSet
                ? new byte[] {0xA9, 0x80, operation, 0x00}
                : new byte[] {0xA9, 0x79, operation, 0x00}, 0x00);

            processor.NextStep();


            //Get the number of cycles after the register has been loaded, so we can isolate the operation under test
            var startingNumberOfCycles = processor.CycleCount;
            processor.NextStep();

            processor.CycleCount.Should().Be(startingNumberOfCycles + numberOfCyclesUsed);
        }

        [Theory]
        [InlineData(0x30, 4, true, true)]
        [InlineData(0x30, 4, true, false)]
        [InlineData(0x10, 4, false, true)]
        [InlineData(0x10, 4, false, false)]
        public void NumberOfCyclesRemaining_Correct_When_Relative_And_Branch_On_Negative_And_Wrap(byte operation,
            int numberOfCyclesUsed, bool isNegativeSet, bool wrapRight)
        {
            var processor = new Processor();

            var newAccumulatorValue = isNegativeSet ? 0x80 : 0x79;
            var initialAddress = wrapRight ? 0xFFF0 : 0x00;
            var amountToMove = wrapRight ? 0x0D : 0x84;

            processor.LoadProgram(initialAddress,
                new byte[] {0xA9, (byte) newAccumulatorValue, operation, (byte) amountToMove, 0x00}, initialAddress);
            processor.NextStep();

            //Get the number of cycles after the register has been loaded, so we can isolate the operation under test
            var startingNumberOfCycles = processor.CycleCount;
            processor.NextStep();

            processor.CycleCount.Should().Be(startingNumberOfCycles + numberOfCyclesUsed);
        }

        [Theory]
        [InlineData(0x50, 3, false)]
        [InlineData(0x50, 2, true)]
        [InlineData(0x70, 3, true)]
        [InlineData(0x70, 2, false)]
        public void NumberOfCyclesRemaining_Correct_When_Relative_And_Branch_On_Overflow(byte operation,
            int numberOfCyclesUsed, bool isOverflowSet)
        {
            var processor = new Processor();

            processor.LoadProgram(0, isOverflowSet
                ? new byte[] {0xA9, 0x01, 0x69, 0x7F, operation, 0x00}
                : new byte[] {0xA9, 0x01, 0x69, 0x01, operation, 0x00}, 0x00);

            processor.NextStep();
            processor.NextStep();

            //Get the number of cycles after the register has been loaded, so we can isolate the operation under test
            var startingNumberOfCycles = processor.CycleCount;
            processor.NextStep();

            processor.CycleCount.Should().Be(startingNumberOfCycles + numberOfCyclesUsed);
        }

        [Theory]
        [InlineData(0x50, 4, false, true)]
        [InlineData(0x50, 4, false, false)]
        [InlineData(0x70, 4, true, true)]
        [InlineData(0x70, 4, true, false)]
        public void NumberOfCyclesRemaining_Correct_When_Relative_And_Branch_On_Overflow_And_Wrap(byte operation,
            int numberOfCyclesUsed, bool isOverflowSet, bool wrapRight)
        {
            var processor = new Processor();

            var newAccumulatorValue = isOverflowSet ? 0x7F : 0x00;
            var initialAddress = wrapRight ? 0xFFF0 : 0x00;
            var amountToMove = wrapRight ? 0x0B : 0x86;

            processor.LoadProgram(initialAddress,
                new byte[] {0xA9, (byte) newAccumulatorValue, 0x69, 0x01, operation, (byte) amountToMove, 0x00},
                initialAddress);
            processor.NextStep();
            processor.NextStep();

            //Get the number of cycles after the register has been loaded, so we can isolate the operation under test
            var startingNumberOfCycles = processor.CycleCount;
            processor.NextStep();

            processor.CycleCount.Should().Be(startingNumberOfCycles + numberOfCyclesUsed);
        }

        #endregion

        #region Program Counter Tests

        [Theory]
        [InlineData(0x69, 2)]
        [InlineData(0x65, 2)]
        [InlineData(0x75, 2)]
        [InlineData(0x6D, 3)]
        [InlineData(0x7D, 3)]
        [InlineData(0x79, 3)]
        [InlineData(0x61, 2)]
        [InlineData(0x71, 2)]
        [InlineData(0x29, 2)]
        [InlineData(0x25, 2)]
        [InlineData(0x35, 2)]
        [InlineData(0x2D, 3)]
        [InlineData(0x3D, 3)]
        [InlineData(0x39, 3)]
        [InlineData(0x21, 2)]
        [InlineData(0x31, 2)]
        [InlineData(0x0A, 1)]
        [InlineData(0x06, 2)]
        [InlineData(0x16, 2)]
        [InlineData(0x0E, 3)]
        [InlineData(0x1E, 3)]
        [InlineData(0x24, 2)]
        [InlineData(0x2C, 3)]
        [InlineData(0x18, 1)]
        [InlineData(0xD8, 1)]
        [InlineData(0x58, 1)]
        [InlineData(0xB8, 1)]
        [InlineData(0xC9, 2)]
        [InlineData(0xC5, 2)]
        [InlineData(0xD5, 2)]
        [InlineData(0xCD, 3)]
        [InlineData(0xDD, 3)]
        [InlineData(0xD9, 3)]
        [InlineData(0xC1, 2)]
        [InlineData(0xD1, 2)]
        [InlineData(0xE0, 2)]
        [InlineData(0xE4, 2)]
        [InlineData(0xEC, 3)]
        [InlineData(0xC0, 2)]
        [InlineData(0xC4, 2)]
        [InlineData(0xCC, 3)]
        [InlineData(0xC6, 2)]
        [InlineData(0xD6, 2)]
        [InlineData(0xCE, 3)]
        [InlineData(0xDE, 3)]
        [InlineData(0xCA, 1)]
        [InlineData(0x88, 1)]
        [InlineData(0x49, 2)]
        [InlineData(0x45, 2)]
        [InlineData(0x55, 2)]
        [InlineData(0x4D, 3)]
        [InlineData(0x5D, 3)]
        [InlineData(0x59, 3)]
        [InlineData(0x41, 2)]
        [InlineData(0x51, 2)]
        [InlineData(0xE6, 2)]
        [InlineData(0xF6, 2)]
        [InlineData(0xEE, 3)]
        [InlineData(0xFE, 3)]
        [InlineData(0xE8, 1)]
        [InlineData(0xC8, 1)]
        [InlineData(0xA9, 2)]
        [InlineData(0xA5, 2)]
        [InlineData(0xB5, 2)]
        [InlineData(0xAD, 3)]
        [InlineData(0xBD, 3)]
        [InlineData(0xB9, 3)]
        [InlineData(0xA1, 2)]
        [InlineData(0xB1, 2)]
        [InlineData(0xA2, 2)]
        [InlineData(0xA6, 2)]
        [InlineData(0xB6, 2)]
        [InlineData(0xAE, 3)]
        [InlineData(0xBE, 3)]
        [InlineData(0xA0, 2)]
        [InlineData(0xA4, 2)]
        [InlineData(0xB4, 2)]
        [InlineData(0xAC, 3)]
        [InlineData(0xBC, 3)]
        [InlineData(0x4A, 1)]
        [InlineData(0x46, 2)]
        [InlineData(0x56, 2)]
        [InlineData(0x4E, 3)]
        [InlineData(0x5E, 3)]
        [InlineData(0xEA, 1)]
        [InlineData(0x09, 2)]
        [InlineData(0x05, 2)]
        [InlineData(0x15, 2)]
        [InlineData(0x0D, 3)]
        [InlineData(0x1D, 3)]
        [InlineData(0x19, 3)]
        [InlineData(0x01, 2)]
        [InlineData(0x11, 2)]
        [InlineData(0x48, 1)]
        [InlineData(0x08, 1)]
        [InlineData(0x68, 1)]
        [InlineData(0x28, 1)]
        [InlineData(0x2A, 1)]
        [InlineData(0x26, 2)]
        [InlineData(0x36, 2)]
        [InlineData(0x2E, 3)]
        [InlineData(0x3E, 3)]
        [InlineData(0x6A, 1)]
        [InlineData(0x66, 2)]
        [InlineData(0x76, 2)]
        [InlineData(0x6E, 3)]
        [InlineData(0x7E, 3)]
        [InlineData(0xE9, 2)]
        [InlineData(0xE5, 2)]
        [InlineData(0xF5, 2)]
        [InlineData(0xED, 3)]
        [InlineData(0xFD, 3)]
        [InlineData(0xF9, 3)]
        [InlineData(0xE1, 2)]
        [InlineData(0xF1, 2)]
        [InlineData(0x38, 1)]
        [InlineData(0xF8, 1)]
        [InlineData(0x78, 1)]
        [InlineData(0x85, 2)]
        [InlineData(0x95, 2)]
        [InlineData(0x8D, 3)]
        [InlineData(0x9D, 3)]
        [InlineData(0x99, 3)]
        [InlineData(0x81, 2)]
        [InlineData(0x91, 2)]
        [InlineData(0x86, 2)]
        [InlineData(0x96, 2)]
        [InlineData(0x8E, 3)]
        [InlineData(0x84, 2)]
        [InlineData(0x94, 2)]
        [InlineData(0x8C, 3)]
        [InlineData(0xAA, 1)]
        [InlineData(0xA8, 1)]
        [InlineData(0xBA, 1)]
        [InlineData(0x8A, 1)]
        [InlineData(0x9A, 1)]
        [InlineData(0x98, 1)]
        public void Program_Counter_Correct(byte operation, int expectedProgramCounter)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);


            processor.LoadProgram(0, new byte[] {operation, 0x0}, 0x00);
            processor.NextStep();

            processor.ProgramCounter.Should().Be(expectedProgramCounter);
        }

        [Theory]
        [InlineData(0x90, true, 2)]
        [InlineData(0xB0, false, 2)]
        public void Branch_On_Carry_Program_Counter_Correct_When_NoBranch_Occurs(byte operation, bool carrySet,
            byte expectedOutput)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0,
                carrySet
                    ? new byte[] {0x38, operation, 0x48}
                    : new byte[] {0x18, operation, 0x48}, 0x00);

            processor.NextStep();
            var currentProgramCounter = processor.ProgramCounter;

            processor.NextStep();
            processor.ProgramCounter.Should().Be(currentProgramCounter + expectedOutput);
        }

        [Theory]
        [InlineData(0xF0, false, 2)]
        [InlineData(0xD0, true, 2)]
        public void Branch_On_Zero_Program_Counter_Correct_When_NoBranch_Occurs(byte operation, bool zeroSet,
            byte expectedOutput)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0,
                zeroSet
                    ? new byte[] {0xA9, 0x00, operation}
                    : new byte[] {0xA9, 0x01, operation}, 0x00);

            processor.NextStep();
            var currentProgramCounter = processor.ProgramCounter;

            processor.NextStep();
            processor.ProgramCounter.Should().Be(currentProgramCounter + expectedOutput);
        }

        [Theory]
        [InlineData(0x30, false, 2)]
        [InlineData(0x10, true, 2)]
        public void Branch_On_Negative_Program_Counter_Correct_When_NoBranch_Occurs(byte operation, bool negativeSet,
            byte expectedOutput)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0,
                negativeSet
                    ? new byte[] {0xA9, 0x80, operation}
                    : new byte[] {0xA9, 0x79, operation}, 0x00);

            processor.NextStep();
            var currentProgramCounter = processor.ProgramCounter;

            processor.NextStep();
            processor.ProgramCounter.Should().Be(currentProgramCounter + expectedOutput);
        }

        [Theory]
        [InlineData(0x50, true, 2)]
        [InlineData(0x70, false, 2)]
        public void Branch_On_Overflow_Program_Counter_Correct_When_NoBranch_Occurs(byte operation, bool overflowSet,
            byte expectedOutput)
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0, overflowSet
                ? new byte[] {0xA9, 0x01, 0x69, 0x7F, operation, 0x00}
                : new byte[] {0xA9, 0x01, 0x69, 0x01, operation, 0x00}, 0x00);

            processor.NextStep();
            processor.NextStep();
            var currentProgramCounter = processor.ProgramCounter;

            processor.NextStep();
            processor.ProgramCounter.Should().Be(currentProgramCounter + expectedOutput);
        }

        [Fact]
        public void Program_Counter_Wraps_Correctly()
        {
            var processor = new Processor();
            processor.ProgramCounter.Should().Be(0);

            processor.LoadProgram(0xFFFF, new byte[] {0x38}, 0xFFFF);
            processor.NextStep();

            processor.ProgramCounter.Should().Be(0);
        }

        #endregion

        #region RunRoutine Tests - stop_on_rts, fail_on_brk, stop_on_address

        [Fact]
        public void RunRoutine_StopOnRts_Stops_On_Rts()
        {
            var processor = new Processor();
            // Set up a proper return address on the stack so RTS works correctly
            // Stack at 0x01FF-0x0100, SP starts at 0xFF
            // Push return address 0x1FFF (will return to 0x2000)
            processor.WriteMemoryValue(0x01FF, 0x1F);  // High byte
            processor.WriteMemoryValue(0x01FE, 0xFF);  // Low byte
            processor.StackPointer = 0xFD;  // Point below the pushed address

            // Simple routine: NOP, NOP, RTS
            processor.LoadProgram(0x1000, new byte[] { 0xEA, 0xEA, 0x60 }, 0x1000, reset: false);

            var result = processor.RunRoutine(0x1000, 0, stopOnRts: true, failOnBrk: true);

            result.Should().BeTrue("routine should exit cleanly on RTS");
            // RTS pops 0x1FFF and adds 1, so PC = 0x2000
            processor.ProgramCounter.Should().Be(0x2000, "PC should be at return address + 1");
        }

        [Fact]
        public void RunRoutine_StopOnRts_False_Does_Not_Stop_On_Rts()
        {
            var processor = new Processor();
            // Routine: NOP, RTS, BRK (should hit BRK if stop_on_rts is false)
            processor.LoadProgram(0x1000, new byte[] { 0xEA, 0x60, 0x00 }, 0x1000);

            var result = processor.RunRoutine(0x1000, 0, stopOnRts: false, failOnBrk: true);

            result.Should().BeFalse("routine should fail because it hit BRK after ignoring RTS");
        }

        [Fact]
        public void RunRoutine_StopOnRts_Tracks_Subroutine_Depth()
        {
            var processor = new Processor();
            // Set up stack for the final RTS to return cleanly
            processor.WriteMemoryValue(0x01FF, 0x2F);  // High byte - return to 0x3000
            processor.WriteMemoryValue(0x01FE, 0xFF);  // Low byte
            processor.StackPointer = 0xFD;

            // Main routine at 0x1000: JSR 0x2000, NOP, RTS
            // Subroutine at 0x2000: NOP, RTS
            processor.LoadProgram(0x1000, new byte[] { 0x20, 0x00, 0x20, 0xEA, 0x60 }, 0x1000, reset: false);
            processor.LoadProgram(0x2000, new byte[] { 0xEA, 0x60 }, 0x2000, reset: false);

            var result = processor.RunRoutine(0x1000, 0, stopOnRts: true, failOnBrk: true);

            result.Should().BeTrue("routine should exit cleanly");
            // JSR pushes return addr, subroutine RTS returns to 0x1003,
            // NOP executes, main RTS pops our setup address and goes to 0x3000
            processor.ProgramCounter.Should().Be(0x3000, "PC should be at the return address we set up");
        }

        [Fact]
        public void RunRoutine_FailOnBrk_True_Fails_On_Brk()
        {
            var processor = new Processor();
            // Routine: NOP, BRK
            processor.LoadProgram(0x1000, new byte[] { 0xEA, 0x00 }, 0x1000);

            var result = processor.RunRoutine(0x1000, 0, stopOnRts: true, failOnBrk: true);

            result.Should().BeFalse("routine should fail when hitting BRK with fail_on_brk=true");
        }

        [Fact]
        public void RunRoutine_FailOnBrk_False_Does_Not_Fail_On_Brk()
        {
            var processor = new Processor();
            // Routine: NOP, BRK
            processor.LoadProgram(0x1000, new byte[] { 0xEA, 0x00 }, 0x1000);

            var result = processor.RunRoutine(0x1000, 0, stopOnRts: true, failOnBrk: false);

            result.Should().BeTrue("routine should exit cleanly when hitting BRK with fail_on_brk=false");
        }

        [Fact]
        public void RunRoutine_StopOnAddress_Stops_At_Address()
        {
            var processor = new Processor();
            // Routine: NOP, NOP, NOP, NOP (we want to stop at 0x1002)
            processor.LoadProgram(0x1000, new byte[] { 0xEA, 0xEA, 0xEA, 0xEA }, 0x1000);

            var result = processor.RunRoutine(0x1000, stopOnAddress: 0x1002, stopOnRts: true, failOnBrk: true);

            result.Should().BeTrue("routine should exit cleanly when stopping at address");
            processor.ProgramCounter.Should().Be(0x1003, "PC should be one past the stop address after NextStep");
        }

        [Fact]
        public void RunRoutine_StopOnAddress_Zero_Does_Not_Trigger()
        {
            var processor = new Processor();
            // Set up stack for RTS
            processor.WriteMemoryValue(0x01FF, 0x2F);
            processor.WriteMemoryValue(0x01FE, 0xFF);
            processor.StackPointer = 0xFD;

            // Routine: NOP, RTS (stop_on_address=0 should be ignored)
            processor.LoadProgram(0x1000, new byte[] { 0xEA, 0x60 }, 0x1000, reset: false);

            var result = processor.RunRoutine(0x1000, stopOnAddress: 0, stopOnRts: true, failOnBrk: true);

            result.Should().BeTrue("routine should exit cleanly on RTS");
            processor.ProgramCounter.Should().Be(0x3000, "PC should be at return address");
        }

        [Fact]
        public void RunRoutine_StopOnAddress_Executes_Instruction_At_Stop_Address()
        {
            // NOTE: Current implementation executes the instruction at stop_on_address
            // before stopping. This test documents that behavior.
            var processor = new Processor();
            // Routine: NOP at 0x1000, NOP at 0x1001, NOP at 0x1002
            processor.LoadProgram(0x1000, new byte[] { 0xEA, 0xEA, 0xEA }, 0x1000);

            var result = processor.RunRoutine(0x1000, stopOnAddress: 0x1001, stopOnRts: true, failOnBrk: true);

            result.Should().BeTrue("routine should exit cleanly");
            // PC is at 0x1002 because we execute the instruction at 0x1001 before stopping
            processor.ProgramCounter.Should().Be(0x1002, "instruction at stop address is executed");
        }

        [Fact]
        public void RunRoutine_StopOnAddress_With_BRK_At_Address_Still_Triggers_FailOnBrk()
        {
            // This documents current behavior: instruction at stop_on_address is executed
            // So BRK at that address will still trigger fail_on_brk
            var processor = new Processor();
            processor.LoadProgram(0x1000, new byte[] { 0xEA, 0x00 }, 0x1000);

            var result = processor.RunRoutine(0x1000, stopOnAddress: 0x1001, stopOnRts: true, failOnBrk: true);

            result.Should().BeFalse("BRK at stop address is still executed and triggers failure");
        }

        [Fact]
        public void RunRoutine_Accumulator_Is_Set_After_Routine()
        {
            var processor = new Processor();
            // Routine: LDA #$42, RTS
            // 0xA9 0x42 = LDA #$42, 0x60 = RTS
            processor.LoadProgram(0x1000, new byte[] { 0xA9, 0x42, 0x60 }, 0x1000);

            var result = processor.RunRoutine(0x1000, 0, stopOnRts: true, failOnBrk: true);

            result.Should().BeTrue();
            processor.Accumulator.Should().Be(0x42, "accumulator should be set by the routine");
        }

        #endregion
    }
}