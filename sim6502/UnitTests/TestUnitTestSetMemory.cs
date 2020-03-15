using YamlDotNet.Serialization;
// ReSharper disable UnusedAutoPropertyAccessor.Global

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
    }
}