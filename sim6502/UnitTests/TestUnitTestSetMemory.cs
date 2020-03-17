using YamlDotNet.Serialization;
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable MemberCanBePrivate.Global

namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class TestUnitTestSetMemory
    {
        [YamlMember(Alias = "description", ApplyNamingConventions = false)]
        public string Description { get; set; }
        [YamlMember(Alias = "address", ApplyNamingConventions = false)]
        public string Address { get; set; }
        [YamlMember(Alias = "byte_value", ApplyNamingConventions = false)]
        public string ByteValue { get; set; }
        [YamlMember(Alias = "word_value", ApplyNamingConventions = false)]
        public string WordValue { get; set; }

        /// <summary>
        /// Set a value on a memory location
        /// </summary>
        /// <param name="proc">A reference to the running 6502</param>
        /// <param name="expr">A reference to our expression parser</param>
        public void SetMemory(Processor proc, ExpressionParser expr)
        {
            var location = expr.Evaluate(Address);
            if (WordValue != null && !"".Equals(WordValue))
            {
                var wordValue = expr.Evaluate(WordValue);
                proc.WriteMemoryWord(location, wordValue);
            }
            else
            {
                var byteValue = expr.Evaluate(ByteValue);
                proc.WriteMemoryValueWithoutIncrement(location, (byte)byteValue);
            }
        }
    }
}