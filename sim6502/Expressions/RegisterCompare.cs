using sim6502.Proc;

namespace sim6502.Expressions
{
    public class RegisterCompare : BaseCompare
    {
        public RegisterCompare(Processor proc) : base(proc)
        {
        }

        public ComparisonResult Compare(string register, int value, string op = "==")
        {
            var actual = 0;
            actual = register switch
            {
                "a" => Proc.Accumulator,
                "x" => Proc.XRegister,
                "y" => Proc.YRegister,
                "pc" => Proc.ProgramCounter,
                "s" => Proc.StackPointer,
                "n" => FlagToInt(Proc.NegativeFlag),
                "d" => FlagToInt(Proc.DecimalFlag),
                "v" => FlagToInt(Proc.OverflowFlag),
                "z" => FlagToInt(Proc.ZeroFlag),
                "c" => FlagToInt(Proc.CarryFlag),
                _ => actual
            };

            var res = CompareValues(value, actual, op);
            if (!res.ComparisonPassed)
            {
                res.FailureMessage = $"Expected {register} A to have a value {op} {value.ToString()}, but was {actual.ToString()}.";
            }

            return res;
        }
        
        private static int FlagToInt(bool flag)
        {
            return flag ? 1 : 0;
        }
    }
}