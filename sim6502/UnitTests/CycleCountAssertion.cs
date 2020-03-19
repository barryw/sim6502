namespace sim6502.UnitTests
{
    public class CycleCountAssertion : BaseAssertion
    {
        /// <summary>
        /// Verify that cycle counts are what we expect
        /// </summary>
        /// <param name="proc">A reference to the running 6502</param>
        /// <param name="expr">A reference to our expression parser</param>
        /// <param name="test">The current test that's running</param>
        /// <param name="assertion">The current assertion within the test that we'd like to test</param>
        /// <returns>True if the assertion passed, False otherwise</returns>
        public override bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test,
            TestAssertion assertion)
        {
            var actualValue = proc.CycleCount;
            var assertValue = expr.Evaluate(assertion.CycleCount);

            var res = assertion.CompareValues(actualValue, assertValue, expr, test);
            if (!res.ComparisonPassed)
                WriteFailureMessage(res.FailureMessage, test, assertion);

            return res.ComparisonPassed;
        }
    }
}