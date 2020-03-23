using System.IO;
using sim6502.Expressions;
using sim6502.Proc;
using sim6502.Utilities;
using YamlDotNet.Serialization;
// ReSharper disable ClassNeverInstantiated.Global

namespace sim6502.UnitTests
{
    public class TestUnitTestSetRegisters
    {
        [YamlMember(Alias = "description", ApplyNamingConventions = false)]
        public string Description { get; set; }
        
        [YamlMember(Alias = "register", ApplyNamingConventions = false)]
        public string Register { get; set; }
        
        [YamlMember(Alias = "byte_value", ApplyNamingConventions = false)]
        public string ByteValue { get; set; }
        
        public void SetRegisters(Processor proc, ExpressionParser expr, TestUnitTest test)
        {
            var register = GetRegister();
            if (ByteValue.Empty())
            {
                throw new InvalidDataException($"You must specify a byte_value for the '{register}' register in test '{test.Name}'");
            }

            var value = expr.Evaluate(ByteValue, test, null);
            SetRegister(proc, register, value);
        }

        private static void SetRegister(Processor proc, string register, int value)
        {
            switch (register)
            {
                case "a":
                    proc.Accumulator = value;
                    break;
                case "x":
                    proc.XRegister = value;
                    break;
                case "y":
                    proc.YRegister = value;
                    break;
            }
        }
        
        private string GetRegister()
        {
            var register = Register.ToLower();
            if (register != "a" && register != "x" && register != "y")
            {
                throw new InvalidDataException($"Invalid register '{register}' specified. Must be one of a, x, y.");
            }

            return register;
        }
    }
}