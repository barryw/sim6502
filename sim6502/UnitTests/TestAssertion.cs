using System.IO;
using NLog;
using YamlDotNet.Serialization;
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class TestAssertion
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
        
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
        /// <exception cref="InvalidDataException"></exception>
        public bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test)
        {
            var passed = true;

            var assertionAddress = expr.Evaluate(Address);
            var wordValue = WordValue;
            var byteValue = ByteValue;

            if (!"".Equals(wordValue) && wordValue != null && !"".Equals(byteValue) && byteValue != null)
            {
                throw new InvalidDataException($"Your tests can only assert either a 'word_value' or a 'byte_value' but not both. Failed on test '{test.Name}' assertion '{Description}'");
            }

            int assertValue;
            int actualValue;
            if (!"".Equals(wordValue) && wordValue != null)
            {
                assertValue = expr.Evaluate(wordValue);
                actualValue = proc.ReadMemoryWordWithoutCycle(assertionAddress);
            }
            else
            {
                assertValue = expr.Evaluate(byteValue);
                actualValue = proc.ReadMemoryValueWithoutCycle(assertionAddress);
            }
                        
            var op = Op;
            switch (op.ToLower())
            {
                case "eq":
                    if (actualValue != assertValue)
                    {
                        WriteFailureMessage($"Expected '{assertValue}', but got '{actualValue}'", test);
                        passed = false;   
                    }
                    break;
                case "gt":
                    if (actualValue < assertValue)
                    {
                        WriteFailureMessage($"Expected '{actualValue}' > '{assertValue}'", test);
                        passed = false;
                    }
                    break;
                case "lt":
                    if (actualValue > assertValue)
                    {
                        WriteFailureMessage($"Expected '{actualValue}' < '{assertValue}'", test);
                    }
                    break;
                case "ne":
                    if (actualValue == assertValue)
                    {
                        WriteFailureMessage($"Expected '{assertValue}' != '{actualValue}'", test);
                        passed = false;
                    }
                    break;
                default:
                    WriteFailureMessage($"Invalid comparison operator '{op}'. Valid operators are eq, ne, gt and lt", test);
                    passed = false;
                    break;
            }

            return passed;
        }

        private void WriteFailureMessage(string message, TestUnitTest test)
        {
            Logger.Fatal($"{message} for '{Description}' assertion of test '{test.Name}'");
        }
    }
}