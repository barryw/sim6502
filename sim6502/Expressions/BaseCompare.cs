using sim6502.Backend;

namespace sim6502.Expressions
{
    public class BaseCompare
    {
        protected readonly IExecutionBackend Backend;

        protected BaseCompare(IExecutionBackend backend)
        {
            Backend = backend;
        }
        
        public static ComparisonResult CompareValues(int expected, int actual, string op)
        {
            var res = new ComparisonResult
            {
                ComparisonPassed = op switch
                {
                    "==" => (expected == actual),
                    "eq" => (expected == actual),
                    "!=" => (expected != actual),
                    "<>" => (expected != actual),
                    "ne" => (expected != actual),
                    ">" => (expected > actual),
                    ">=" => (expected >= actual),
                    "<" => (expected < actual),
                    "<=" => (expected <= actual),
                    "lt" => (expected < actual),
                    "gt" => (expected > actual),
                    _ => false
                }
            };


            if (!res.ComparisonPassed)
                res.FailureMessage = $"Expected {expected.ToString()} {op} {actual.ToString()}";
            
            return res;
        }
    }
}