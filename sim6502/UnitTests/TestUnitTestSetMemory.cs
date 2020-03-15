using YamlDotNet.Serialization;

namespace sim6502.UnitTests
{
    public class TestUnitTestSetMemory
    {
        [YamlMember(Alias = "address", ApplyNamingConventions = false)]
        public string Address { get; set; }
        [YamlMember(Alias = "byte_value", ApplyNamingConventions = false)]
        public string ByteValue { get; set; }
        [YamlMember(Alias = "word_value", ApplyNamingConventions = false)]
        public string WordValue { get; set; }
        
        public int AddressParsed => Address.ParseNumber();
        public int WordValueParsed => WordValue.ParseNumber();
        public byte ByteValueParsed => (byte)ByteValue.ParseNumber();
    }
}