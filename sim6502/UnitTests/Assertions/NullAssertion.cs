using sim6502.Expressions;
using sim6502.Proc;

namespace sim6502.UnitTests.Assertions
{
    /// <summary>
    /// Always returns true. This is only used if an 'assertion_type' isn't specified
    /// </summary>
    public class NullAssertion : BaseAssertion
    {
        public override bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test,
            TestAssertion assertion)
        {
            Logger.Warn(
                $"Missing assertion_type for assertion '{assertion.Description}' of test '{test.Name}'. Will use the null assertion check which always returns true.");
            return true;
        }
    }
}