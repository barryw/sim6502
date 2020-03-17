using YamlDotNet.Serialization;
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class TestAssertion
    {
        [YamlMember(Alias = "byte_count", ApplyNamingConventions = false)]
        public string ByteCount { get; set; }
        [YamlMember(Alias = "description", ApplyNamingConventions = false)]
        public string Description { get; set; }
        [YamlMember(Alias = "address", ApplyNamingConventions = false)]
        public string Address { get; set; }
        [YamlMember(Alias = "op", ApplyNamingConventions = false)]
        public string Op { get; set; }
        [YamlMember(Alias = "word_value", ApplyNamingConventions = false)]
        public string WordValue { get; set; }
        [YamlMember(Alias = "byte_value", ApplyNamingConventions = false)]
        public string ByteValue { get; set; }

        /// <summary>
        /// Run one of a unit test's assertions
        /// </summary>
        /// <param name="proc">A reference to our running 6502</param>
        /// <param name="expr">A reference to our expression parser</param>
        /// <param name="test">A reference to our parent TestUnitTest object</param>
        /// <returns>True if the assertion passed, or False otherwise</returns>
        public bool PerformAssertion(Processor proc, ExpressionParser expr, TestUnitTest test)
        {
            return AssertionFactory.GetAssertionClass(this).PerformAssertion(proc, expr, test, this);
        }
    }
}