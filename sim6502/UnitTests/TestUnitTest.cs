using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace sim6502.UnitTests
{
    public class TestUnitTest
    {
        [YamlMember(Alias = "name", ApplyNamingConventions = false)]
        public string Name { get; set; }
        [YamlMember(Alias = "description", ApplyNamingConventions = false)]
        public string Description { get; set; }
        [YamlMember(Alias = "set_memory", ApplyNamingConventions = false)]
        public List<TestUnitTestSetMemory> SetMemory { get; set; }
        [YamlMember(Alias = "jump_address", ApplyNamingConventions = false)]
        public string JumpAddress { get; set; }
        [YamlMember(Alias = "stop_on", ApplyNamingConventions = false)]
        public string StopOn { get; set; }
        [YamlMember(Alias = "assert", ApplyNamingConventions = false)]
        public List<TestAssertion> Assertions { get; set; }

        public int JumpAddressParsed => JumpAddress.ParseNumber();
    }
}