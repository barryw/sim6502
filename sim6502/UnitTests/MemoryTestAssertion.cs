using System.IO;

namespace sim6502.UnitTests
{
    /// <summary>
    /// Does a memory compare assertion
    /// </summary>
    public class MemoryTestAssertion : BaseAssertion
    {
        public override bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test, TestAssertion assertion)
        {
            var passed = true;

            var assertionAddress = expr.Evaluate(assertion.Address);
            var wordValue = assertion.WordValue;
            var byteValue = assertion.ByteValue;

            if (!"".Equals(wordValue) && wordValue != null && !"".Equals(byteValue) && byteValue != null)
            {
                throw new InvalidDataException($"Your tests can only assert either a 'word_value' or a 'byte_value' but not both. Failed on test '{test.Name}' assertion '{assertion.Description}'");
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
                        
            var op = assertion.Op;
            switch (op.ToLower())
            {
                case "eq":
                    if (actualValue != assertValue)
                    {
                        WriteFailureMessage($"Expected '{assertValue}', but got '{actualValue}'", test, assertion);
                        passed = false;   
                    }
                    break;
                case "gt":
                    if (actualValue < assertValue)
                    {
                        WriteFailureMessage($"Expected '{actualValue}' > '{assertValue}'", test, assertion);
                        passed = false;
                    }
                    break;
                case "lt":
                    if (actualValue > assertValue)
                    {
                        WriteFailureMessage($"Expected '{actualValue}' < '{assertValue}'", test, assertion);
                    }
                    break;
                case "ne":
                    if (actualValue == assertValue)
                    {
                        WriteFailureMessage($"Expected '{assertValue}' != '{actualValue}'", test, assertion);
                        passed = false;
                    }
                    break;
                default:
                    WriteFailureMessage($"Invalid comparison operator '{op}'. Valid operators are eq, ne, gt and lt", test, assertion);
                    passed = false;
                    break;
            }

            return passed;
        }
    }
}