using sim6502.Proc;

namespace sim6502.Expressions
{
    public class MemoryCompare : BaseCompare
    {
        public MemoryCompare(Processor proc) : base(proc)
        {
        }
        
        public ComparisonResult MemoryCmp(int source, int target, int count)
        {
            var res = new ComparisonResult();
            
            for (var i = 0; i < count; i++)
            {
                var sourceValue = Proc.ReadMemoryValueWithoutCycle(source + i);
                var targetValue = Proc.ReadMemoryValueWithoutCycle(target + i);

                if (sourceValue == targetValue) continue;
                
                res.FailureMessage = $"Expected values at memory locations {(source + i).ToString()} and " +
                    $"{(target + i).ToString()} to match, but {(source + i).ToString()} contains " +
                    $"{sourceValue.ToString()} and {(target + i).ToString()} contains {targetValue.ToString()}";
                res.ComparisonPassed = false;
                break;
            }

            return res;
        }

        public ComparisonResult MemoryChk(int source, int count, int value)
        {
            var res = new ComparisonResult();
            
            for (var i = source; i < source + count; i++)
            {
                var actualValue = Proc.ReadMemoryValueWithoutCycle(i);
                if (actualValue == value) continue;
                
                res.FailureMessage = $"Expected value at memory location {i.ToString()} to be {value.ToString()}, " +
                                     $"but the actual value was {actualValue.ToString()}";
                res.ComparisonPassed = false;
                break;
            }

            return res;
        }

        public ComparisonResult MemoryVal(int location, int value, string op = "==")
        {
            var actual = value > 255 ? Proc.ReadMemoryWordWithoutCycle(location) : 
                Proc.ReadMemoryValueWithoutCycle(location);

            var res = CompareValues(value, actual, op);
            
            if (!res.ComparisonPassed)
            {
                res.FailureMessage = $"Expected the value in memory location {location.ToString()} to be {op} " +
                                     $"{value.ToString()}, but the actual value was {actual.ToString()}";
            }
            
            return res;
        }
    }
}