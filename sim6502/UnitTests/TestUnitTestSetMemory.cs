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

using sim6502.Expressions;
using sim6502.Proc;
using sim6502.Utilities;
using YamlDotNet.Serialization;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable MemberCanBePrivate.Global

namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class TestUnitTestSetMemory
    {
        [YamlMember(Alias = "description", ApplyNamingConventions = false)]
        public string Description { get; set; }

        [YamlMember(Alias = "address", ApplyNamingConventions = false)]
        public string Address { get; set; }

        [YamlMember(Alias = "byte_value", ApplyNamingConventions = false)]
        public string ByteValue { get; set; }

        [YamlMember(Alias = "word_value", ApplyNamingConventions = false)]
        public string WordValue { get; set; }

        /// <summary>
        /// Set a value on a memory location
        /// </summary>
        /// <param name="proc">A reference to the running 6502</param>
        /// <param name="expr">A reference to our expression parser</param>
        /// <param name="test">The current running test</param>
        public void SetMemory(Processor proc, ExpressionParser expr, TestUnitTest test)
        {
            var location = expr.Evaluate(Address, test, null);
            if (location == -1)
                return;
            
            if (!WordValue.Empty())
            {
                var wordValue = expr.Evaluate(WordValue, test, null);
                proc.WriteMemoryWord(location, wordValue);
            }
            else
            {
                var byteValue = expr.Evaluate(ByteValue, test, null);
                proc.WriteMemoryValueWithoutIncrement(location, (byte) byteValue);
            }
        }
    }
}