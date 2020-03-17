using NLog;

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
            var byteValue = expr.Evaluate(assertion.ByteValue);

            for (var i = assertionAddress; i <= assertionAddress + byteCount; i++)
            {
                var val = proc.ReadMemoryValueWithoutCycle(i);
                if (val != byteValue)
                    passed = false;
            }
            
            return passed;
        }
    }
}