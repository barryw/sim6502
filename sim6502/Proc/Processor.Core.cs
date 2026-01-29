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
using sim6502.Systems;

namespace sim6502.Proc;

/// <summary>
/// An Implementation of a 6502 Processor - Core State and Properties
/// </summary>
public partial class Processor
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
    private int _programCounter;
    private int _stackPointer;
    private bool _previousInterrupt;
    private bool _interrupt;

    // All of the properties here are public and read only to facilitate ease of debugging and testing.

    #region Properties

    /// <summary>
    /// The processor variant being emulated.
    /// </summary>
    public ProcessorType ProcessorType { get; }

    /// <summary>
    /// Our 64k address space
    /// </summary>
    protected byte[] Memory { get; private set; }

    /// <summary>
    /// The Accumulator. This value is implemented as an integer instead of a byte.
    /// This is done so we can detect wrapping of the value and set the correct number of cycles.
    /// </summary>
    public int Accumulator { get; set; }

    /// <summary>
    /// The X Index Register
    /// </summary>
    public int XRegister { get; set; }

    /// <summary>
    /// The Y Index Register
    /// </summary>
    public int YRegister { get; set; }

    /// <summary>
    /// The Current Op Code being executed by the system
    /// </summary>
    public int CurrentOpCode { get; private set; }

    /// <summary>
    /// The disassembly of the current operation. This value is only set when the CPU is built in debug mode.
    /// </summary>
    public Disassembly CurrentDisassembly { get; private set; }

    /// <summary>
    /// Points to the Current Address of the instruction being executed by the system.
    /// The PC wraps when the value is greater than 65535, or less than 0.
    /// </summary>
    public int ProgramCounter
    {
        get => _programCounter;
        set => _programCounter = WrapProgramCounter(value);
    }

    /// <summary>
    /// Points to the Current Position of the Stack.
    /// This value is a 00-FF value but is offset to point to the location in memory where the stack resides.
    /// </summary>
    public int StackPointer
    {
        get => _stackPointer;
        set
        {
            if (value > 0xFF)
                _stackPointer = value - 0x100;
            else if (value < 0x00)
                _stackPointer = value + 0x100;
            else
                _stackPointer = value;
        }
    }

    /// <summary>
    /// An external action that occurs when the cycle count is incremented
    /// </summary>
    public Action CycleCountIncrementedAction { get; set; }

    //Status Registers
    /// <summary>
    /// This is the carry flag. when adding, if the result is greater than 255 or 99 in BCD Mode, then this bit is enabled.
    /// In subtraction this is reversed and set to false if a borrow is required IE the result is less than 0
    /// </summary>
    public bool CarryFlag { get; set; }

    /// <summary>
    /// Is true if one of the registers is set to zero.
    /// </summary>
    public bool ZeroFlag { get; set; }

    /// <summary>
    /// This determines if Interrupts are currently disabled.
    /// This flag is turned on during a reset to prevent an interrupt from occuring during startup/Initialization.
    /// If this flag is true, then the IRQ pin is ignored.
    /// </summary>
    public bool DisableInterruptFlag { get; set; }

    /// <summary>
    /// Binary Coded Decimal Mode is set/cleared via this flag.
    /// when this mode is in effect, a byte represents a number from 0-99.
    /// </summary>
    public bool DecimalFlag { get; set; }

    /// <summary>
    /// This property is set when an overflow occurs. An overflow happens if the high bit(7) changes during the operation. Remember that values from 128-256 are negative values
    /// as the high bit is set to 1.
    /// Examples:
    /// 64 + 64 = -128
    /// -128 + -128 = 0
    /// </summary>
    public bool OverflowFlag { get; set; }

    /// <summary>
    /// Set to true if the result of an operation is negative in ADC and SBC operations.
    /// Remember that 128-256 represent negative numbers when doing signed math.
    /// In shift operations the sign holds the carry.
    /// </summary>
    public bool NegativeFlag { get; set; }

    /// <summary>
    /// Set to true when an NMI should occur
    /// </summary>
    public bool TriggerNmi { get; set; }

    /// Set to true when an IRQ has occurred and is being processed by the CPU
    public bool TriggerIrq { get; private set; }

    /// <summary>
    /// Gets the Number of Cycles that have elapsed
    /// </summary>
    /// <value>The number of elapsed cycles</value>
    public int CycleCount { get; private set; }

    /// <summary>
    /// Enables trace buffering for execution trace
    /// </summary>
    public bool TraceEnabled { get; set; }

    /// <summary>
    /// Buffered trace lines for failure-only output
    /// </summary>
    private readonly System.Collections.Generic.List<string> _traceBuffer = new();

    /// <summary>
    /// The memory map implementation for this processor.
    /// Allows system-specific memory banking and ROM overlays.
    /// </summary>
    public IMemoryMap? MemoryMap { get; private set; }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Increments the cycle count by 1 and executes the CycleCountIncrementedAction
    /// </summary>
    protected void IncrementCycleCount()
    {
        CycleCount++;
        CycleCountIncrementedAction();

        _previousInterrupt = _interrupt;
        _interrupt = TriggerNmi || TriggerIrq && !DisableInterruptFlag;
    }

    /// <summary>
    /// Resets the Cycle Count back to 0
    /// </summary>
    public void ResetCycleCount()
    {
        CycleCount = 0;
    }

    /// <summary>
    /// Gets the current trace buffer contents
    /// </summary>
    /// <returns>List of buffered trace lines</returns>
    public System.Collections.Generic.List<string> GetTraceBuffer()
    {
        return _traceBuffer;
    }

    /// <summary>
    /// Clears the trace buffer
    /// </summary>
    public void ClearTraceBuffer()
    {
        _traceBuffer.Clear();
    }

    /// <summary>
    /// Sets the NegativeFlag register
    /// </summary>
    /// <param name="value"></param>
    protected void SetNegativeFlag(int value)
    {
        //on the 6502, any value greater than 127 is negative. 128 = 1000000 in Binary. the 8th bit is set, therefore the number is a negative number.
        NegativeFlag = value > 127;
    }

    /// <summary>
    /// Sets the IsResultZero register
    /// </summary>
    /// <param name="value"></param>
    protected void SetZeroFlag(int value)
    {
        ZeroFlag = value == 0;
    }

    /// <summary>
    /// Converts the processor flags into a byte for storage on the stack
    /// </summary>
    /// <param name="setBreak">Whether to set the break flag</param>
    /// <returns>A byte representation of the processor flags</returns>
    private byte ConvertFlagsToByte(bool setBreak)
    {
        return (byte) ((CarryFlag ? 0x01 : 0) + (ZeroFlag ? 0x02 : 0) + (DisableInterruptFlag ? 0x04 : 0) +
                       (DecimalFlag ? 8 : 0) + (setBreak ? 0x10 : 0) + 0x20 + (OverflowFlag ? 0x40 : 0) +
                       (NegativeFlag ? 0x80 : 0));
    }

    /// <summary>
    /// Wraps the program counter to ensure it stays within the valid 16-bit address range
    /// </summary>
    /// <param name="value">The value to wrap</param>
    /// <returns>The wrapped value (0-65535)</returns>
    private int WrapProgramCounter(int value)
    {
        return value & 0xFFFF;
    }

    /// <summary>
    /// Gets the display name for the current processor type
    /// </summary>
    /// <returns>The processor name</returns>
    private string GetProcessorName() => ProcessorType switch
    {
        ProcessorType.MOS6502 => "6502",
        ProcessorType.MOS6510 => "6510",
        ProcessorType.WDC65C02 => "65C02",
        _ => "6502"
    };

    #endregion
}
