using System.Collections.Generic;
using YamlDotNet.Serialization;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class TestInit
    {
        [YamlMember(Alias = "load", ApplyNamingConventions = false)]
        public List<TestInitLoadFile> LoadFiles { get; set; }
    }
}