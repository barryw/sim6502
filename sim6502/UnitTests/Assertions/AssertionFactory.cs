/*
Copyright (c) 2020 Barry Walker. All rights reserved.

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

using System.ComponentModel;
using sim6502.Utilities;

namespace sim6502.UnitTests.Assertions
{
    public static class AssertionFactory
    {
        public static BaseAssertion GetAssertionClass(TestAssertion assertion)
        {
            if (assertion.AssertionType.Empty()) return new NullAssertion();

            var assertionType = assertion.AssertionType.ToLower();

            return assertionType switch
            {
                "memory_block_compare" => (BaseAssertion) new MemoryBlockCompareAssertion(),
                "memory_test" => new MemoryTestAssertion(),
                "memory_block" => new MemoryBlockAssertion(),
                "processor_register" => new ProcessorRegisterAssertion(),
                "cycle_count" => new CycleCountAssertion(),
                _ => throw new InvalidEnumArgumentException(
                    $"Invalid assertion type '{assertionType}' found for assertion '{assertion.Description}'")
            };
        }
    }
}