namespace sim6502.UnitTests
{
    public class ProcessorRegisterAssertion : BaseAssertion
    {
        public override bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test, TestAssertion assertion)
        {
            var register = assertion.Register.ToLower();
            
            var registerValue = 0;

            switch (register)
            {
                case "a":
                    registerValue = proc.Accumulator;
                    break;
                case "x":
                    registerValue = proc.XRegister;
                    break;
                case "y":
                    registerValue = proc.YRegister;
                    break;
                case "pc":
                    registerValue = proc.ProgramCounter;
                    break;
                case "s":
                    registerValue = proc.StackPointer;
                    break;
                case "p":
                    // TODO
                    break;
                default:
                    WriteFailureMessage($"{register} is not a valid register value. Valid values are a, x, y, pc, s, p", test, assertion);
                    return false;
            }

            var assertValue = assertion.AssertionValue(expr, test);
            var res = assertion.CompareValues(registerValue, assertValue, proc, expr, test);
            if(!res.ComparisonPassed)
                WriteFailureMessage(res.FailureMessage, test, assertion);

            return res.ComparisonPassed;
        }
    }
}