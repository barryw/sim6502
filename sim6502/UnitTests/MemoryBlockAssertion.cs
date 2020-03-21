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

namespace sim6502.UnitTests
{
    /// <summary>
    /// Check a block of memory to make sure it's set to the specified value
    /// </summary>
    public class MemoryBlockAssertion : BaseAssertion
    {
        /// <summary>
        /// Perform a check of a block of memory and compare the entire range with a single value
        /// </summary>
        /// <param name="proc">A reference to the running 6502</param>
        /// <param name="expr">A reference to our expression parser</param>
        /// <param name="test">The current test that's running</param>
        /// <param name="assertion">The current assertion within the test that we'd like to test</param>
        /// <returns>True if the assertion passed, False otherwise</returns>
        public override bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test,
            TestAssertion assertion)
        {
            var passed = true;

            var assertionAddress = expr.Evaluate(assertion.Address);
            var byteCount = expr.Evaluate(assertion.ByteCount);
            var assertValue = assertion.AssertionValue(expr, test);
            var badMemoryValues = 0;

            for (var i = assertionAddress; i < assertionAddress + byteCount; i++)
            {
                var actualValue = proc.ReadMemoryValueWithoutCycle(i);
                if (actualValue == assertValue) continue;
                WriteFailureMessage($"Expected '{assertValue.ToString()}' at location '{i.ToString()}', but got '{actualValue.ToString()}'", test,
                    assertion);
                badMemoryValues++;
                passed = false;
            }

            if (badMemoryValues > 0)
                WriteFailureMessage(
                    string.Format(new PluralFormatProvider(),
                        "A total of {0:memory value;memory values} contain unexpected values", badMemoryValues), test,
                    assertion);

            return passed;
        }
    }
}