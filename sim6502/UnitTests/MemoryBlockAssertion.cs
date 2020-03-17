namespace sim6502.UnitTests
{
    /// <summary>
    /// Check a block of memory to make sure it's set to the specified value
    /// </summary>
    public class MemoryBlockAssertion : BaseAssertion
    {
        public override bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test, TestAssertion assertion)
        {
            var passed = true;
            
            var assertionAddress = expr.Evaluate(assertion.Address);
            var byteCount = expr.Evaluate(assertion.ByteCount);
            var assertValue = expr.Evaluate(assertion.ByteValue);
            var badMemoryValues = 0;
            
            for (var i = assertionAddress; i < assertionAddress + byteCount; i++)
            {
                var actualValue = proc.ReadMemoryValueWithoutCycle(i);
                if (actualValue == assertValue) continue;
                WriteFailureMessage($"Expected '{assertValue}' at location '{i}', but got '{actualValue}'", test, assertion);
                badMemoryValues++;
                passed = false;
            }
            
            if(badMemoryValues > 0)
                WriteFailureMessage(string.Format(new PluralFormatProvider(), "A total of {0:memory value;memory values} contain unexpected values", badMemoryValues), test, assertion);
            
            return passed;
        }
    }
}