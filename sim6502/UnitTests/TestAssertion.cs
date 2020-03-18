using System.IO;
using YamlDotNet.Serialization;
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class TestAssertion
    {
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
        /// Return the actual value
        /// </summary>
        /// <param name="proc"></param>
        /// <param name="expr"></param>
        /// <returns></returns>
        public int ActualValue(Processor proc, ExpressionParser expr)
        {
            return !WordValue.Empty()
                ? proc.ReadMemoryWordWithoutCycle(expr.Evaluate(Address))
                : proc.ReadMemoryValueWithoutCycle(expr.Evaluate(Address));
        }
        
        /// <summary>
        /// Return the asserted value
        /// </summary>
        /// <param name="expr"></param>
        /// <param name="test"></param>
        /// <returns></returns>
        public int AssertionValue(ExpressionParser expr, TestUnitTest test)
        {
            if (!WordValue.Empty() && !ByteValue.Empty())
            {
                throw new InvalidDataException($"Your tests can only assert either a 'word_value' or a 'byte_value' but not both. Failed on test '{test.Name}' assertion '{Description}'");
            }
            
            return expr.Evaluate(!WordValue.Empty() ? WordValue : ByteValue);
        }

        /// <summary>
        /// Do a comparison of actual and asserted values
        /// </summary>
        /// <param name="actualValue"></param>
        /// <param name="assertValue"></param>
        /// <param name="proc"></param>
        /// <param name="expr"></param>
        /// <param name="test"></param>
        /// <returns></returns>
        public ComparisonResult CompareValues(int actualValue, int assertValue, Processor proc, ExpressionParser expr, TestUnitTest test)
        {
            var res = new ComparisonResult();
            
            switch (Op.ToLower())
            {
                case "eq":
                    if (actualValue != assertValue)
                    {
                        res.ComparisonPassed = false;
                        res.FailureMessage = $"Expected '{assertValue}', but got '{actualValue}'";
                    }
                    break;
                case "gt":
                    if (actualValue < assertValue)
                    {
                        res.ComparisonPassed = false;
                        res.FailureMessage = $"Expected '{actualValue}' > '{assertValue}'";
                    }
                    break;
                case "lt":
                    if (actualValue > assertValue)
                    {
                        res.ComparisonPassed = false;
                        res.FailureMessage = $"Expected '{actualValue}' < '{assertValue}'";
                    }
                    break;
                case "ne":
                    if (actualValue == assertValue)
                    {
                        res.ComparisonPassed = false;
                        res.FailureMessage = $"Expected '{assertValue}' != '{actualValue}'";
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