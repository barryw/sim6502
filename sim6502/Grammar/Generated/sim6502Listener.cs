//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     ANTLR Version: 4.8
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

// Generated from sim6502.g4 by ANTLR 4.8

// Unreachable code detected
#pragma warning disable 0162
// The variable '...' is assigned but its value is never used
#pragma warning disable 0219
// Missing XML comment for publicly visible type or member '...'
#pragma warning disable 1591
// Ambiguous reference in cref attribute
#pragma warning disable 419

namespace sim6502.Grammar.Generated {
using Antlr4.Runtime.Misc;
using IParseTreeListener = Antlr4.Runtime.Tree.IParseTreeListener;
using IToken = Antlr4.Runtime.IToken;

/// <summary>
/// This interface defines a complete listener for a parse tree produced by
/// <see cref="sim6502Parser"/>.
/// </summary>
[System.CodeDom.Compiler.GeneratedCode("ANTLR", "4.8")]
[System.CLSCompliant(false)]
public interface Isim6502Listener : IParseTreeListener {
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.suites"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSuites([NotNull] sim6502Parser.SuitesContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.suites"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSuites([NotNull] sim6502Parser.SuitesContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.suite"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSuite([NotNull] sim6502Parser.SuiteContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.suite"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSuite([NotNull] sim6502Parser.SuiteContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.suiteName"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSuiteName([NotNull] sim6502Parser.SuiteNameContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.suiteName"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSuiteName([NotNull] sim6502Parser.SuiteNameContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>expressionAssignment</c>
	/// labeled alternative in <see cref="sim6502Parser.assignment"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterExpressionAssignment([NotNull] sim6502Parser.ExpressionAssignmentContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>expressionAssignment</c>
	/// labeled alternative in <see cref="sim6502Parser.assignment"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitExpressionAssignment([NotNull] sim6502Parser.ExpressionAssignmentContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>symbolAssignment</c>
	/// labeled alternative in <see cref="sim6502Parser.assignment"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSymbolAssignment([NotNull] sim6502Parser.SymbolAssignmentContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>symbolAssignment</c>
	/// labeled alternative in <see cref="sim6502Parser.assignment"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSymbolAssignment([NotNull] sim6502Parser.SymbolAssignmentContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>addressAssignment</c>
	/// labeled alternative in <see cref="sim6502Parser.assignment"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterAddressAssignment([NotNull] sim6502Parser.AddressAssignmentContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>addressAssignment</c>
	/// labeled alternative in <see cref="sim6502Parser.assignment"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitAddressAssignment([NotNull] sim6502Parser.AddressAssignmentContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>registerAssignment</c>
	/// labeled alternative in <see cref="sim6502Parser.assignment"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterRegisterAssignment([NotNull] sim6502Parser.RegisterAssignmentContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>registerAssignment</c>
	/// labeled alternative in <see cref="sim6502Parser.assignment"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitRegisterAssignment([NotNull] sim6502Parser.RegisterAssignmentContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>flagAssignment</c>
	/// labeled alternative in <see cref="sim6502Parser.assignment"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterFlagAssignment([NotNull] sim6502Parser.FlagAssignmentContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>flagAssignment</c>
	/// labeled alternative in <see cref="sim6502Parser.assignment"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitFlagAssignment([NotNull] sim6502Parser.FlagAssignmentContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>numberAddress</c>
	/// labeled alternative in <see cref="sim6502Parser.address"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterNumberAddress([NotNull] sim6502Parser.NumberAddressContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>numberAddress</c>
	/// labeled alternative in <see cref="sim6502Parser.address"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitNumberAddress([NotNull] sim6502Parser.NumberAddressContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>symbolAddress</c>
	/// labeled alternative in <see cref="sim6502Parser.address"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSymbolAddress([NotNull] sim6502Parser.SymbolAddressContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>symbolAddress</c>
	/// labeled alternative in <see cref="sim6502Parser.address"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSymbolAddress([NotNull] sim6502Parser.SymbolAddressContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>hexNumber</c>
	/// labeled alternative in <see cref="sim6502Parser.number"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterHexNumber([NotNull] sim6502Parser.HexNumberContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>hexNumber</c>
	/// labeled alternative in <see cref="sim6502Parser.number"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitHexNumber([NotNull] sim6502Parser.HexNumberContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>intNumber</c>
	/// labeled alternative in <see cref="sim6502Parser.number"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterIntNumber([NotNull] sim6502Parser.IntNumberContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>intNumber</c>
	/// labeled alternative in <see cref="sim6502Parser.number"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitIntNumber([NotNull] sim6502Parser.IntNumberContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>binaryNumber</c>
	/// labeled alternative in <see cref="sim6502Parser.number"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterBinaryNumber([NotNull] sim6502Parser.BinaryNumberContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>binaryNumber</c>
	/// labeled alternative in <see cref="sim6502Parser.number"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitBinaryNumber([NotNull] sim6502Parser.BinaryNumberContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.boolean"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterBoolean([NotNull] sim6502Parser.BooleanContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.boolean"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitBoolean([NotNull] sim6502Parser.BooleanContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.assertFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterAssertFunction([NotNull] sim6502Parser.AssertFunctionContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.assertFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitAssertFunction([NotNull] sim6502Parser.AssertFunctionContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.assertDescription"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterAssertDescription([NotNull] sim6502Parser.AssertDescriptionContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.assertDescription"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitAssertDescription([NotNull] sim6502Parser.AssertDescriptionContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>compareExpression</c>
	/// labeled alternative in <see cref="sim6502Parser.comparison"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterCompareExpression([NotNull] sim6502Parser.CompareExpressionContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>compareExpression</c>
	/// labeled alternative in <see cref="sim6502Parser.comparison"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitCompareExpression([NotNull] sim6502Parser.CompareExpressionContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>memoryChk</c>
	/// labeled alternative in <see cref="sim6502Parser.comparison"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterMemoryChk([NotNull] sim6502Parser.MemoryChkContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>memoryChk</c>
	/// labeled alternative in <see cref="sim6502Parser.comparison"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitMemoryChk([NotNull] sim6502Parser.MemoryChkContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>memoryCmp</c>
	/// labeled alternative in <see cref="sim6502Parser.comparison"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterMemoryCmp([NotNull] sim6502Parser.MemoryCmpContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>memoryCmp</c>
	/// labeled alternative in <see cref="sim6502Parser.comparison"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitMemoryCmp([NotNull] sim6502Parser.MemoryCmpContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>registerCompare</c>
	/// labeled alternative in <see cref="sim6502Parser.compareLHS"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterRegisterCompare([NotNull] sim6502Parser.RegisterCompareContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>registerCompare</c>
	/// labeled alternative in <see cref="sim6502Parser.compareLHS"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitRegisterCompare([NotNull] sim6502Parser.RegisterCompareContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>flagCompare</c>
	/// labeled alternative in <see cref="sim6502Parser.compareLHS"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterFlagCompare([NotNull] sim6502Parser.FlagCompareContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>flagCompare</c>
	/// labeled alternative in <see cref="sim6502Parser.compareLHS"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitFlagCompare([NotNull] sim6502Parser.FlagCompareContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>addressCompare</c>
	/// labeled alternative in <see cref="sim6502Parser.compareLHS"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterAddressCompare([NotNull] sim6502Parser.AddressCompareContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>addressCompare</c>
	/// labeled alternative in <see cref="sim6502Parser.compareLHS"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitAddressCompare([NotNull] sim6502Parser.AddressCompareContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>cyclesCompare</c>
	/// labeled alternative in <see cref="sim6502Parser.compareLHS"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterCyclesCompare([NotNull] sim6502Parser.CyclesCompareContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>cyclesCompare</c>
	/// labeled alternative in <see cref="sim6502Parser.compareLHS"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitCyclesCompare([NotNull] sim6502Parser.CyclesCompareContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>expressionCompare</c>
	/// labeled alternative in <see cref="sim6502Parser.compareLHS"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterExpressionCompare([NotNull] sim6502Parser.ExpressionCompareContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>expressionCompare</c>
	/// labeled alternative in <see cref="sim6502Parser.compareLHS"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitExpressionCompare([NotNull] sim6502Parser.ExpressionCompareContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.jsrFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterJsrFunction([NotNull] sim6502Parser.JsrFunctionContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.jsrFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitJsrFunction([NotNull] sim6502Parser.JsrFunctionContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.stopOn"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterStopOn([NotNull] sim6502Parser.StopOnContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.stopOn"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitStopOn([NotNull] sim6502Parser.StopOnContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.failOnBreak"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterFailOnBreak([NotNull] sim6502Parser.FailOnBreakContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.failOnBreak"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitFailOnBreak([NotNull] sim6502Parser.FailOnBreakContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.symbolsFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSymbolsFunction([NotNull] sim6502Parser.SymbolsFunctionContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.symbolsFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSymbolsFunction([NotNull] sim6502Parser.SymbolsFunctionContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.symbolsFilename"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSymbolsFilename([NotNull] sim6502Parser.SymbolsFilenameContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.symbolsFilename"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSymbolsFilename([NotNull] sim6502Parser.SymbolsFilenameContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.loadFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterLoadFunction([NotNull] sim6502Parser.LoadFunctionContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.loadFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitLoadFunction([NotNull] sim6502Parser.LoadFunctionContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.loadFilename"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterLoadFilename([NotNull] sim6502Parser.LoadFilenameContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.loadFilename"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitLoadFilename([NotNull] sim6502Parser.LoadFilenameContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.loadAddress"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterLoadAddress([NotNull] sim6502Parser.LoadAddressContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.loadAddress"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitLoadAddress([NotNull] sim6502Parser.LoadAddressContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.stripHeader"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterStripHeader([NotNull] sim6502Parser.StripHeaderContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.stripHeader"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitStripHeader([NotNull] sim6502Parser.StripHeaderContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.testFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterTestFunction([NotNull] sim6502Parser.TestFunctionContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.testFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitTestFunction([NotNull] sim6502Parser.TestFunctionContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.testName"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterTestName([NotNull] sim6502Parser.TestNameContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.testName"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitTestName([NotNull] sim6502Parser.TestNameContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.testDescription"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterTestDescription([NotNull] sim6502Parser.TestDescriptionContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.testDescription"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitTestDescription([NotNull] sim6502Parser.TestDescriptionContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.testContents"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterTestContents([NotNull] sim6502Parser.TestContentsContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.testContents"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitTestContents([NotNull] sim6502Parser.TestContentsContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.peekByteFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterPeekByteFunction([NotNull] sim6502Parser.PeekByteFunctionContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.peekByteFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitPeekByteFunction([NotNull] sim6502Parser.PeekByteFunctionContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.peekWordFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterPeekWordFunction([NotNull] sim6502Parser.PeekWordFunctionContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.peekWordFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitPeekWordFunction([NotNull] sim6502Parser.PeekWordFunctionContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.memoryCmpFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterMemoryCmpFunction([NotNull] sim6502Parser.MemoryCmpFunctionContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.memoryCmpFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitMemoryCmpFunction([NotNull] sim6502Parser.MemoryCmpFunctionContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.memoryChkFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterMemoryChkFunction([NotNull] sim6502Parser.MemoryChkFunctionContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.memoryChkFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitMemoryChkFunction([NotNull] sim6502Parser.MemoryChkFunctionContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.sourceAddress"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSourceAddress([NotNull] sim6502Parser.SourceAddressContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.sourceAddress"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSourceAddress([NotNull] sim6502Parser.SourceAddressContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.targetAddress"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterTargetAddress([NotNull] sim6502Parser.TargetAddressContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.targetAddress"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitTargetAddress([NotNull] sim6502Parser.TargetAddressContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.memorySize"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterMemorySize([NotNull] sim6502Parser.MemorySizeContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.memorySize"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitMemorySize([NotNull] sim6502Parser.MemorySizeContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.memoryValue"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterMemoryValue([NotNull] sim6502Parser.MemoryValueContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.memoryValue"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitMemoryValue([NotNull] sim6502Parser.MemoryValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>addressValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterAddressValue([NotNull] sim6502Parser.AddressValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>addressValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitAddressValue([NotNull] sim6502Parser.AddressValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>divisionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterDivisionValue([NotNull] sim6502Parser.DivisionValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>divisionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitDivisionValue([NotNull] sim6502Parser.DivisionValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>intValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterIntValue([NotNull] sim6502Parser.IntValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>intValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitIntValue([NotNull] sim6502Parser.IntValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>intFunctionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterIntFunctionValue([NotNull] sim6502Parser.IntFunctionValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>intFunctionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitIntFunctionValue([NotNull] sim6502Parser.IntFunctionValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>bitOrExpressionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterBitOrExpressionValue([NotNull] sim6502Parser.BitOrExpressionValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>bitOrExpressionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitBitOrExpressionValue([NotNull] sim6502Parser.BitOrExpressionValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>boolFunctionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterBoolFunctionValue([NotNull] sim6502Parser.BoolFunctionValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>boolFunctionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitBoolFunctionValue([NotNull] sim6502Parser.BoolFunctionValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>subValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSubValue([NotNull] sim6502Parser.SubValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>subValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSubValue([NotNull] sim6502Parser.SubValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>subExpressionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSubExpressionValue([NotNull] sim6502Parser.SubExpressionValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>subExpressionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSubExpressionValue([NotNull] sim6502Parser.SubExpressionValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>boolValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterBoolValue([NotNull] sim6502Parser.BoolValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>boolValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitBoolValue([NotNull] sim6502Parser.BoolValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>bitXorExpressionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterBitXorExpressionValue([NotNull] sim6502Parser.BitXorExpressionValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>bitXorExpressionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitBitXorExpressionValue([NotNull] sim6502Parser.BitXorExpressionValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>multiplyValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterMultiplyValue([NotNull] sim6502Parser.MultiplyValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>multiplyValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitMultiplyValue([NotNull] sim6502Parser.MultiplyValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>bitAndExpressionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterBitAndExpressionValue([NotNull] sim6502Parser.BitAndExpressionValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>bitAndExpressionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitBitAndExpressionValue([NotNull] sim6502Parser.BitAndExpressionValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>addValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterAddValue([NotNull] sim6502Parser.AddValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>addValue</c>
	/// labeled alternative in <see cref="sim6502Parser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitAddValue([NotNull] sim6502Parser.AddValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>loByte</c>
	/// labeled alternative in <see cref="sim6502Parser.lbhb"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterLoByte([NotNull] sim6502Parser.LoByteContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>loByte</c>
	/// labeled alternative in <see cref="sim6502Parser.lbhb"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitLoByte([NotNull] sim6502Parser.LoByteContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>hiByte</c>
	/// labeled alternative in <see cref="sim6502Parser.lbhb"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterHiByte([NotNull] sim6502Parser.HiByteContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>hiByte</c>
	/// labeled alternative in <see cref="sim6502Parser.lbhb"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitHiByte([NotNull] sim6502Parser.HiByteContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>byteValue</c>
	/// labeled alternative in <see cref="sim6502Parser.byteWord"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterByteValue([NotNull] sim6502Parser.ByteValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>byteValue</c>
	/// labeled alternative in <see cref="sim6502Parser.byteWord"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitByteValue([NotNull] sim6502Parser.ByteValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>wordValue</c>
	/// labeled alternative in <see cref="sim6502Parser.byteWord"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterWordValue([NotNull] sim6502Parser.WordValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>wordValue</c>
	/// labeled alternative in <see cref="sim6502Parser.byteWord"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitWordValue([NotNull] sim6502Parser.WordValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>peekByteFunctionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.intFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterPeekByteFunctionValue([NotNull] sim6502Parser.PeekByteFunctionValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>peekByteFunctionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.intFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitPeekByteFunctionValue([NotNull] sim6502Parser.PeekByteFunctionValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>peekWordFunctionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.intFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterPeekWordFunctionValue([NotNull] sim6502Parser.PeekWordFunctionValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>peekWordFunctionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.intFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitPeekWordFunctionValue([NotNull] sim6502Parser.PeekWordFunctionValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>memoryChkFunctionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.boolFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterMemoryChkFunctionValue([NotNull] sim6502Parser.MemoryChkFunctionValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>memoryChkFunctionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.boolFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitMemoryChkFunctionValue([NotNull] sim6502Parser.MemoryChkFunctionValueContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>memoryCmpFunctionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.boolFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterMemoryCmpFunctionValue([NotNull] sim6502Parser.MemoryCmpFunctionValueContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>memoryCmpFunctionValue</c>
	/// labeled alternative in <see cref="sim6502Parser.boolFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitMemoryCmpFunctionValue([NotNull] sim6502Parser.MemoryCmpFunctionValueContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.symbolRef"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSymbolRef([NotNull] sim6502Parser.SymbolRefContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.symbolRef"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSymbolRef([NotNull] sim6502Parser.SymbolRefContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="sim6502Parser.symbol"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSymbol([NotNull] sim6502Parser.SymbolContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="sim6502Parser.symbol"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSymbol([NotNull] sim6502Parser.SymbolContext context);
}
} // namespace sim6502.Grammar.Generated
