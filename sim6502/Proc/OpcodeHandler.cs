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
/// Delegate for opcode handler functions
/// </summary>
public delegate void OpcodeHandler(Processor processor);

/// <summary>
/// Metadata about a 6502 opcode including its handler
/// </summary>
/// <param name="Opcode">The opcode byte value (0x00-0xFF)</param>
/// <param name="Mnemonic">The 3-letter instruction mnemonic (e.g., "ADC", "LDA")</param>
/// <param name="AddressingMode">The addressing mode for this opcode variant</param>
/// <param name="Bytes">Number of bytes for this instruction (1-3)</param>
/// <param name="BaseCycles">Base cycle count (may vary with page crossing)</param>
/// <param name="Handler">The delegate that executes this opcode</param>
public record OpcodeInfo(
    byte Opcode,
    string Mnemonic,
    AddressingMode AddressingMode,
    int Bytes,
    int BaseCycles,
    OpcodeHandler Handler
);
