using NUnit.Framework;
using sim6502;
using sim6502.Utilities;

namespace sim6502tests
{
    [TestFixture]
    public class SymbolFileTests
    {
        [Test]
        public void TestAddSymbols()
        {
            var symfile = ".label MyLabel=$0801\n.label YourLabel=$c000\n  .label OurLabel=49152";
            var sf = new SymbolFile(symfile);

            Assert.AreEqual(2049, sf.SymbolToAddress("MyLabel"));
            Assert.AreEqual(49152, sf.SymbolToAddress("YourLabel"));
            Assert.AreEqual(49152, sf.SymbolToAddress("OurLabel"));
        }

        [Test]
        public void TestNamespaces()
        {
            var symfile =
                ".label NonNamespacedLabel=$400\n.namespace kernal {\n  .label NamespacedLabel=$ffff\n}\n.label AnotherNonNamespacedLabel=$0800";
            var sf = new SymbolFile(symfile);

            Assert.AreEqual(1024, sf.SymbolToAddress("NonNamespacedLabel"));
            Assert.AreEqual(65535, sf.SymbolToAddress("kernal.NamespacedLabel"));
            Assert.AreEqual(2048, sf.SymbolToAddress("AnotherNonNamespacedLabel"));
        }

        [Test]
        public void TestLookupByAddress()
        {
            var symfile =
                ".label NonNamespacedLabel=$400\n.namespace kernal {\n  .label NamespacedLabel=$ffff\n}\n.label AnotherNonNamespacedLabel=$0800";
            var sf = new SymbolFile(symfile);

            Assert.AreEqual("NonNamespacedLabel", sf.AddressToSymbol(1024));
            Assert.AreEqual("kernal.NamespacedLabel", sf.AddressToSymbol(65535));
            Assert.AreEqual("AnotherNonNamespacedLabel", sf.AddressToSymbol(2048));

            Assert.AreEqual("$401", sf.AddressToSymbol(1025));
            Assert.AreEqual("1025", sf.AddressToSymbol(1025, false));
        }
    }
}