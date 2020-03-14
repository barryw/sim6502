using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace sim6502.UnitTests
{
    public class TestInit
    {
        [YamlMember(Alias = "load", ApplyNamingConventions = false)]
        public List<TestInitLoadFile> LoadFiles { get; set; }
    }
}