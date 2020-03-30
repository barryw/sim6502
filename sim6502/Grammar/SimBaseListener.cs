using System;
using System.Collections.Generic;
using System.IO;
using Antlr4.Runtime.Tree;
using NLog;
using sim6502.Expressions;
using sim6502.Grammar.Generated;
using sim6502.Proc;
using sim6502.Utilities;

namespace sim6502.Grammar
{
    public class SimBaseListener : sim6502BaseListener
    {
        private readonly ParseTreeProperty<int> _intValues = new ParseTreeProperty<int>();
        private readonly ParseTreeProperty<bool> _boolValues = new ParseTreeProperty<bool>();
        
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        // Whether the current test has passed
        private bool TestPassed => _testFailureMessages.Count == 0;

        // Keep track of our failure messages. We display them for any test that had failing assertions.
        private readonly List<string> _testFailureMessages = new List<string>();

        // Each suite has a list of resources that we need for test, which includes the program under test
        // as well as any additional things like ROMS (KERNAL/BASIC - sold separately)
        private readonly List<LoadableResource> _suiteResources = new List<LoadableResource>();

        private bool _currentSuitePassed = true;
        private bool _allSuitesPassed = true;
        private string _currentAssertion;

        public int TotalTestsFailed { get; private set; }
        public int TotalTestsPassed { get; private set; }

        public int TotalSuitesPassed { get; set; } = 0;

        public int TotalSuitesFailed { get; set; } = 0;
        
        // Make sure we do a JSR during test
        private bool _didJsr;
        
        public Processor Proc { get; set; }
        public SymbolFile Symbols { get; set; }

        private string CurrentSuite { get; set; }
        
        private void ResetTest()
        {
            _testFailureMessages.Clear();
            _didJsr = false;
            Proc.ResetCycleCount();
            LoadResources();
        }

        private void FailAssertion(string message)
        {
            _testFailureMessages.Add($"{message} in assertion '{_currentAssertion}'");
            FailTest();
        }
        
        private void FailTest()
        {
            _currentSuitePassed = false;
            _allSuitesPassed = false;
        }

        private void ResetSuite()
        {
            if (TotalTestsFailed > 0)
                TotalSuitesFailed++;
            else
                TotalSuitesPassed++;
            
            var totalTests = TotalTestsFailed + TotalTestsPassed;
            var logLevel = TotalTestsFailed == 0 ? LogLevel.Info : LogLevel.Fatal;
            Logger.Log(logLevel, $"{TotalTestsPassed.ToString()} of {totalTests.ToString()} tests ran successfully in suite '{CurrentSuite}'.");
            
            CurrentSuite = "";
            _currentSuitePassed = true;
            _testFailureMessages.Clear();
            _suiteResources.Clear();
            TotalTestsFailed = 0;
            TotalTestsPassed = 0;
        }

        private void LoadResources()
        {
            foreach (var lr in _suiteResources)
            {
                Utility.LoadFileIntoProcessor(Proc, lr.LoadAddress, lr.Filename, lr.StripHeader);
            }
        }
        
        private void SetIntValue(IParseTree node, int value)
        {
            _intValues.Put(node, value);
        }

        private int GetIntValue(IParseTree node)
        {
            if (node != null)
                return _intValues.Get(node);
            throw new Exception($"Tried to fetch int value for null node");
        }

        private void SetBoolValue(IParseTree node, bool value)
        {
            _boolValues.Put(node, value);
        }

        private bool GetBoolValue(IParseTree node)
        {
            return _boolValues.Get(node);
        }

        public override void EnterSuite(sim6502Parser.SuiteContext context)
        {
            Proc = new Processor();
            Proc.Reset();
            CurrentSuite = context.suiteName().GetText();
            Logger.Info($"Running test suite '{CurrentSuite}'...");
        }

        public override void ExitSuite(sim6502Parser.SuiteContext context)
        {
            ResetSuite();
        }

        public override void ExitSuites(sim6502Parser.SuitesContext context)
        {
            var logLevel = TotalSuitesFailed == 0 ? LogLevel.Info : LogLevel.Fatal;
            var totalSuites = TotalSuitesFailed + TotalSuitesPassed;
            Logger.Log(logLevel, $"{TotalSuitesPassed.ToString()} of {totalSuites.ToString()} suites passed.");
        }

        public override void ExitStripHeader(sim6502Parser.StripHeaderContext context) => SetBoolValue(context, 
            GetBoolValue(context.boolean()));

        public override void ExitBoolean(sim6502Parser.BooleanContext context) => SetBoolValue(context, 
            context.Boolean().GetText() == "true");

        public override void ExitHexNumber(sim6502Parser.HexNumberContext context) => SetIntValue(context, 
            context.Hex().GetText().ParseNumber());

        public override void ExitIntNumber(sim6502Parser.IntNumberContext context) => SetIntValue(context, 
            context.Int().GetText().ParseNumber());

        public override void ExitNumberAddress(sim6502Parser.NumberAddressContext context) => SetIntValue(context, 
            context.number().GetText().ParseNumber());

        public override void ExitSymbolAddress(sim6502Parser.SymbolAddressContext context) => 
            SymbolToInt(context.symbolRef().symbol().GetText(), context, context.symbolRef().Start.Line, 
                context.symbolRef().Start.Column);

        public override void ExitSymbolRef(sim6502Parser.SymbolRefContext context) => 
            SymbolToInt(context.symbol().GetText(), context, context.symbol().Start.Line, context.symbol().Start.Column);

        private void SymbolToInt(string symbol, IParseTree context, int line, int col)
        {
            if (Symbols.SymbolExists(symbol))
            {
                var address = Symbols.SymbolToAddress(symbol);
                Logger.Trace($"Symbol [{symbol}] resolves to {address.ToString()}");
                SetIntValue(context, address);
            }
            else
            {
                FailAssertion($"Symbol [{symbol}] was not found @ line {line.ToString()}:{col.ToString()}");
            }
        }

        public override void ExitBinaryNumber(sim6502Parser.BinaryNumberContext context) => SetIntValue(context, 
            context.Binary().GetText().ParseNumber());

        public override void ExitMemoryCmpFunctionValue(sim6502Parser.MemoryCmpFunctionValueContext context) => 
            SetBoolValue(context, GetBoolValue(context.memoryCmpFunction()));

        public override void ExitMemoryChkFunctionValue(sim6502Parser.MemoryChkFunctionValueContext context) => 
            SetBoolValue(context, GetBoolValue(context.memoryChkFunction()));

        public override void ExitMemorySize(sim6502Parser.MemorySizeContext context) => SetIntValue(context, 
            GetIntValue(context.expression()));

        public override void ExitSourceAddress(sim6502Parser.SourceAddressContext context) => SetIntValue(context, 
            GetIntValue(context.expression()));

        public override void ExitTargetAddress(sim6502Parser.TargetAddressContext context) => SetIntValue(context, 
            GetIntValue(context.expression()));

        public override void ExitMemoryValue(sim6502Parser.MemoryValueContext context) => SetIntValue(context, 
            GetIntValue(context.expression()));

        public override void ExitLoadAddress(sim6502Parser.LoadAddressContext context) => SetIntValue(context, 
            GetIntValue(context.address()));

        #region Expression Values
        
        public override void ExitSubExpressionValue(sim6502Parser.SubExpressionValueContext context) => SetIntValue(context, 
            GetIntValue(context.expression()));

        public override void ExitSubValue(sim6502Parser.SubValueContext context) => SetIntValue(context, 
            GetIntValue(context.expression(0)) - GetIntValue(context.expression(1)));

        public override void ExitAddValue(sim6502Parser.AddValueContext context) => SetIntValue(context, 
            GetIntValue(context.expression(0)) + GetIntValue(context.expression(1)));

        public override void ExitPeekByteFunctionValue(sim6502Parser.PeekByteFunctionValueContext context) => 
            SetIntValue(context, GetIntValue(context.peekByteFunction()));

        public override void ExitPeekWordFunctionValue(sim6502Parser.PeekWordFunctionValueContext context) => 
            SetIntValue(context, GetIntValue(context.peekWordFunction()));

        public override void ExitDivisionValue(sim6502Parser.DivisionValueContext context)
        {
            var exp1 = GetIntValue(context.expression(0));
            var exp2 = GetIntValue(context.expression(1));

            try
            {
                SetIntValue(context, exp1 / exp2);
            }
            catch (DivideByZeroException)
            {
                FailAssertion($"Division by zero {exp1.ToString()} / {exp2.ToString()}");
            }
        }
        
        public override void ExitMultiplyValue(sim6502Parser.MultiplyValueContext context) => SetIntValue(context, 
            GetIntValue(context.expression(0)) * GetIntValue(context.expression(1)));

        public override void ExitIntFunctionValue(sim6502Parser.IntFunctionValueContext context) => SetIntValue(context, 
            GetIntValue(context.intFunction()));

        public override void ExitBitAndExpressionValue(sim6502Parser.BitAndExpressionValueContext context) => 
            SetIntValue(context, GetIntValue(context.expression(0)) & GetIntValue(context.expression(1)));

        public override void ExitBitOrExpressionValue(sim6502Parser.BitOrExpressionValueContext context) => 
            SetIntValue(context, GetIntValue(context.expression(0)) | GetIntValue(context.expression(1)));

        public override void ExitBitXorExpressionValue(sim6502Parser.BitXorExpressionValueContext context) => 
            SetIntValue(context, GetIntValue(context.expression(0)) ^ GetIntValue(context.expression(1)));

        public override void ExitAddressValue(sim6502Parser.AddressValueContext context)
        {
            var address = GetIntValue(context.address());
            var value = address;

            if (context.lbhb() != null)
            {
                switch (context.lbhb().GetText())
                {
                    case ".l":
                        value = address - ((address >> 8) * 256);
                        Logger.Trace($"Returning LO BYTE of address {address.ToString()}: {value.ToString()}");
                        break;
                    case ".h":
                        value = address >> 8;
                        Logger.Trace($"Returning HI BYTE of address {address.ToString()}: {value.ToString()}");
                        break;
                }    
            }

            SetIntValue(context, value);
        }

        public override void ExitIntValue(sim6502Parser.IntValueContext context) => SetIntValue(context, 
            context.number().GetText().ParseNumber());

        public override void ExitBoolValue(sim6502Parser.BoolValueContext context)
        {
            SetIntValue(context, GetBoolValue(context.boolean()) ? 1 : 0);
            SetBoolValue(context, GetBoolValue(context.boolean()));
        }
        
        public override void ExitBoolFunctionValue(sim6502Parser.BoolFunctionValueContext context)
        {
            SetIntValue(context, GetBoolValue(context.boolFunction()) ? 1 : 0);
            SetBoolValue(context, GetBoolValue(context.boolFunction()));
        }

        #endregion
        
        #region Assignments

        public override void ExitExpressionAssignment(sim6502Parser.ExpressionAssignmentContext context)
        {
            Proc.WriteMemoryValue(GetIntValue(context.expression(0)), 
                GetIntValue(context.expression(1)));
        }

        public override void ExitAddressAssignment(sim6502Parser.AddressAssignmentContext context) => WriteValueToMemory(
            GetIntValue(context.address()), GetIntValue(context.expression()));

        public override void ExitSymbolAssignment(sim6502Parser.SymbolAssignmentContext context) => WriteValueToMemory(
            GetIntValue(context.symbolRef()), GetIntValue(context.expression()));

        public override void ExitRegisterAssignment(sim6502Parser.RegisterAssignmentContext context)
        {
            var register = context.Register().GetText();
            var exp = GetIntValue(context.expression());
            if (exp > 255)
            {
                FailAssertion($"Cannot set the {register} register to {exp.ToString()} since it's bigger than 8 bits");
            }
            else
            {
                switch (register)
                {
                    case "a":
                        Proc.Accumulator = exp;
                        Logger.Trace($"Setting accumulator to {exp.ToString()}");
                        break;
                    case "x":
                        Proc.XRegister = exp;
                        Logger.Trace($"Setting x register to {exp.ToString()}");
                        break;
                    case "y":
                        Proc.YRegister = exp;
                        Logger.Trace($"Setting y register to {exp.ToString()}");
                        break;
                }
            }
        }
        
        public override void ExitFlagAssignment(sim6502Parser.FlagAssignmentContext context)
        {
            var flag = context.ProcessorFlag().GetText();

            var expr = context.expression().GetText();

            if (expr != "true" && expr != "false")
                expr = expr == "1" ? "true" : "false";

            var val = bool.Parse(expr);
            
            switch (flag)
            {
                case "c":
                    Proc.CarryFlag = val;
                    break;
                case "n":
                    Proc.NegativeFlag = val;
                    break;
                case "z":
                    Proc.ZeroFlag = val;
                    break;
                case "v":
                    Proc.NegativeFlag = val;
                    break;
                case "d":
                    Proc.DecimalFlag = val;
                    break;
                default:
                    FailAssertion($"Invalid flag {flag} attempted to be set to {val.ToString()}.");
                    break;
            }
            Logger.Trace($"{flag} is being set to {val.ToString()}");
        }
        
        private void WriteValueToMemory(int address, int value)
        {
            Proc.WriteMemoryValue(address, value);
        }
        
        #endregion
        
        #region Compares

        public override void ExitExpressionCompare(sim6502Parser.ExpressionCompareContext context)
        {
            var address = GetIntValue(context.expression());
            var value = 0;
            var bw = context.byteWord();
            if (bw == null || bw.GetText().ToLower().Equals(".b"))
            {
                value = Proc.ReadMemoryValueWithoutCycle(address);
            }
            if (bw != null && bw.GetText().ToLower().Equals(".w"))
            {
                value = Proc.ReadMemoryWordWithoutCycle(address);
            }
            
            SetIntValue(context, value);
        }

        public override void ExitAddressCompare(sim6502Parser.AddressCompareContext context)
        {
            var address = GetIntValue(context.address());
            var value = Proc.ReadMemoryValueWithoutCycle(address);

            SetIntValue(context, value);  
        } 

        public override void ExitRegisterCompare(sim6502Parser.RegisterCompareContext context)
        {
            var register = context.Register().GetText();
            var value = register switch
            {
                "a" => Proc.Accumulator,
                "x" => Proc.XRegister,
                "y" => Proc.YRegister,
                _ => -1
            };

            Logger.Trace($"Register {register} has a value of {value.ToString()}");
            SetIntValue(context, value);
        }
        
        public override void ExitFlagCompare(sim6502Parser.FlagCompareContext context)
        {
            var flag = context.ProcessorFlag().GetText();
            var value = flag switch
            {
                "c" => Proc.CarryFlag ? 1 : 0,
                "z" => Proc.ZeroFlag ? 1 : 0,
                "d" => Proc.DecimalFlag ? 1 : 0,
                "v" => Proc.OverflowFlag ? 1 : 0,
                "n" => Proc.NegativeFlag ? 1 : 0,
                _ => 0
            };
            
            Logger.Trace($"Flag {flag} has a value of {value.ToString()}");
            SetIntValue(context, value);
        }
        
        public override void ExitCompareExpression(sim6502Parser.CompareExpressionContext context)
        {
            var expr = GetIntValue(context.expression());
            var op = context.CompareOperator().GetText();
            var lhs = GetIntValue(context.compareLHS());
            
            var res = BaseCompare.CompareValues(lhs, expr, op);
            Logger.Trace($"{lhs.ToString()} {op} {expr.ToString()} : {res.ComparisonPassed.ToString()}");
            if(!res.ComparisonPassed)
                FailAssertion(res.FailureMessage);
            SetBoolValue(context, res.ComparisonPassed);
        }

        public override void ExitCyclesCompare(sim6502Parser.CyclesCompareContext context) => SetIntValue(context, 
            Proc.CycleCount);

        #endregion
        
        #region Functions
        
        public override void ExitSymbolsFunction(sim6502Parser.SymbolsFunctionContext context)
        {
            var filename = context.symbolsFilename().StringLiteral().GetText();
            Logger.Trace($"Loading symbol file {filename}");
            var symbols = File.ReadAllText(filename);
            Symbols = new SymbolFile(symbols);
            Logger.Trace($"{Symbols.SymbolCount.ToString()} symbols loaded.");
        }
        
        public override void ExitLoadFunction(sim6502Parser.LoadFunctionContext context)
        {
            var filename = context.loadFilename().StringLiteral().GetText();
            var addrCtx = context.loadAddress();
            var strip = false;
            if (context.stripHeader() != null)
                strip = GetBoolValue(context.stripHeader());

            var address = addrCtx == null ? Utility.GetProgramLoadAddress(filename) : GetIntValue(context.loadAddress().address());

            var lr = new LoadableResource {Filename = filename, LoadAddress = address, StripHeader = strip};

            _suiteResources.Add(lr);
        }
        
        public override void EnterTestFunction(sim6502Parser.TestFunctionContext context)
        {
            ResetTest();
        }

        public override void ExitTestFunction(sim6502Parser.TestFunctionContext context)
        {
            var test = context.testName().GetText();
            var description = context.testDescription().GetText();

            if (!_didJsr)
            {
                FailAssertion("No JSR encountered. Make sure you call the jsr function in this test!");
            }

            if (TestPassed)
            {
                Logger.Info($"'{test} - {description}' : PASSED");
                TotalTestsPassed++;
            }
            else
            {
                Logger.Fatal($"'{test} - {description}' : FAILED");
                TotalTestsFailed++;
                foreach (var msg in _testFailureMessages)
                {
                    Logger.Fatal($"'{test}' - {msg}");
                }
            }
        }

        public override void EnterAssertFunction(sim6502Parser.AssertFunctionContext context)
        {
            _currentAssertion = context.assertDescription().GetText();
        }

        public override void ExitAssertFunction(sim6502Parser.AssertFunctionContext context)
        {
            var success = GetBoolValue(context.comparison());
            if (!success)
            {
                FailTest();
            }
        }

        public override void ExitMemoryChk(sim6502Parser.MemoryChkContext context) => SetBoolValue(context, GetBoolValue(context.memoryChkFunction()));

        public override void ExitMemoryChkFunction(sim6502Parser.MemoryChkFunctionContext context)
        {
            var source = GetIntValue(context.sourceAddress());
            var size = GetIntValue(context.memorySize());
            var value = GetIntValue(context.memoryValue());

            var mc = new MemoryCompare(Proc);
            var chk = mc.MemoryChk(source, size, value);
            if (!chk.ComparisonPassed)
            {
                FailAssertion(chk.FailureMessage);
            }
            
            SetBoolValue(context, chk.ComparisonPassed);
        }
        
        public override void ExitMemoryCmp(sim6502Parser.MemoryCmpContext context) => SetBoolValue(context, 
            GetBoolValue(context.memoryCmpFunction()));

        public override void ExitMemoryCmpFunction(sim6502Parser.MemoryCmpFunctionContext context)
        {
            var source = GetIntValue(context.sourceAddress());
            var target = GetIntValue(context.targetAddress());
            var size = GetIntValue(context.memorySize());
            
            var mc = new MemoryCompare(Proc);
            var cmp = mc.MemoryCmp(source, target, size);
            if (!cmp.ComparisonPassed)
            {
                FailAssertion(cmp.FailureMessage);
            }
            
            SetBoolValue(context, cmp.ComparisonPassed);
        }
        
        public override void ExitPeekByteFunction(sim6502Parser.PeekByteFunctionContext context) => SetIntValue(context, 
            Proc.ReadMemoryValueWithoutCycle(GetIntValue(context.expression())));

        public override void ExitPeekWordFunction(sim6502Parser.PeekWordFunctionContext context) => SetIntValue(context, 
            Proc.ReadMemoryWordWithoutCycle(GetIntValue(context.expression())));

        public override void ExitJsrFunction(sim6502Parser.JsrFunctionContext context)
        {
            var address = GetIntValue(context.address());
            var failOnBrkSet = context.failOnBreak().GetText() != null;
            var failOnBrk = true;
            if (failOnBrkSet)
                failOnBrk = GetBoolValue(context.failOnBreak());

            var stopOnRts = GetBoolValue(context.stopOn());
            var stopOnAddress = GetIntValue(context.stopOn());
            
            Logger.Trace($"Stop on address = {stopOnAddress.ToString()}");
            Logger.Trace($"Stop on RTS = {stopOnRts.ToString()}");
            Logger.Trace($"JSR address = {address.ToString()}");
            Logger.Trace($"JSR fail_on_brk = {failOnBrk.ToString()}");

            var finishedCleanly = Proc.RunRoutine(address, stopOnAddress, stopOnRts, failOnBrk);
            if (!finishedCleanly)
            {
                FailAssertion($"JSR call to {address.ToString()} returned an error.");
            }

            _didJsr = true;
        }

        public override void ExitStopOn(sim6502Parser.StopOnContext context)
        {
            if (context.address() != null)
            {
                SetIntValue(context, GetIntValue(context.address()));
            }

            if (context.boolean() != null)
            {
                SetBoolValue(context, GetBoolValue(context.boolean()));
            }
        }

        public override void ExitFailOnBreak(sim6502Parser.FailOnBreakContext context) => SetBoolValue(context, 
            GetBoolValue(context.boolean()));

        #endregion
        
    }
}