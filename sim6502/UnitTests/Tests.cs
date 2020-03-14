using YamlDotNet.Serialization;

namespace sim6502.UnitTests
{
    public class Tests
    {
        [YamlMember(Alias = "init", ApplyNamingConventions = false)]
        public TestInit Init { get; set; }
        [YamlMember(Alias = "unit_tests", ApplyNamingConventions = false)]
        public TestUnitTests UnitTests { get; set; }
    }
}