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

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies, 
either expressed or implied, of the FreeBSD Project.
*/

using System.IO;
using YamlDotNet.Serialization;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class TestAssertion
    {
        [YamlMember(Alias = "cycle_count", ApplyNamingConventions = false)]
        public string CycleCount { get; set; }

        [YamlMember(Alias = "register", ApplyNamingConventions = false)]
        public string Register { get; set; }

        [YamlMember(Alias = "byte_count", ApplyNamingConventions = false)]
        public string ByteCount { get; set; }

        [YamlMember(Alias = "description", ApplyNamingConventions = false)]
        public string Description { get; set; }

        [YamlMember(Alias = "address", ApplyNamingConventions = false)]
        public string Address { get; set; }

        [YamlMember(Alias = "op", ApplyNamingConventions = false)]
        public string Op { get; set; }

        [YamlMember(Alias = "word_value", ApplyNamingConventions = false)]
        public string WordValue { get; set; }

        [YamlMember(Alias = "byte_value", ApplyNamingConventions = false)]
        public string ByteValue { get; set; }

        /// <summary>
        /// Run one of a unit test's assertions
        /// </summary>
        /// <param name="proc">A reference to our running 6502</param>
        /// <param name="expr">A reference to our expression parser</param>
        /// <param name="test">A reference to our parent TestUnitTest object</param>
        /// <returns>True if the assertion passed, or False otherwise</returns>
        public bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test)
        {
            return AssertionFactory.GetAssertionClass(this).PerformAssertion(proc, expr, test, this);
        }

        /// <summary>
        /// Return the actual value from the processor
        /// </summary>
        /// <param name="proc">A reference to our running 6502</param>
        /// <param name="expr">A reference to our expression parser</param>
        /// <returns>The assertion's actual value (from the processor)</returns>
        public int ActualValue(Processor proc, ExpressionParser expr)
        {
            return !WordValue.Empty()
                ? proc.ReadMemoryWordWithoutCycle(expr.Evaluate(Address))
                : proc.ReadMemoryValueWithoutCycle(expr.Evaluate(Address));
        }

        /// <summary>
        /// Return the asserted value, which is what we expect the value to be
        /// </summary>
        /// <param name="expr"></param>
        /// <param name="test"></param>
        /// <returns>The asserted value</returns>
        public int AssertionValue(ExpressionParser expr, TestUnitTest test)
        {
            if (!WordValue.Empty() && !ByteValue.Empty())
            {
                throw new InvalidDataException(
                    $"Your tests can only assert either a 'word_value' or a 'byte_value' but not both. Failed on test '{test.Name}' assertion '{Description}'");
            }

            return expr.Evaluate(!WordValue.Empty() ? WordValue : ByteValue);
        }

        /// <summary>
        /// Do a comparison of actual and asserted values
        /// </summary>
        /// <param name="actualValue">The actual value from the processor</param>
        /// <param name="assertValue">The value that we expect it to be</param>
        /// <returns></returns>
        public ComparisonResult CompareValues(int actualValue, int assertValue)
        {
            var res = new ComparisonResult();

            switch (Op.ToLower())
            {
                case "eq":
                    if (actualValue != assertValue)
                    {
                        res.ComparisonPassed = false;
                        res.FailureMessage = $"Expected '{assertValue.ToString()}', but got '{actualValue.ToString()}'";
                    }

                    break;
                case "gt":
                    if (actualValue < assertValue)
                    {
                        res.ComparisonPassed = false;
                        res.FailureMessage = $"Expected '{actualValue.ToString()}' > '{assertValue.ToString()}'";
                    }

                    break;
                case "lt":
                    if (actualValue > assertValue)
                    {
                        res.ComparisonPassed = false;
                        res.FailureMessage = $"Expected '{actualValue.ToString()}' < '{assertValue.ToString()}'";
                    }

                    break;
                case "ne":
                    if (actualValue == assertValue)
                    {
                        res.ComparisonPassed = false;
                        res.FailureMessage = $"Expected '{assertValue.ToString()}' != '{actualValue.ToString()}'";
                    }

                    break;
                default:
                    res.ComparisonPassed = false;
                    res.FailureMessage = $"Invalid comparison operator '{Op}'. Valid operators are eq, ne, gt and lt";
                    break;
            }

            return res;
        }
    }
}