using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Antlr4.Runtime.Tree;
using NLog;
using sim6502.Errors;
using sim6502.Expressions;
using sim6502.Grammar.Generated;
using sim6502.Proc;
using sim6502.Systems;
using sim6502.Utilities;

namespace sim6502.Grammar
{
    public class SimBaseListener : sim6502BaseListener
    {
        private readonly ParseTreeProperty<int> _intValues = new ParseTreeProperty<int>();
        private readonly ParseTreeProperty<bool> _boolValues = new ParseTreeProperty<bool>();

        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        // Track the processor type for the current suite
        private ProcessorType _currentProcessorType = ProcessorType.MOS6502;

        // Track system type and memory map for the current suite
        private SystemType? _currentSystemType;
        private IMemoryMap? _currentMemoryMap;

        /// <summary>
        /// Strip surrounding quotes from a string literal.
        /// The grammar no longer strips quotes, so we do it here for portability.
        /// </summary>
        private static string StripQuotes(string text)
        {
            if (text.Length >= 2 && text.StartsWith("\"") && text.EndsWith("\""))
                return text.Substring(1, text.Length - 2);
            return text;
        }

        /// <summary>
        /// Get the ProcessorType from a processorTypeValue context.
        /// </summary>
        private ProcessorType GetProcessorType(sim6502Parser.ProcessorTypeValueContext context)
        {
            if (context.ProcessorType6502() != null)
                return ProcessorType.MOS6502;
            if (context.ProcessorType6510() != null)
                return ProcessorType.MOS6510;
            if (context.ProcessorType65C02() != null)
                return ProcessorType.WDC65C02;

            return ProcessorType.MOS6502; // default
        }

        /// <summary>
        /// Get the SystemType from a systemTypeValue context.
        /// </summary>
        private SystemType GetSystemType(sim6502Parser.SystemTypeValueContext context)
        {
            if (context.SystemC64() != null)
                return SystemType.C64;
            if (context.SystemGeneric6502() != null)
                return SystemType.Generic6502;
            if (context.SystemGeneric6510() != null)
                return SystemType.Generic6510;
            if (context.SystemGeneric65C02() != null)
                return SystemType.Generic65C02;

            return SystemType.Generic6502; // default
        }

        /// <summary>
        /// Check if a test should run based on --test and --filter options.
        /// --test takes priority (exact match), --filter uses glob pattern.
        /// </summary>
        private bool ShouldRunTest(string testName)
        {
            // --test takes priority (exact match)
            if (!string.IsNullOrEmpty(SingleTest))
                return testName.Equals(SingleTest, StringComparison.OrdinalIgnoreCase);

            // --filter glob pattern
            if (!string.IsNullOrEmpty(FilterPattern))
            {
                var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(FilterPattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$";
                if (!System.Text.RegularExpressions.Regex.IsMatch(testName, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Check if a test should run based on --filter-tag and --exclude-tag options.
        /// </summary>
        private bool ShouldRunByTags()
        {
            // Parse current test tags
            var testTags = string.IsNullOrEmpty(_currentTestTags)
                ? new List<string>()
                : _currentTestTags.Split(',').Select(t => t.Trim()).ToList();

            // --filter-tag (OR logic - at least one tag must match)
            if (!string.IsNullOrEmpty(FilterTags))
            {
                var filterList = FilterTags.Split(',').Select(t => t.Trim()).ToList();
                if (!filterList.Any(f => testTags.Contains(f, StringComparer.OrdinalIgnoreCase)))
                    return false;
            }

            // --exclude-tag (exclude if any tag matches)
            if (!string.IsNullOrEmpty(ExcludeTags))
            {
                var excludeList = ExcludeTags.Split(',').Select(t => t.Trim()).ToList();
                if (excludeList.Any(e => testTags.Contains(e, StringComparer.OrdinalIgnoreCase)))
                    return false;
            }

            return true;
        }

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
        public int TotalTestsSkipped { get; private set; }

        public int TotalSuitesPassed { get; set; } = 0;

        public int TotalSuitesFailed { get; set; } = 0;

        // Make sure we do a JSR during test
        private bool _didJsr;

        // Store the setup block context for execution before each test
        private sim6502Parser.SetupBlockContext _currentSetupBlock;

        // Track whether we're currently walking the setup block definition (not executing it)
        private bool _inSetupBlockDefinition;

        // Test options
        private bool _currentTestSkipped;
        private bool _currentTestExplicitlySkipped;  // For tests with skip = true
        private bool _currentTestTraceEnabled;
        private int _currentTestTimeout;
        private string _currentTestTags;

        // Filter options
        public string? FilterPattern { get; set; }
        public string? SingleTest { get; set; }
        public string? FilterTags { get; set; }
        public string? ExcludeTags { get; set; }
        public bool ListOnly { get; set; }

        // Error collector for semantic errors
        public ErrorCollector Errors { get; set; } = new();

        public Processor Proc { get; set; }
        public SymbolFile Symbols { get; set; }

        private string CurrentSuite { get; set; }
        
        private void ResetTest()
        {
            _testFailureMessages.Clear();
            _didJsr = false;
            _currentTestSkipped = false;
            _currentTestExplicitlySkipped = false;
            _currentTestTraceEnabled = false;
            _currentTestTimeout = 0;
            _currentTestTags = "";
            Proc.ResetCycleCount();
            Proc.TraceEnabled = false;
            Proc.ClearTraceBuffer();
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

        #region Validation Helpers

        private bool ValidateAddress(int address, int line, int col, string context = "address")
        {
            if (address >= 0 && address <= 0xFFFF)
                return true;

            Errors.AddError(
                ErrorPhase.Semantic,
                line,
                col,
                1,
                $"{context} ${address:X} out of range (valid: $0000-$FFFF)",
                null);
            return false;
        }

        private bool ValidateByteValue(int value, int line, int col)
        {
            if (value >= 0 && value <= 0xFF)
                return true;

            Errors.AddError(
                ErrorPhase.Semantic,
                line,
                col,
                1,
                $"value ${value:X} too large for byte (max $FF)",
                null);
            return false;
        }

        private bool ValidateWordValue(int value, int line, int col)
        {
            if (value >= 0 && value <= 0xFFFF)
                return true;

            Errors.AddError(
                ErrorPhase.Semantic,
                line,
                col,
                1,
                $"value ${value:X} too large for word (max $FFFF)",
                null);
            return false;
        }

        private void AddFileError(string filePath, int line, int col, string message)
        {
            Errors.AddError(
                ErrorPhase.Runtime,
                line,
                col,
                filePath.Length,
                message,
                null);
        }

        #endregion

        private void OutputTrace()
        {
            var trace = Proc.GetTraceBuffer();
            if (trace.Count == 0) return;

            Logger.Info("");
            Logger.Info($"Execution trace ({trace.Count} instructions):");
            foreach (var line in trace)
            {
                Logger.Info(line);
            }
        }

        private void ResetSuite()
        {
            if (TotalTestsFailed > 0)
                TotalSuitesFailed++;
            else
                TotalSuitesPassed++;

            var totalTests = TotalTestsFailed + TotalTestsPassed + TotalTestsSkipped;
            var logLevel = TotalTestsFailed == 0 ? LogLevel.Info : LogLevel.Fatal;
            var passedMsg = $"{TotalTestsPassed.ToString()} of {totalTests.ToString()} tests ran successfully";
            if (TotalTestsSkipped > 0)
            {
                passedMsg += $" ({TotalTestsSkipped.ToString()} skipped)";
            }
            Logger.Log(logLevel, $"{passedMsg} in suite '{CurrentSuite}'.");

            CurrentSuite = "";
            _currentSuitePassed = true;
            _testFailureMessages.Clear();
            _suiteResources.Clear();
            TotalTestsFailed = 0;
            TotalTestsPassed = 0;
            TotalTestsSkipped = 0;
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
            // Reset for new suite
            _currentSystemType = null;
            _currentMemoryMap = null;
            _currentProcessorType = ProcessorType.MOS6502;

            // Check for system declaration (takes precedence)
            var sysDecl = context.systemDeclaration();
            if (sysDecl != null)
            {
                _currentSystemType = GetSystemType(sysDecl.systemTypeValue());
                var (memMap, procType) = MemoryMapFactory.CreateForSystem(_currentSystemType.Value);
                _currentMemoryMap = memMap;
                _currentProcessorType = procType;
                Logger.Info($"System type set to: {_currentSystemType} (processor: {_currentProcessorType})");
            }
            else
            {
                // Check for processor declaration (backward compat with deprecation warning)
                var procDecl = context.processorDeclaration();
                if (procDecl != null)
                {
                    _currentProcessorType = GetProcessorType(procDecl.processorTypeValue());
                    var (memMap, _) = MemoryMapFactory.CreateForProcessor(_currentProcessorType);
                    _currentMemoryMap = memMap;
                    var systemName = _currentProcessorType.ToString().ToLower().Replace("mos", "").Replace("wdc", "");
                    Logger.Warn($"processor() is deprecated - use system(generic_{systemName}) instead");
                    Logger.Info($"Processor type set to: {_currentProcessorType}");
                }
                else
                {
                    // Default to generic 6502
                    var (memMap, _) = MemoryMapFactory.CreateForProcessor(ProcessorType.MOS6502);
                    _currentMemoryMap = memMap;
                }
            }

            // Create processor with memory map
            Proc = new Processor(_currentProcessorType, _currentMemoryMap!);
            Proc.Reset();

            CurrentSuite = StripQuotes(context.suiteName().GetText());
            Logger.Info($"Running test suite '{CurrentSuite}'...");
        }

        public override void ExitSuite(sim6502Parser.SuiteContext context)
        {
            // Clear the setup block context when exiting the suite
            _currentSetupBlock = null;
            ResetSuite();
        }

        public override void ExitRomDeclaration(sim6502Parser.RomDeclarationContext context)
        {
            var romName = StripQuotes(context.romName().StringLiteral().GetText());
            var romFilename = StripQuotes(context.romFilename().StringLiteral().GetText());
            var filenameCtx = context.romFilename();

            if (!File.Exists(romFilename))
            {
                Errors.AddError(
                    ErrorPhase.Runtime,
                    filenameCtx.Start.Line,
                    filenameCtx.Start.Column,
                    romFilename.Length + 2,
                    $"ROM file not found: '{romFilename}'",
                    null);
                return;
            }

            if (_currentMemoryMap == null)
            {
                Errors.AddError(
                    ErrorPhase.Runtime,
                    context.Start.Line,
                    context.Start.Column,
                    10,
                    "rom() requires a system or processor declaration",
                    null);
                return;
            }

            try
            {
                var romData = File.ReadAllBytes(romFilename);
                _currentMemoryMap.LoadRom(romName, romData);
                Logger.Info($"Loaded ROM '{romName}' from '{romFilename}' ({romData.Length} bytes)");
            }
            catch (Exception ex)
            {
                Errors.AddError(
                    ErrorPhase.Runtime,
                    filenameCtx.Start.Line,
                    filenameCtx.Start.Column,
                    romFilename.Length + 2,
                    $"Failed to load ROM: {ex.Message}",
                    null);
            }
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
                // Try to suggest a similar symbol
                var hint = SuggestionEngine.SuggestSymbol(symbol, Symbols.GetAllSymbols().Keys);
                var hintMsg = hint != null ? $"Did you mean '{hint}'?" : null;

                Errors.AddError(
                    ErrorPhase.Semantic,
                    line,
                    col,
                    symbol.Length,
                    $"undefined symbol '{symbol}'",
                    hintMsg);

                // Set a default value so we can continue finding more errors
                SetIntValue(context, 0);
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
            // Always evaluate expressions, but skip side effects during setup block definition or skipped tests
            var address = GetIntValue(context.expression(0));
            var value = GetIntValue(context.expression(1));

            // Validate address and value (word values supported for auto byte/word write)
            var addrExpr = context.expression(0);
            var valExpr = context.expression(1);
            var addrValid = ValidateAddress(address, addrExpr.Start.Line, addrExpr.Start.Column);
            var valValid = ValidateWordValue(value, valExpr.Start.Line, valExpr.Start.Column);

            if (!_inSetupBlockDefinition && !_currentTestSkipped && addrValid && valValid)
            {
                Proc.WriteMemoryValue(address, value);
            }
        }

        public override void ExitAddressAssignment(sim6502Parser.AddressAssignmentContext context)
        {
            // Always evaluate expressions, but skip side effects during setup block definition or skipped tests
            var address = GetIntValue(context.address());
            var value = GetIntValue(context.expression());

            // Validate address and value (word values supported for auto byte/word write)
            var addrCtx = context.address();
            var valExpr = context.expression();
            var addrValid = ValidateAddress(address, addrCtx.Start.Line, addrCtx.Start.Column);
            var valValid = ValidateWordValue(value, valExpr.Start.Line, valExpr.Start.Column);

            if (!_inSetupBlockDefinition && !_currentTestSkipped && addrValid && valValid)
            {
                WriteValueToMemory(address, value);
            }
        }

        public override void ExitSymbolAssignment(sim6502Parser.SymbolAssignmentContext context)
        {
            // Always evaluate expressions, but skip side effects during setup block definition or skipped tests
            var address = GetIntValue(context.symbolRef());
            var value = GetIntValue(context.expression());

            if (!_inSetupBlockDefinition && !_currentTestSkipped)
            {
                WriteValueToMemory(address, value);
            }
        }

        public override void ExitSymbolRegisterAssignment(sim6502Parser.SymbolRegisterAssignmentContext context)
        {
            // Store register value to memory at symbol address
            var address = GetIntValue(context.symbolRef());
            var value = GetRegisterValue(context.Register().GetText());

            if (!_inSetupBlockDefinition && !_currentTestSkipped)
            {
                Proc.WriteMemoryValueWithoutIncrement(address, (byte)value);
                Logger.Trace($"Stored register value ${value:X2} to ${address:X4}");
            }
        }

        public override void ExitAddressRegisterAssignment(sim6502Parser.AddressRegisterAssignmentContext context)
        {
            // Store register value to memory at numeric address
            var address = GetIntValue(context.address());
            var value = GetRegisterValue(context.Register().GetText());

            // Validate address
            var addrCtx = context.address();
            if (!ValidateAddress(address, addrCtx.Start.Line, addrCtx.Start.Column))
                return;

            if (!_inSetupBlockDefinition && !_currentTestSkipped)
            {
                Proc.WriteMemoryValueWithoutIncrement(address, (byte)value);
                Logger.Trace($"Stored register value ${value:X2} to ${address:X4}");
            }
        }

        private int GetRegisterValue(string register)
        {
            return register.ToLower() switch
            {
                "a" => Proc.Accumulator,
                "x" => Proc.XRegister,
                "y" => Proc.YRegister,
                _ => throw new InvalidOperationException($"Unknown register: {register}")
            };
        }

        public override void ExitRegisterAssignment(sim6502Parser.RegisterAssignmentContext context)
        {
            var register = context.Register().GetText();
            var exp = GetIntValue(context.expression());

            // Skip side effects during setup block definition or skipped tests
            if (_inSetupBlockDefinition || _currentTestSkipped)
                return;

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

            // Skip side effects during setup block definition or skipped tests
            if (_inSetupBlockDefinition || _currentTestSkipped)
                return;

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
                    Proc.OverflowFlag = val;
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
            var exprValue = GetIntValue(context.expression());
            var bw = context.byteWord();

            int value;
            if (bw == null)
            {
                // No .b or .w suffix - use the expression value directly
                // This handles cases like peekbyte(...), peekword(...), or arithmetic expressions
                value = exprValue;
            }
            else if (bw.GetText().ToLower().Equals(".b"))
            {
                // .b suffix - read byte from memory at address
                value = Proc.ReadMemoryValueWithoutCycle(exprValue);
            }
            else // .w
            {
                // .w suffix - read word from memory at address
                value = Proc.ReadMemoryWordWithoutCycle(exprValue);
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
            var filename = StripQuotes(context.symbolsFilename().StringLiteral().GetText());
            var filenameCtx = context.symbolsFilename();

            if (!File.Exists(filename))
            {
                Errors.AddError(
                    ErrorPhase.Runtime,
                    filenameCtx.Start.Line,
                    filenameCtx.Start.Column,
                    filename.Length + 2,
                    $"symbol file not found: '{filename}'",
                    null);
                return;
            }

            Logger.Trace($"Loading symbol file {filename}");
            var symbols = File.ReadAllText(filename);
            Symbols = new SymbolFile(symbols);
            Logger.Trace($"{Symbols.SymbolCount.ToString()} symbols loaded.");
        }

        public override void ExitLoadFunction(sim6502Parser.LoadFunctionContext context)
        {
            var filename = StripQuotes(context.loadFilename().StringLiteral().GetText());
            var filenameCtx = context.loadFilename();
            var addrCtx = context.loadAddress();
            var strip = false;
            if (context.stripHeader() != null)
                strip = GetBoolValue(context.stripHeader());

            if (!File.Exists(filename))
            {
                Errors.AddError(
                    ErrorPhase.Runtime,
                    filenameCtx.Start.Line,
                    filenameCtx.Start.Column,
                    filename.Length + 2,
                    $"file not found: '{filename}'",
                    null);
                return;
            }

            var fileInfo = new FileInfo(filename);
            if (fileInfo.Length == 0)
            {
                Errors.AddError(
                    ErrorPhase.Runtime,
                    filenameCtx.Start.Line,
                    filenameCtx.Start.Column,
                    filename.Length + 2,
                    $"file is empty: '{filename}'",
                    null);
                return;
            }

            var address = addrCtx == null ? Utility.GetProgramLoadAddress(filename) : GetIntValue(context.loadAddress().address());

            var lr = new LoadableResource {Filename = filename, LoadAddress = address, StripHeader = strip};

            _suiteResources.Add(lr);
        }
        
        public override void EnterSetupBlock(sim6502Parser.SetupBlockContext context)
        {
            // Store the setup block context for execution before each test
            // Set flag to prevent execution during the initial tree walk
            _currentSetupBlock = context;
            _inSetupBlockDefinition = true;
            Logger.Trace("Setup block registered for execution before each test");
        }

        public override void ExitSetupBlock(sim6502Parser.SetupBlockContext context)
        {
            // Clear the flag - we're done walking the setup block definition
            _inSetupBlockDefinition = false;
        }

        public override void EnterTestFunction(sim6502Parser.TestFunctionContext context)
        {
            ResetTest();

            // Parse test options if present
            var options = context.testOptions();
            if (options != null)
            {
                foreach (var opt in options.testOption())
                {
                    if (opt.Skip() != null)
                    {
                        // Parse boolean directly from text since we haven't exited the context yet
                        _currentTestSkipped = opt.boolean().GetText() == "true";
                        _currentTestExplicitlySkipped = _currentTestSkipped;
                        Logger.Trace($"Test option: skip = {_currentTestSkipped}");
                    }
                    else if (opt.Trace() != null)
                    {
                        _currentTestTraceEnabled = opt.boolean().GetText() == "true";
                        Logger.Trace($"Test option: trace = {_currentTestTraceEnabled}");
                    }
                    else if (opt.Timeout() != null)
                    {
                        // Parse number directly from text
                        _currentTestTimeout = opt.number().GetText().ParseNumber();
                        Logger.Trace($"Test option: timeout = {_currentTestTimeout}");
                    }
                    else if (opt.Tags() != null)
                    {
                        _currentTestTags = StripQuotes(opt.StringLiteral().GetText());
                        Logger.Trace($"Test option: tags = {_currentTestTags}");
                    }
                }
            }

            // Get test name for filtering
            var testName = StripQuotes(context.testName().GetText());

            // --list mode: just output the name if it matches filters and return
            if (ListOnly)
            {
                if (ShouldRunTest(testName) && ShouldRunByTags())
                {
                    Console.WriteLine($"  {testName}");
                }
                _currentTestSkipped = true;
                return;
            }

            // Check if test matches name/pattern filter
            if (!ShouldRunTest(testName))
            {
                _currentTestSkipped = true;
                return;
            }

            // Check if test matches tag filters
            if (!ShouldRunByTags())
            {
                _currentTestSkipped = true;
                return;
            }

            // If test is skipped via skip option, don't execute setup or test contents
            if (_currentTestSkipped)
            {
                Logger.Info("Test marked as skipped, will not execute");
                return;
            }

            // Enable trace buffering if requested
            if (_currentTestTraceEnabled)
            {
                Proc.TraceEnabled = true;
                Proc.ClearTraceBuffer();
                Logger.Trace("Trace buffering enabled for test");
            }

            // Execute setup block before each test if present
            if (_currentSetupBlock != null)
            {
                Logger.Trace("Executing setup block before test");
                foreach (var content in _currentSetupBlock.setupContents())
                {
                    // Walk each setup statement
                    ParseTreeWalker.Default.Walk(this, content);
                }
            }
        }

        public override void ExitTestFunction(sim6502Parser.TestFunctionContext context)
        {
            var test = StripQuotes(context.testName().GetText());
            var description = StripQuotes(context.testDescription().GetText());

            // Handle skipped tests (including those filtered out)
            if (_currentTestSkipped)
            {
                // Only log if it was explicitly skipped via skip = true option
                if (_currentTestExplicitlySkipped)
                {
                    Logger.Warn($"'{test} - {description}' : SKIPPED");
                }
                TotalTestsSkipped++;
                return;
            }

            // Check for JSR only if test was not skipped
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

                // Output trace if trace was enabled and test failed
                if (_currentTestTraceEnabled)
                {
                    OutputTrace();
                }
            }
        }

        public override void EnterAssertFunction(sim6502Parser.AssertFunctionContext context)
        {
            // Skip assertions in skipped tests
            if (_currentTestSkipped)
                return;

            _currentAssertion = StripQuotes(context.assertDescription().GetText());
        }

        public override void ExitAssertFunction(sim6502Parser.AssertFunctionContext context)
        {
            // Skip assertions in skipped tests
            if (_currentTestSkipped)
                return;

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
            // Skip execution if we're in the setup block definition
            if (_inSetupBlockDefinition)
                return;

            // Skip execution if test is skipped
            if (_currentTestSkipped)
                return;

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

        public override void ExitMemFillFunction(sim6502Parser.MemFillFunctionContext ctx)
        {
            // Always evaluate expressions
            var address = _intValues.Get(ctx.expression(0));
            var count = _intValues.Get(ctx.expression(1));
            var value = _intValues.Get(ctx.expression(2));

            // Skip side effects during setup block definition or skipped tests
            if (_inSetupBlockDefinition || _currentTestSkipped)
                return;

            // Validate address is within 6502 memory bounds (64KB)
            if (address < 0 || address >= 0x10000)
            {
                Logger.Error($"[memfill] Invalid address ${address:X4} (valid range: $0000-$FFFF)");
                return;
            }

            // Handle zero or negative count
            if (count <= 0)
            {
                Logger.Debug($"[memfill] Nothing to fill (count={count})");
                return;
            }

            // Clamp count to prevent writing beyond memory bounds
            var maxCount = 0x10000 - address;
            if (count > maxCount)
            {
                Logger.Warn($"[memfill] Count {count} exceeds memory bounds, clamping to {maxCount}");
                count = maxCount;
            }

            Logger.Debug($"memfill(${address:X4}, {count}, ${value:X2})");

            for (var i = 0; i < count; i++)
            {
                Proc.WriteMemoryValueWithoutIncrement(address + i, (byte)(value & 0xFF));
            }
        }

        public override void ExitMemDumpFunction(sim6502Parser.MemDumpFunctionContext ctx)
        {
            // Skip execution if we're in the setup block definition or skipped tests
            if (_inSetupBlockDefinition || _currentTestSkipped)
                return;

            var address = _intValues.Get(ctx.expression(0));
            var count = _intValues.Get(ctx.expression(1));

            // Validate address is within 6502 memory bounds (64KB)
            if (address < 0 || address >= 0x10000)
            {
                Logger.Error($"[memdump] Invalid address ${address:X4} (valid range: $0000-$FFFF)");
                return;
            }

            // Handle zero or negative count
            if (count <= 0)
            {
                Logger.Debug($"[memdump] Nothing to dump (count={count})");
                return;
            }

            // Clamp count to prevent reading beyond memory bounds
            var maxCount = 0x10000 - address;
            if (count > maxCount)
            {
                Logger.Warn($"[memdump] Count {count} exceeds memory bounds, clamping to {maxCount}");
                count = maxCount;
            }

            // Try to resolve symbol name for the address
            var symbolName = Symbols?.AddressToSymbol(address, false);
            var header = symbolName != null && symbolName != address.ToString()
                ? $"[memdump] ${address:X4} ({symbolName}), {count} bytes:"
                : $"[memdump] ${address:X4}, {count} bytes:";

            Console.WriteLine(header);

            // Output 8 bytes per line with ASCII sidebar
            for (var offset = 0; offset < count; offset += 8)
            {
                var lineAddr = address + offset;
                var bytes = new System.Text.StringBuilder();
                var ascii = new System.Text.StringBuilder();

                for (var i = 0; i < 8 && offset + i < count; i++)
                {
                    var b = Proc.ReadMemoryValueWithoutCycle(lineAddr + i);
                    bytes.Append($"{b:X2} ");
                    // ASCII printable range: 0x20 (space) to 0x7E (tilde), excluding 0x7F (DEL)
                    ascii.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                }

                Console.WriteLine($"{lineAddr:X4}: {bytes,-24}|  {ascii}|");
            }
        }

        #endregion

    }
}