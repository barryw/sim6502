namespace sim6502.UnitTests
{
    public class TestInitLoadFile
    {
        public string Filename { get; set; }
        public string Address { get; set; }

        public int AddressParsed => Address.ParseNumber();
    }
}