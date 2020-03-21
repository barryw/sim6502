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
            Assert.AreEqual(0xffff.ToString(), hex1.ParseNumber().ToString());

            hex1 = "$fff";
            Assert.AreEqual(0xfff.ToString(), hex1.ParseNumber().ToString());

            hex1 = "$ff";
            Assert.AreEqual(0xff.ToString(), hex1.ParseNumber().ToString());

            hex1 = "$f";
            Assert.AreEqual(0xf.ToString(), hex1.ParseNumber().ToString());

            hex1 = "$abcd";
            Assert.AreEqual(0xabcd.ToString(), hex1.ParseNumber().ToString());
        }

        [Test]
        public void TestParseWithZeroX()
        {
            var hex1 = "0xffff";
            Assert.AreEqual(0xffff.ToString(), hex1.ParseNumber().ToString());

            hex1 = "0xfff";
            Assert.AreEqual(0xfff.ToString(), hex1.ParseNumber().ToString());

            hex1 = "0xff";
            Assert.AreEqual(0xff.ToString(), hex1.ParseNumber().ToString());

            hex1 = "0xf";
            Assert.AreEqual(0xf.ToString(), hex1.ParseNumber().ToString());

            hex1 = "0xabcd";
            Assert.AreEqual(0xabcd.ToString(), hex1.ParseNumber().ToString());
        }

        [Test]
        public void TestPlainIntegers()
        {
            var int1 = "65535";
            Assert.AreEqual(0xffff.ToString(), int1.ParseNumber().ToString());

            int1 = "4095";
            Assert.AreEqual(0xfff.ToString(), int1.ParseNumber().ToString());

            int1 = "255";
            Assert.AreEqual(0xff.ToString(), int1.ParseNumber().ToString());

            int1 = "15";
            Assert.AreEqual(0xf.ToString(), int1.ParseNumber().ToString());

            int1 = "43981";
            Assert.AreEqual(0xabcd.ToString(), int1.ParseNumber().ToString());
        }

        [Test]
        public void TestGetLoadAddress()
        {
            var bytes = new byte[2];
            bytes[0] = 0;
            bytes[1] = 192;

            var address = Utility.GetProgramLoadAddress(bytes);
            Assert.AreEqual(0xc000.ToString(), address.ToString());
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