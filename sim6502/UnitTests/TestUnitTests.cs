using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace sim6502.UnitTests
{
    public class TestUnitTests
    {
        [YamlMember(Alias = "program", ApplyNamingConventions = false)]
        public string Program { get; set; }
        [YamlMember(Alias = "address", ApplyNamingConventions = false)]
        public string Address { get; set; }
        [YamlMember(Alias = "tests", ApplyNamingConventions = false)]
        public List<TestUnitTest> UnitTests { get; set; }
        
        public int AddressParsed => Address.ParseNumber();
    }
}