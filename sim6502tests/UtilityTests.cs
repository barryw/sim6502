using NUnit.Framework;
using sim6502;

namespace sim6502tests
{
    [TestFixture]
    public class UtilityTests
    {
        [Test]
        public void TestParseHexWithDollarSign()
        {
            var hex1 = "$ffff";
            Assert.AreEqual(65535, hex1.ParseNumber());

            hex1 = "$fff";
            Assert.AreEqual(4095, hex1.ParseNumber());
            
            hex1 = "$ff";
            Assert.AreEqual(255, hex1.ParseNumber());

            hex1 = "$f";
            Assert.AreEqual(15, hex1.ParseNumber());

            hex1 = "$abcd";
            Assert.AreEqual(43981, hex1.ParseNumber());
        }

        [Test]
        public void TestParseWithZeroX()
        {
            var hex1 = "0xffff";
            Assert.AreEqual(65535, hex1.ParseNumber());

            hex1 = "0xfff";
            Assert.AreEqual(4095, hex1.ParseNumber());

            hex1 = "0xff";
            Assert.AreEqual(255, hex1.ParseNumber());

            hex1 = "0xf";
            Assert.AreEqual(15, hex1.ParseNumber());

            hex1 = "0xabcd";
            Assert.AreEqual(43981, hex1.ParseNumber());
        }

        [Test]
        public void TestPlainIntegers()
        {
            var int1 = "65535";
            Assert.AreEqual(65535, int1.ParseNumber());

            int1 = "4095";
            Assert.AreEqual(4095, int1.ParseNumber());

            int1 = "255";
            Assert.AreEqual(255, int1.ParseNumber());

            int1 = "15";
            Assert.AreEqual(15, int1.ParseNumber());

            int1 = "43981";
            Assert.AreEqual(43981, int1.ParseNumber());
        }

        [Test]
        public void TestGetLoadAddress()
        {
            var bytes = new byte[2];
            bytes[0] = 0;
            bytes[1] = 192;

            var address = sim6502.Utility.GetProgramLoadAddress(bytes);
            Assert.AreEqual(49152, address);
        }

        [Test]
        public void TestConvertIntToHex()
        {
            var i = 65535;
            Assert.AreEqual("$ffff", i.ToHex());

            i = 4095;
            Assert.AreEqual("$fff", i.ToHex());

            i = 255;
            Assert.AreEqual("$ff", i.ToHex());

            i = 15;
            Assert.AreEqual("$f", i.ToHex());
        }
    }
}