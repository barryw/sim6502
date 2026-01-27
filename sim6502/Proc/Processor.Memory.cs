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
/// An Implementation of a 6502 Processor - Memory Operations
/// </summary>
public partial class Processor
{
    /// <summary>
    /// Clear memory
    /// </summary>
    public void ResetMemory()
    {
        Memory = new byte[0x10000];
    }

    /// <summary>
    /// Clears the memory
    /// </summary>
    public void ClearMemory()
    {
        for (var i = 0; i < Memory.Length; i++)
            Memory[i] = 0x00;
    }

    /// <summary>
    /// Returns the byte at the given address.
    /// </summary>
    /// <param name="address">The address to return</param>
    /// <returns>the byte being returned</returns>
    public virtual byte ReadMemoryValue(int address)
    {
        var value = Memory[address];
        IncrementCycleCount();
        return value;
    }

    /// <summary>
    /// Returns the byte at a given address without incrementing the cycle. Useful for test harness.
    /// </summary>
    /// <param name="address"></param>
    /// <returns></returns>
    public virtual byte ReadMemoryValueWithoutCycle(int address)
    {
        var value = Memory[address];

        Logger.Trace($"Read BYTE value {value.ToString()} from location {address.ToString()}");

        return value;
    }

    /// <summary>
    /// Return the 16-bit word at a given address without incrementing the cycle counter.
    /// </summary>
    /// <param name="address"></param>
    /// <returns></returns>
    public virtual int ReadMemoryWordWithoutCycle(int address)
    {
        var lobyte = ReadMemoryValueWithoutCycle(address);
        var hibyte = ReadMemoryValueWithoutCycle(address + 1);
        var value = hibyte * 256 + lobyte;

        Logger.Trace($"Read WORD value {value.ToString()} from location {address.ToString()} and {(address + 1).ToString()}");

        return value;
    }

    /// <summary>
    /// Writes data to the given address.
    /// </summary>
    /// <param name="address">The address to write data to</param>
    /// <param name="data">The data to write</param>
    public virtual void WriteMemoryValue(int address, byte data)
    {
        IncrementCycleCount();
        WriteMemoryValueWithoutIncrement(address, data);
    }

    /// <summary>
    /// Writes data to the given address, but doesn't increase the cycle count.
    /// </summary>
    /// <param name="address">The address to write data to</param>
    /// <param name="data">The data to write</param>
    public virtual void WriteMemoryValueWithoutIncrement(int address, byte data)
    {
        Logger.Trace($"Writing BYTE {data.ToString()} to address {address.ToString()}");

        Memory[address] = data;
    }

    /// <summary>
    /// Write 2 bytes to address and address + 1
    /// </summary>
    /// <param name="address">The starting address to write to</param>
    /// <param name="word">The 16-bit word to write</param>
    public virtual void WriteMemoryWord(int address, int word)
    {
        var page = Convert.ToByte(word / 256);
        var b = Convert.ToByte(word - page * 256);

        Logger.Trace($"Writing WORD {word.ToString()} to address {address.ToString()} and {(address + 1).ToString()}");

        Memory[address] = b;
        Memory[address + 1] = page;
    }

    /// <summary>
    /// Writes either a byte or word to memory depending on the value
    /// </summary>
    /// <param name="address">The address to write to</param>
    /// <param name="value">The value to write</param>
    public virtual void WriteMemoryValue(int address, int value)
    {
        if (value > 255)
        {
            WriteMemoryWord(address, value);
        }
        else
        {
            WriteMemoryValueWithoutIncrement(address, (byte)value);
        }
    }

    /// <summary>
    /// Dumps the entire memory object. Used when saving the memory state
    /// </summary>
    /// <returns></returns>
    public byte[] DumpMemory()
    {
        return Memory;
    }

    /// <summary>
    /// Loads a program into the processors memory
    /// </summary>
    /// <param name="offset">The offset in memory when loading the program.</param>
    /// <param name="program">The program to be loaded</param>
    /// <param name="initialProgramCounter">The initial PC value, this is the entry point of the program</param>
    /// <param name="reset">Should the processor be reset after load? Defaults to false</param>
    public void LoadProgram(int offset, byte[] program, int initialProgramCounter, bool reset = true)
    {
        LoadProgram(offset, program);
        var bytes = BitConverter.GetBytes(initialProgramCounter);
        if (!reset) return;

        // Write the initialProgram Counter to the reset vector
        WriteMemoryValue(0xFFFC, bytes[0]);
        WriteMemoryValue(0xFFFD, bytes[1]);

        // Reset the CPU
        Reset();
    }

    /// <summary>
    /// Loads a program into the processors memory
    /// </summary>
    /// <param name="offset">The offset in memory when loading the program.</param>
    /// <param name="program">The program to be loaded</param>
    public void LoadProgram(int offset, byte[] program)
    {
        if (offset > Memory.Length)
            throw new InvalidOperationException("Offset '{0}' is larger than memory size '{1}'");

        if (program.Length > Memory.Length + offset)
            throw new InvalidOperationException(
                $"Program Size '{program.Length.ToString()}' Cannot be Larger than Memory Size '{Memory.Length.ToString()}' plus offset '{offset.ToString()}'");

        for (var i = 0; i < program.Length; i++) Memory[i + offset] = program[i];
    }
}
