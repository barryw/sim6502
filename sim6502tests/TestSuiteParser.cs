using System.Collections.Generic;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using NUnit.Framework;
using sim6502.Grammar;
using sim6502.Grammar.Generated;
using sim6502.Utilities;

namespace sim6502tests
{
    [TestFixture]
    public class TestSuiteParser
    {
        private static sim6502Parser.SuitesContext GetContext(string test)
        {
            var afs = new AntlrFileStream(test);
            var lexer = new sim6502Lexer(afs);
            var tokens = new CommonTokenStream(lexer);
            var parser = new sim6502Parser(tokens);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(new SimErrorListener());
            parser.BuildParseTree = true;
            return parser.suites();
        }
        
        [Test]
        public void TestSuite1()
        {
            var symbols = new Dictionary<string, int>();

            symbols.Add("MySymbol", 0xa000);
            symbols.Add("Loc1", 0xc000);
            symbols.Add("Loc2", 0x80);
            
            var symbolFile = new SymbolFile(symbols);

            var tree = GetContext("GrammarTests/test-1.txt");

            var walker = new ParseTreeWalker();
            var sbl = new SimBaseListener();
            
            sbl.Symbols = symbolFile;

            walker.Walk(sbl, tree);
            
            Assert.AreEqual(0xd0, sbl.Proc.ReadMemoryValueWithoutCycle(0x80));
            Assert.AreEqual(0xabcd, sbl.Proc.ReadMemoryWordWithoutCycle(0xc000));
            Assert.AreEqual(0xdcba, sbl.Proc.ReadMemoryWordWithoutCycle(0xc002));
            Assert.AreEqual(0x0d, sbl.Proc.ReadMemoryValueWithoutCycle(0x81));
        }
        
        [Test]
        public void TestSuite2()
        {
            var symbols = new Dictionary<string, int> {{"Val1", 0x11}, {"Val2", 0x22}, {"Val3", 0xff}};
            var symbolFile = new SymbolFile(symbols);

            var tree = GetContext("GrammarTests/test-2.txt");

            var walker = new ParseTreeWalker();
            var sbl = new SimBaseListener {Symbols = symbolFile};
            
            walker.Walk(sbl, tree);
            
            Assert.AreEqual(0x11, sbl.Proc.XRegister);
            Assert.AreEqual(0x22, sbl.Proc.Accumulator);
            Assert.AreEqual(0xff, sbl.Proc.YRegister);
        }

        [Test]
        public void TestSuite3()
        {
            var symbols = new Dictionary<string, int>
            {
                {"Val1", 0x11},
                {"Val2", 0x22},
                {"Val3", 0xff},
                {"Loc1", 0xd020},
                {"Loc2", 0xd021},
                {"Loc3", 0xd022}
            };


            var symbolFile = new SymbolFile(symbols);

            var tree = GetContext("GrammarTests/test-3.txt");

            var walker = new ParseTreeWalker();
            var sbl = new SimBaseListener {Symbols = symbolFile};
            
            walker.Walk(sbl, tree);
            
            Assert.AreEqual(0x11, sbl.Proc.ReadMemoryValueWithoutCycle(0xd020));
            Assert.AreEqual(0x22, sbl.Proc.ReadMemoryValueWithoutCycle(0xd021));
            Assert.AreEqual(0xff, sbl.Proc.ReadMemoryValueWithoutCycle(0xd022));
        }
        
        [Test]
        public void TestSuite4()
        {
            var symbols = new Dictionary<string, int> {{"FALSE", 0x00}};
            
            var symbolFile = new SymbolFile(symbols);
            var tree = GetContext("GrammarTests/test-4.txt");

            var walker = new ParseTreeWalker();
            var sbl = new SimBaseListener {Symbols = symbolFile};

            walker.Walk(sbl, tree);
            
            Assert.IsTrue(sbl.Proc.CarryFlag);
            Assert.IsFalse(sbl.Proc.NegativeFlag);
            Assert.IsTrue(sbl.Proc.ZeroFlag);
            Assert.IsFalse(sbl.Proc.OverflowFlag);
            Assert.IsFalse(sbl.Proc.DecimalFlag);
        }
        
        [Test]
        public void TestSuite5()
        {
            var tree = GetContext("GrammarTests/test-5.txt");

            var walker = new ParseTreeWalker();
            var sbl = new SimBaseListener();

            walker.Walk(sbl, tree);
        }
        
        [Test]
        public void TestSuite6()
        {
            var tree = GetContext("GrammarTests/test-6.txt");

            var walker = new ParseTreeWalker();
            var sbl = new SimBaseListener();

            walker.Walk(sbl, tree);
        }
        
        [Test]
        public void TestSuite7()
        {
            var tree = GetContext("GrammarTests/test-7.txt");

            var walker = new ParseTreeWalker();
            var sbl = new SimBaseListener();

            walker.Walk(sbl, tree);
        }
        
        [Test]
        public void TestSuite8()
        {
            var symbols = new Dictionary<string, int>
            {
                {"Loc1", 0xd020}
            };
            
            var symbolFile = new SymbolFile(symbols);
            
            var tree = GetContext("GrammarTests/test-8.txt");

            var walker = new ParseTreeWalker();
            var sbl = new SimBaseListener {Symbols = symbolFile};

            walker.Walk(sbl, tree);

            Assert.AreEqual(0xabcd, sbl.Proc.ReadMemoryWordWithoutCycle(0xd020));
            Assert.AreEqual(0xd0, sbl.Proc.ReadMemoryValueWithoutCycle(0xd022));
        }
        
        [Test]
        public void TestSuite9()
        {
            var symbols = new Dictionary<string, int>
            {
                {"Loc1", 0xd020}
            };
            
            var symbolFile = new SymbolFile(symbols);
            
            var tree = GetContext("GrammarTests/test-9.txt");

            var walker = new ParseTreeWalker();
            var sbl = new SimBaseListener {Symbols = symbolFile};

            walker.Walk(sbl, tree);
        }
        
        [Test]
        public void TestSuite10()
        {
            var symbols = new Dictionary<string, int>
            {
                {"Loc1", 0xd020}
            };
            
            var symbolFile = new SymbolFile(symbols);
            
            var tree = GetContext("GrammarTests/test-10.txt");

            var walker = new ParseTreeWalker();
            var sbl = new SimBaseListener {Symbols = symbolFile};

            walker.Walk(sbl, tree);
        }
    }
}