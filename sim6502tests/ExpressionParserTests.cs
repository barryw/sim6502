using NUnit.Framework;
using sim6502;

namespace sim6502tests
{
    [TestFixture]
    public class ExpressionParserTests
    {
        private Processor Proc;
        private SymbolFile Syms;

        [SetUp]
        public void Setup()
        {
            Proc = new Processor();
            Proc.Reset();
            const string symbols = ".label test1=$0001\n.label test2=$fffe\n.label test3=$8000\n.namespace vic {\n.label SP0X=$d000\n}";
            Syms = new SymbolFile(symbols);

            Proc.WriteMemoryValue(0x0001, 0xcd);
            Proc.WriteMemoryWord(0xfffe, 0xabcd);
        }

        [TestCase("1+1", 2)]
        [TestCase("{test3}", 0x8000)]
        [TestCase("peekbyte({test1})", 0xcd)]
        [TestCase("peekword({test2})", 0xabcd)]
        [TestCase("peekbyte({test2}) + 256 * peekbyte({test2} + 1)", 0xabcd)]
        [TestCase("{vic.SP0X}", 0xd000)]
        [TestCase("11669 * 3", 35007)]
        [TestCase("9229668 * 6", 55378008)]
        public void TestExpressions(string expression, int expected)
        {
            var ep = new ExpressionParser(Proc, Syms);
            var actual = ep.Evaluate(expression);
            Assert.AreEqual(expected.ToString(), actual.ToString());
        }
    }
}