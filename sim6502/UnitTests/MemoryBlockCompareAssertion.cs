namespace sim6502.UnitTests
{
    /// <summary>
    /// Compare blocks of memory to ensure they're identical
    /// </summary>
    public class MemoryBlockCompareAssertion : BaseAssertion
    {
        
        /// <summary>
        /// Test a memory location or a memory word and compare it to an expected value
        /// </summary>
        /// <param name="proc">A reference to the running 6502</param>
        /// <param name="expr">A reference to our expression parser</param>
        /// <param name="test">The current test that's running</param>
        /// <param name="assertion">The current assertion within the test that we'd like to test</param>
        /// <returns>True if the assertion passed, False otherwise</returns>
        public override bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test, TestAssertion assertion)
        {
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
                
                WriteFailureMessage($"Expected '{sourceValue.ToString()}' at location '{(targetAddress + i).ToString()}', but got '{sourceValue.ToString()}'", test,
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