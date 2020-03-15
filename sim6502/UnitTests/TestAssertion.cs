using YamlDotNet.Serialization;

namespace sim6502.UnitTests
{
    public class TestAssertion
    {
        [YamlMember(Alias = "address", ApplyNamingConventions = false)]
        public string Address { get; set; }
        [YamlMember(Alias = "op", ApplyNamingConventions = false)]
        public string Op { get; set; }
        [YamlMember(Alias = "value", ApplyNamingConventions = false)]
        public string Value { get; set; }
        
        public int AddressParsed => Address.ParseNumber();
    }
}