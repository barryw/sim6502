namespace sim6502.UnitTests
{
    /// <summary>
    /// Does a memory compare assertion
    /// </summary>
    public class MemoryTestAssertion : BaseAssertion
    {
        public override bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test, TestAssertion assertion)
        {
            var actualValue = assertion.ActualValue(proc, expr);
            var assertValue = assertion.AssertionValue(expr, test);
            
            var res = assertion.CompareValues(actualValue, assertValue, proc, expr, test);
            if(!res.ComparisonPassed)
                WriteFailureMessage(res.FailureMessage, test, assertion);

            return res.ComparisonPassed;
        }
    }
}