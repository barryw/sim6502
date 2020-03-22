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

using System.IO;
using NLog;
using sim6502.Expressions;
using sim6502.Proc;
using sim6502.UnitTests.Assertions;
using sim6502.Utilities;
using YamlDotNet.Serialization;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class TestAssertion
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
        
        [YamlMember(Alias = "type", ApplyNamingConventions = false)]
        public string AssertionType { get; set; }
        
        [YamlMember(Alias = "cycle_count", ApplyNamingConventions = false)]
        public string CycleCount { get; set; }

        [YamlMember(Alias = "register", ApplyNamingConventions = false)]
        public string Register { get; set; }

        [YamlMember(Alias = "byte_count", ApplyNamingConventions = false)]
        public string ByteCount { get; set; }

        [YamlMember(Alias = "description", ApplyNamingConventions = false)]
        public string Description { get; set; }

        [YamlMember(Alias = "target", ApplyNamingConventions = false)]
        public string Target { get; set; }
        
        [YamlMember(Alias = "address", ApplyNamingConventions = false)]
        public string Address { get; set; }

        [YamlMember(Alias = "op", ApplyNamingConventions = false)]
        public string Op { get; set; }

        [YamlMember(Alias = "word_value", ApplyNamingConventions = false)]
        public string WordValue { get; set; }

        [YamlMember(Alias = "byte_value", ApplyNamingConventions = false)]
        public string ByteValue { get; set; }
        
        public bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test)
        {
            return AssertionFactory.GetAssertionClass(this).PerformAssertion(proc, expr, test, this);
        }

        public int ActualValue(Processor proc, ExpressionParser expr, TestUnitTest test)
        {
            var address = expr.Evaluate(Address, test, this);
            if (address == -1)
                return -1;
            
            return !WordValue.Empty()
                ? proc.ReadMemoryWordWithoutCycle(address)
                : proc.ReadMemoryValueWithoutCycle(address);
        }
        
        public int AssertionValue(ExpressionParser expr, TestUnitTest test)
        {
            if (!WordValue.Empty() && !ByteValue.Empty())
            {
                throw new InvalidDataException(
                    $"Your tests can only assert either a 'word_value' or a 'byte_value' but not both. Failed on test '{test.Name}' assertion '{Description}'");
            }

            return expr.Evaluate(!WordValue.Empty() ? WordValue : ByteValue, test, this);
        }
        
        public ComparisonResult CompareValues(int actualValue, int assertValue, TestUnitTest test)
        {
            var res = new ComparisonResult();

            if (Op.Empty())
            {
                Logger.Warn($"The 'op' attribute is unset for assertion '{Description}' on test '{test.Name}'. Assuming 'eq' (equal)");
                Op = "eq";
            }
            
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