using sim6502.Expressions;
using sim6502.Proc;
using sim6502.Utilities;

namespace sim6502.UnitTests.Assertions
{
    public class MemoryBlockCompareAssertion : BaseAssertion
    {
        public override bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test,
            TestAssertion assertion)
        {
            Logger.Trace($"Using MemoryBlockCompareAssertion for {assertion.Description}");
            
            var passed = true;

            var sourceAddress = expr.Evaluate(assertion.Address, test, assertion);
            var targetAddress = expr.Evaluate(assertion.Target, test, assertion);
            var byteCount = expr.Evaluate(assertion.ByteCount, test, assertion);
            var badMemoryValues = 0;

            for (var i = 0; i < byteCount; i++)
            {
                var sourceValue = proc.ReadMemoryValueWithoutCycle(sourceAddress + i);
                var targetValue = proc.ReadMemoryValueWithoutCycle(targetAddress + i);

                if (sourceValue == targetValue) continue;

                WriteFailureMessage(
                    $"Expected '{sourceValue.ToString()}' at location '{(targetAddress + i).ToString()}', but got '{sourceValue.ToString()}'",
                    test,
                    assertion);
                badMemoryValues++;
                passed = false;
            }

            if (badMemoryValues > 0)
                WriteFailureMessage(
                    string.Format(new PluralFormatProvider(),
                        "A total of {0:memory value;memory values} contain incorrect values", badMemoryValues), test,
                    assertion);

            return passed;
        }
    }
}