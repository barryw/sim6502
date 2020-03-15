using YamlDotNet.Serialization;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class Tests
    {
        [YamlMember(Alias = "init", ApplyNamingConventions = false)]
        public TestInit Init { get; set; }
        [YamlMember(Alias = "unit_tests", ApplyNamingConventions = false)]
        public TestUnitTests UnitTests { get; set; }
    }
}