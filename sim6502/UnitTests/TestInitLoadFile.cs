// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable MemberCanBePrivate.Global
namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class TestInitLoadFile
    {
        public string Filename { get; set; }
        public string Address { get; set; }

        public int AddressParsed => Address.ParseNumber();
    }
}