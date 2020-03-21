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

namespace sim6502.UnitTests.Assertions
{
    public class CycleCountAssertion : BaseAssertion
    {
        /// <summary>
        /// Verify that cycle counts are what we expect
        /// </summary>
        /// <param name="proc">A reference to the running 6502</param>
        /// <param name="expr">A reference to our expression parser</param>
        /// <param name="test">The current test that's running</param>
        /// <param name="assertion">The current assertion within the test that we'd like to test</param>
        /// <returns>True if the assertion passed, False otherwise</returns>
        public override bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test,
            TestAssertion assertion)
        {
            var actualValue = proc.CycleCount;
            var assertValue = expr.Evaluate(assertion.CycleCount, test, assertion);
            if (assertValue == -1)
                return false;

            var res = assertion.CompareValues(actualValue, assertValue, test);
            if (!res.ComparisonPassed)
                WriteFailureMessage(res.FailureMessage, test, assertion);

            return res.ComparisonPassed;
        }
    }
}