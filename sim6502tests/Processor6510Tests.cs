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

/// <summary>
/// Tests for 6510-specific I/O port functionality at addresses $00 and $01
/// </summary>
public class Processor6510Tests
{
    [Fact]
    public void IO_Port_DDR_Read_Returns_DataDirection()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6510);
        processor.ResetMemory();
        processor.LoadProgram(0x0000, new byte[] { 0xA5, 0x00 }, 0x0000); // LDA $00
        processor.Reset();

        // Set DDR via WriteMemoryValueWithoutIncrement to avoid cycle increment
        processor.WriteMemoryValueWithoutIncrement(0x00, 0x3F);

        // Act
        var result = processor.ReadMemoryValueWithoutCycle(0x00);

        // Assert
        result.Should().Be(0x3F, "reading from $00 should return the DDR value");
    }

    [Fact]
    public void IO_Port_DDR_Write_Sets_DataDirection()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6510);
        processor.ResetMemory();

        // Act
        processor.WriteMemoryValueWithoutIncrement(0x00, 0xFF);
        var result = processor.ReadMemoryValueWithoutCycle(0x00);

        // Assert
        result.Should().Be(0xFF, "writing to $00 should set the DDR");
    }

    [Fact]
    public void IO_Port_DataPort_Read_Returns_DataPort()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6510);
        processor.ResetMemory();
        processor.LoadProgram(0x0000, new byte[] { 0xA5, 0x01 }, 0x0000); // LDA $01
        processor.Reset();

        // Set data port via WriteMemoryValueWithoutIncrement
        processor.WriteMemoryValueWithoutIncrement(0x01, 0x07);

        // Act
        var result = processor.ReadMemoryValueWithoutCycle(0x01);

        // Assert
        result.Should().Be(0x07, "reading from $01 should return the data port value");
    }

    [Fact]
    public void IO_Port_DataPort_Write_Sets_DataPort()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6510);
        processor.ResetMemory();

        // Act
        processor.WriteMemoryValueWithoutIncrement(0x01, 0x37);
        var result = processor.ReadMemoryValueWithoutCycle(0x01);

        // Assert
        result.Should().Be(0x37, "writing to $01 should set the data port");
    }

    [Fact]
    public void IO_Port_Reset_Clears_DDR_And_DataPort()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6510);
        processor.ResetMemory();
        processor.WriteMemoryValue(0xFFFC, 0x00);
        processor.WriteMemoryValue(0xFFFD, 0x10);

        // Set both registers to non-zero values
        processor.WriteMemoryValueWithoutIncrement(0x00, 0xFF);
        processor.WriteMemoryValueWithoutIncrement(0x01, 0xFF);

        // Act
        processor.Reset();

        // Assert
        processor.ReadMemoryValueWithoutCycle(0x00).Should().Be(0x00, "Reset should clear DDR to $00");
        processor.ReadMemoryValueWithoutCycle(0x01).Should().Be(0x00, "Reset should clear data port to $00");
    }

    [Fact]
    public void IO_Port_Multiple_Writes_Updates_Registers()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6510);
        processor.ResetMemory();

        // Act - Write multiple values
        processor.WriteMemoryValueWithoutIncrement(0x00, 0x11);
        var ddr1 = processor.ReadMemoryValueWithoutCycle(0x00);

        processor.WriteMemoryValueWithoutIncrement(0x00, 0x22);
        var ddr2 = processor.ReadMemoryValueWithoutCycle(0x00);

        processor.WriteMemoryValueWithoutIncrement(0x01, 0x33);
        var port1 = processor.ReadMemoryValueWithoutCycle(0x01);

        processor.WriteMemoryValueWithoutIncrement(0x01, 0x44);
        var port2 = processor.ReadMemoryValueWithoutCycle(0x01);

        // Assert
        ddr1.Should().Be(0x11);
        ddr2.Should().Be(0x22);
        port1.Should().Be(0x33);
        port2.Should().Be(0x44);
    }

    [Fact]
    public void MOS6502_Treats_Address_00_As_Normal_Memory()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6502);
        processor.ResetMemory();

        // Act
        processor.WriteMemoryValueWithoutIncrement(0x00, 0xAB);
        var result = processor.ReadMemoryValueWithoutCycle(0x00);

        // Assert
        result.Should().Be(0xAB, "6502 should treat $00 as normal memory");
    }

    [Fact]
    public void MOS6502_Treats_Address_01_As_Normal_Memory()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6502);
        processor.ResetMemory();

        // Act
        processor.WriteMemoryValueWithoutIncrement(0x01, 0xCD);
        var result = processor.ReadMemoryValueWithoutCycle(0x01);

        // Assert
        result.Should().Be(0xCD, "6502 should treat $01 as normal memory");
    }

    [Fact]
    public void WDC65C02_Treats_Address_00_As_Normal_Memory()
    {
        // Arrange
        var processor = new Processor(ProcessorType.WDC65C02);
        processor.ResetMemory();

        // Act
        processor.WriteMemoryValueWithoutIncrement(0x00, 0xEF);
        var result = processor.ReadMemoryValueWithoutCycle(0x00);

        // Assert
        result.Should().Be(0xEF, "65C02 should treat $00 as normal memory");
    }

    [Fact]
    public void WDC65C02_Treats_Address_01_As_Normal_Memory()
    {
        // Arrange
        var processor = new Processor(ProcessorType.WDC65C02);
        processor.ResetMemory();

        // Act
        processor.WriteMemoryValueWithoutIncrement(0x01, 0x12);
        var result = processor.ReadMemoryValueWithoutCycle(0x01);

        // Assert
        result.Should().Be(0x12, "65C02 should treat $01 as normal memory");
    }

    [Fact]
    public void IO_Port_ReadMemoryValue_Returns_DDR_With_Cycle_Increment()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6510);
        processor.ResetMemory();
        processor.WriteMemoryValueWithoutIncrement(0x00, 0x55);
        var initialCycles = processor.CycleCount;

        // Act
        var result = processor.ReadMemoryValue(0x00);

        // Assert
        result.Should().Be(0x55);
        processor.CycleCount.Should().Be(initialCycles + 1, "reading DDR should increment cycle count");
    }

    [Fact]
    public void IO_Port_ReadMemoryValue_Returns_DataPort_With_Cycle_Increment()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6510);
        processor.ResetMemory();
        processor.WriteMemoryValueWithoutIncrement(0x01, 0xAA);
        var initialCycles = processor.CycleCount;

        // Act
        var result = processor.ReadMemoryValue(0x01);

        // Assert
        result.Should().Be(0xAA);
        processor.CycleCount.Should().Be(initialCycles + 1, "reading data port should increment cycle count");
    }

    [Fact]
    public void IO_Port_WriteMemoryValue_Sets_DDR()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6510);
        processor.ResetMemory();

        // Act
        processor.WriteMemoryValue(0x00, 0x77);

        // Assert - Verify the value was written correctly
        processor.ReadMemoryValueWithoutCycle(0x00).Should().Be(0x77, "writing to $00 should set the DDR");
    }

    [Fact]
    public void IO_Port_WriteMemoryValue_Sets_DataPort()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6510);
        processor.ResetMemory();

        // Act
        processor.WriteMemoryValue(0x01, 0x88);

        // Assert - Verify the value was written correctly
        processor.ReadMemoryValueWithoutCycle(0x01).Should().Be(0x88, "writing to $01 should set the data port");
    }

    [Fact]
    public void IO_Port_DDR_And_DataPort_Are_Independent()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6510);
        processor.ResetMemory();

        // Act
        processor.WriteMemoryValueWithoutIncrement(0x00, 0x1A);
        processor.WriteMemoryValueWithoutIncrement(0x01, 0x2B);

        // Assert
        processor.ReadMemoryValueWithoutCycle(0x00).Should().Be(0x1A, "DDR should maintain its own value");
        processor.ReadMemoryValueWithoutCycle(0x01).Should().Be(0x2B, "Data port should maintain its own value");
    }

    [Fact]
    public void IO_Port_Does_Not_Affect_Normal_Memory()
    {
        // Arrange
        var processor = new Processor(ProcessorType.MOS6510);
        processor.ResetMemory();

        // Act - Write to I/O ports and normal memory
        processor.WriteMemoryValueWithoutIncrement(0x00, 0xAA);
        processor.WriteMemoryValueWithoutIncrement(0x01, 0xBB);
        processor.WriteMemoryValueWithoutIncrement(0x02, 0xCC); // Normal memory

        // Assert - Normal memory unaffected
        processor.ReadMemoryValueWithoutCycle(0x02).Should().Be(0xCC, "normal memory should be unaffected by I/O port operations");
    }
}
