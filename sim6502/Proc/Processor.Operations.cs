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
/// An Implementation of a 6502 Processor - Operation Methods
/// </summary>
public partial class Processor
{
    #region Op Code Operations

    /// <summary>
    /// The ADC - Add Memory to Accumulator with Carry Operation
    /// </summary>
    /// <param name="addressingMode">The addressing mode used to perform this operation.</param>
    protected virtual void AddWithCarryOperation(AddressingMode addressingMode)
    {
        //Accumulator, Carry = Accumulator + ValueInMemoryLocation + Carry
        var memoryValue = ReadMemoryValue(GetAddressByAddressingMode(addressingMode));
        var newValue = memoryValue + Accumulator + (CarryFlag ? 1 : 0);


        OverflowFlag = ((Accumulator ^ newValue) & 0x80) != 0 && ((Accumulator ^ memoryValue) & 0x80) == 0;

        if (DecimalFlag)
        {
            newValue = int.Parse(memoryValue.ToString("x")) + int.Parse(Accumulator.ToString("x")) +
                       (CarryFlag ? 1 : 0);

            if (newValue > 99)
            {
                CarryFlag = true;
                newValue -= 100;
            }
            else
            {
                CarryFlag = false;
            }

            newValue = (int) Convert.ToInt64(string.Concat("0x", newValue), 16);
        }
        else
        {
            if (newValue > 255)
            {
                CarryFlag = true;
                newValue -= 256;
            }
            else
            {
                CarryFlag = false;
            }
        }

        SetZeroFlag(newValue);
        SetNegativeFlag(newValue);

        Accumulator = newValue;
    }

    /// <summary>
    /// The AND - Compare Memory with Accumulator operation
    /// </summary>
    /// <param name="addressingMode">The addressing mode being used</param>
    private void AndOperation(AddressingMode addressingMode)
    {
        Accumulator = ReadMemoryValue(GetAddressByAddressingMode(addressingMode)) & Accumulator;

        SetZeroFlag(Accumulator);
        SetNegativeFlag(Accumulator);
    }

    /// <summary>
    /// The ASL - Shift Left One Bit (Memory or Accumulator)
    /// </summary>
    /// <param name="addressingMode">The addressing Mode being used</param>
    private void AslOperation(AddressingMode addressingMode)
    {
        int value;
        var memoryAddress = 0;
        if (addressingMode == AddressingMode.Accumulator)
        {
            ReadMemoryValue(ProgramCounter + 1);
            value = Accumulator;
        }
        else
        {
            memoryAddress = GetAddressByAddressingMode(addressingMode);
            value = ReadMemoryValue(memoryAddress);
        }

        // Dummy Write
        if (addressingMode != AddressingMode.Accumulator) WriteMemoryValue(memoryAddress, (byte) value);

        // If the 7th bit is set, then we have a carry
        CarryFlag = (value & 0x80) != 0;

        // The And here ensures that if the value is greater than 255 it wraps properly.
        value = (value << 1) & 0xFE;

        SetNegativeFlag(value);
        SetZeroFlag(value);


        if (addressingMode == AddressingMode.Accumulator)
            Accumulator = value;
        else
            WriteMemoryValue(memoryAddress, (byte) value);
    }

    /// <summary>
    /// Performs the different branch operations.
    /// </summary>
    /// <param name="performBranch">Is a branch required</param>
    private void BranchOperation(bool performBranch)
    {
        var value = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.Relative));

        if (!performBranch)
        {
            ProgramCounter++;
            return;
        }

        MoveProgramCounterByRelativeValue(value);
    }

    /// <summary>
    /// The bit operation, does an & comparison between a value in memory and the accumulator
    /// </summary>
    /// <param name="addressingMode"></param>
    private void BitOperation(AddressingMode addressingMode)
    {
        var memoryValue = ReadMemoryValue(GetAddressByAddressingMode(addressingMode));
        var valueToCompare = memoryValue & Accumulator;

        OverflowFlag = (memoryValue & 0x40) != 0;

        SetNegativeFlag(memoryValue);
        SetZeroFlag(valueToCompare);
    }

    /// <summary>
    /// The compare operation. This operation compares a value in memory with a value passed into it.
    /// </summary>
    /// <param name="addressingMode">The addressing mode to use</param>
    /// <param name="comparisonValue">The value to compare against memory</param>
    private void CompareOperation(AddressingMode addressingMode, int comparisonValue)
    {
        var memoryValue = ReadMemoryValue(GetAddressByAddressingMode(addressingMode));
        var comparedValue = comparisonValue - memoryValue;

        if (comparedValue < 0)
            comparedValue += 0x10000;

        SetZeroFlag(comparedValue);

        CarryFlag = memoryValue <= comparisonValue;
        SetNegativeFlag(comparedValue);
    }

    /// <summary>
    /// Changes a value in memory by 1
    /// </summary>
    /// <param name="addressingMode">The addressing mode to use</param>
    /// <param name="decrement">If the operation is decrementing or incrementing the value by 1 </param>
    private void ChangeMemoryByOne(AddressingMode addressingMode, bool decrement)
    {
        var memoryLocation = GetAddressByAddressingMode(addressingMode);
        var memory = ReadMemoryValue(memoryLocation);

        WriteMemoryValue(memoryLocation, memory);

        if (decrement)
            memory -= 1;
        else
            memory += 1;

        SetZeroFlag(memory);
        SetNegativeFlag(memory);


        WriteMemoryValue(memoryLocation, memory);
    }

    /// <summary>
    /// Changes a value in either the X or Y register by 1
    /// </summary>
    /// <param name="useXRegister">If the operation is using the X or Y register</param>
    /// <param name="decrement">If the operation is decrementing or incrementing the value by 1 </param>
    private void ChangeRegisterByOne(bool useXRegister, bool decrement)
    {
        var value = useXRegister ? XRegister : YRegister;

        if (decrement)
            value -= 1;
        else
            value += 1;

        if (value < 0x00)
            value += 0x100;
        else if (value > 0xFF)
            value -= 0x100;

        SetZeroFlag(value);
        SetNegativeFlag(value);
        IncrementCycleCount();

        if (useXRegister)
            XRegister = value;
        else
            YRegister = value;
    }

    /// <summary>
    /// The EOR Operation, Performs an Exclusive OR Operation against the Accumulator and a value in memory
    /// </summary>
    /// <param name="addressingMode">The addressing mode to use</param>
    private void EorOperation(AddressingMode addressingMode)
    {
        Accumulator ^= ReadMemoryValue(GetAddressByAddressingMode(addressingMode));

        SetNegativeFlag(Accumulator);
        SetZeroFlag(Accumulator);
    }

    /// <summary>
    /// The LSR Operation. Performs a Left shift operation on a value in memory
    /// </summary>
    /// <param name="addressingMode">The addressing mode to use</param>
    private void LsrOperation(AddressingMode addressingMode)
    {
        int value;
        var memoryAddress = 0;
        if (addressingMode == AddressingMode.Accumulator)
        {
            ReadMemoryValue(ProgramCounter + 1);
            value = Accumulator;
        }
        else
        {
            memoryAddress = GetAddressByAddressingMode(addressingMode);
            value = ReadMemoryValue(memoryAddress);
        }

        // Dummy Write
        if (addressingMode != AddressingMode.Accumulator) WriteMemoryValue(memoryAddress, (byte) value);

        NegativeFlag = false;

        // If the Zero bit is set, we have a carry
        CarryFlag = (value & 0x01) != 0;

        value = value >> 1;

        SetZeroFlag(value);
        if (addressingMode == AddressingMode.Accumulator)
            Accumulator = value;
        else
            WriteMemoryValue(memoryAddress, (byte) value);
    }

    /// <summary>
    /// The Or Operation. Performs an Or Operation with the accumulator and a value in memory
    /// </summary>
    /// <param name="addressingMode">The addressing mode to use</param>
    private void OrOperation(AddressingMode addressingMode)
    {
        Accumulator |= ReadMemoryValue(GetAddressByAddressingMode(addressingMode));

        SetNegativeFlag(Accumulator);
        SetZeroFlag(Accumulator);
    }

    /// <summary>
    /// The ROL operation. Performs a rotate left operation on a value in memory.
    /// </summary>
    /// <param name="addressingMode">The addressing mode to use</param>
    private void RolOperation(AddressingMode addressingMode)
    {
        int value;
        var memoryAddress = 0;
        if (addressingMode == AddressingMode.Accumulator)
        {
            // Dummy Read
            ReadMemoryValue(ProgramCounter + 1);
            value = Accumulator;
        }
        else
        {
            memoryAddress = GetAddressByAddressingMode(addressingMode);
            value = ReadMemoryValue(memoryAddress);
        }

        // Dummy Write
        if (addressingMode != AddressingMode.Accumulator) WriteMemoryValue(memoryAddress, (byte) value);

        // Store the carry flag before shifting it
        var newCarry = (0x80 & value) != 0;

        // The And here ensures that if the value is greater than 255 it wraps properly.
        value = (value << 1) & 0xFE;

        if (CarryFlag)
            value |= 0x01;

        CarryFlag = newCarry;

        SetZeroFlag(value);
        SetNegativeFlag(value);


        if (addressingMode == AddressingMode.Accumulator)
            Accumulator = value;
        else
            WriteMemoryValue(memoryAddress, (byte) value);
    }

    /// <summary>
    /// The ROR operation. Performs a rotate right operation on a value in memory.
    /// </summary>
    /// <param name="addressingMode">The addressing mode to use</param>
    private void RorOperation(AddressingMode addressingMode)
    {
        int value;
        var memoryAddress = 0;
        if (addressingMode == AddressingMode.Accumulator)
        {
            // Dummy Read
            ReadMemoryValue(ProgramCounter + 1);
            value = Accumulator;
        }
        else
        {
            memoryAddress = GetAddressByAddressingMode(addressingMode);
            value = ReadMemoryValue(memoryAddress);
        }

        // Dummy Write
        if (addressingMode != AddressingMode.Accumulator) WriteMemoryValue(memoryAddress, (byte) value);

        // Store the carry flag before shifting it
        var newCarry = (0x01 & value) != 0;

        value = value >> 1;

        // If the carry flag is set then 0x
        if (CarryFlag)
            value |= 0x80;

        CarryFlag = newCarry;

        SetZeroFlag(value);
        SetNegativeFlag(value);

        if (addressingMode == AddressingMode.Accumulator)
            Accumulator = value;
        else
            WriteMemoryValue(memoryAddress, (byte) value);
    }

    /// <summary>
    /// The SBC operation. Performs a subtract with carry operation on the accumulator and a value in memory.
    /// </summary>
    /// <param name="addressingMode">The addressing mode to use</param>
    protected virtual void SubtractWithBorrowOperation(AddressingMode addressingMode)
    {
        var memoryValue = ReadMemoryValue(GetAddressByAddressingMode(addressingMode));
        var newValue = DecimalFlag
            ? int.Parse(Accumulator.ToString("x")) - int.Parse(memoryValue.ToString("x")) - (CarryFlag ? 0 : 1)
            : Accumulator - memoryValue - (CarryFlag ? 0 : 1);

        CarryFlag = newValue >= 0;

        if (DecimalFlag)
        {
            if (newValue < 0)
                newValue += 100;

            newValue = (int) Convert.ToInt64(string.Concat("0x", newValue), 16);
        }
        else
        {
            OverflowFlag = ((Accumulator ^ newValue) & 0x80) != 0 && ((Accumulator ^ memoryValue) & 0x80) != 0;

            if (newValue < 0)
                newValue += 256;
        }

        SetNegativeFlag(newValue);
        SetZeroFlag(newValue);

        Accumulator = newValue;
    }

    /// <summary>
    /// The PSP Operation. Pushes the Status Flags to the stack
    /// </summary>
    private void PushFlagsOperation()
    {
        PokeStack(ConvertFlagsToByte(true));
    }

    /// <summary>
    /// The PLP Operation. Pull the status flags off the stack on sets the flags accordingly.
    /// </summary>
    private void PullFlagsOperation()
    {
        var flags = PeekStack();
        CarryFlag = (flags & 0x01) != 0;
        ZeroFlag = (flags & 0x02) != 0;
        DisableInterruptFlag = (flags & 0x04) != 0;
        DecimalFlag = (flags & 0x08) != 0;
        OverflowFlag = (flags & 0x40) != 0;
        NegativeFlag = (flags & 0x80) != 0;
    }

    /// <summary>
    /// The JSR routine. Jumps to a subroutine.
    /// </summary>
    private void JumpToSubRoutineOperation()
    {
        IncrementCycleCount();

        // Put the high value on the stack, this should be the address after our operation -1
        // The RTS operation increments the PC by 1 which is why we don't move 2
        PokeStack((byte) (((ProgramCounter + 1) >> 8) & 0xFF));
        StackPointer--;
        IncrementCycleCount();

        PokeStack((byte) ((ProgramCounter + 1) & 0xFF));
        StackPointer--;
        IncrementCycleCount();

        ProgramCounter = GetAddressByAddressingMode(AddressingMode.Absolute);
    }

    /// <summary>
    /// The RTS routine. Called when returning from a subroutine.
    /// </summary>
    private void ReturnFromSubRoutineOperation()
    {
        ReadMemoryValue(++ProgramCounter);
        StackPointer++;
        IncrementCycleCount();

        var lowBit = PeekStack();
        StackPointer++;
        IncrementCycleCount();

        var highBit = PeekStack() << 8;
        IncrementCycleCount();

        ProgramCounter = (highBit | lowBit) + 1;
        IncrementCycleCount();
    }


    /// <summary>
    /// The BRK routine. Called when a BRK occurs.
    /// </summary>
    private void BreakOperation(bool isBrk, int vector)
    {
        ReadMemoryValue(++ProgramCounter);

        // Put the high value on the stack
        // When we RTI the address will be incremented by one, and the address after a break will not be used.
        PokeStack((byte) ((ProgramCounter >> 8) & 0xFF));
        StackPointer--;
        IncrementCycleCount();

        // Put the low value on the stack
        PokeStack((byte) (ProgramCounter & 0xFF));
        StackPointer--;
        IncrementCycleCount();

        // We only set the Break Flag if a BRK Occurs
        if (isBrk)
            PokeStack((byte) (ConvertFlagsToByte(true) | 0x10));
        else
            PokeStack(ConvertFlagsToByte(false));

        StackPointer--;
        IncrementCycleCount();

        DisableInterruptFlag = true;

        ProgramCounter = (ReadMemoryValue(vector + 1) << 8) | ReadMemoryValue(vector);

        _previousInterrupt = false;
    }

    /// <summary>
    /// The RTI routine. Called when returning from a BRK operation.
    /// Note: when called after a BRK operation the Program Counter is not set to the location after the BRK,
    /// it is set +1
    /// </summary>
    private void ReturnFromInterruptOperation()
    {
        ReadMemoryValue(++ProgramCounter);
        StackPointer++;
        IncrementCycleCount();

        PullFlagsOperation();
        StackPointer++;
        IncrementCycleCount();

        var lowBit = PeekStack();
        StackPointer++;
        IncrementCycleCount();

        var highBit = PeekStack() << 8;
        IncrementCycleCount();

        ProgramCounter = highBit | lowBit;
    }

    /// <summary>
    /// This is ran any time an NMI occurs
    /// </summary>
    private void ProcessNmi()
    {
        ProgramCounter--;
        BreakOperation(false, 0xFFFA);
        CurrentOpCode = ReadMemoryValue(ProgramCounter);

        SetDisassembly();
    }

    /// <summary>
    /// This is ran any time an IRQ occurs
    /// </summary>
    private void ProcessIrq()
    {
        if (DisableInterruptFlag)
            return;

        ProgramCounter--;
        BreakOperation(false, 0xFFFE);
        CurrentOpCode = ReadMemoryValue(ProgramCounter);

        SetDisassembly();
    }

    #endregion

    #region 65C02 Stack Operations

    /// <summary>
    /// PHX - Push X Register to Stack (65C02)
    /// </summary>
    public void PushXOperation()
    {
        ReadMemoryValue(ProgramCounter + 1);
        PokeStack((byte)XRegister);
        StackPointer--;
        IncrementCycleCount();
    }

    /// <summary>
    /// PLX - Pull X Register from Stack (65C02)
    /// </summary>
    public void PullXOperation()
    {
        ReadMemoryValue(ProgramCounter + 1);
        StackPointer++;
        IncrementCycleCount();
        XRegister = PeekStack();
        SetNegativeFlag(XRegister);
        SetZeroFlag(XRegister);
        IncrementCycleCount();
    }

    /// <summary>
    /// PHY - Push Y Register to Stack (65C02)
    /// </summary>
    public void PushYOperation()
    {
        ReadMemoryValue(ProgramCounter + 1);
        PokeStack((byte)YRegister);
        StackPointer--;
        IncrementCycleCount();
    }

    /// <summary>
    /// PLY - Pull Y Register from Stack (65C02)
    /// </summary>
    public void PullYOperation()
    {
        ReadMemoryValue(ProgramCounter + 1);
        StackPointer++;
        IncrementCycleCount();
        YRegister = PeekStack();
        SetNegativeFlag(YRegister);
        SetZeroFlag(YRegister);
        IncrementCycleCount();
    }

    #endregion

    #region 65C02 Store Operations

    /// <summary>
    /// STZ - Store Zero to Memory (65C02)
    /// Stores $00 to the specified memory location
    /// </summary>
    /// <param name="addressingMode">The addressing mode used to determine the target address</param>
    public void StoreZeroOperation(AddressingMode addressingMode)
    {
        WriteMemoryValue(GetAddressByAddressingMode(addressingMode), 0x00);
    }

    #endregion

    #region 65C02 Branch Operations

    /// <summary>
    /// BRA - Branch Always (65C02)
    /// Unconditionally branches to a relative address
    /// </summary>
    public void BranchAlwaysOperation()
    {
        var value = ReadMemoryValue(GetAddressByAddressingMode(AddressingMode.Relative));
        MoveProgramCounterByRelativeValue(value);
    }

    #endregion
}
