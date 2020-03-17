using System;
using System.Data;
using System.Text.RegularExpressions;
using NCalc;
using NLog;
// ReSharper disable UnusedMember.Local

namespace sim6502
{
    public class ExpressionParser
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
        
        /// <summary>
        /// Allow us to have functions in our expressions
        /// </summary>
        private class ExpressionContext
        {
            public Processor Processor { get; set; }

            /// <summary>
            /// Peek a byte from the 6502's memory
            /// </summary>
            /// <param name="address">The address to peek from</param>
            /// <returns>The byte stored in address</returns>
            public byte Peekbyte(int address)
            {
                var val = Processor.ReadMemoryValueWithoutCycle(address);
                Logger.Trace($"peekbyte({address}) = {val}");
                return val;
            }

            /// <summary>
            /// Peek a word from the 6502's memory
            /// </summary>
            /// <param name="address">The low byte address to peek from</param>
            /// <returns>The word stored in address and address + 1</returns>
            public int Peekword(int address)
            {
                var val = Processor.ReadMemoryWordWithoutCycle(address);
                Logger.Trace($"peekword({address}) = {val}");
                return val;
            }
        }
        
        private readonly Processor _proc;
        private readonly SymbolFile _syms;
        
        public ExpressionParser(Processor proc, SymbolFile symbols)
        {
            _proc = proc;
            _syms = symbols;
        }

        /// <summary>
        /// Evaluate an expression containing functions and symbols
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public int Evaluate(string expression)
        {
            Logger.Trace($"Evaluating raw expression '{expression}'");
            var replaceSymbols = ReplaceSymbols(expression);
            Logger.Trace($"After symbol substitution '{replaceSymbols}'");
            var replaceHex = ReplaceHexStrings(replaceSymbols);
            Logger.Trace($"After hex substitution '{replaceHex}'");
            var expr = new Expression(replaceHex);
            var f = expr.ToLambda<ExpressionContext, int>();
            var context = new ExpressionContext {Processor = _proc};
            var val = Convert.ToInt32(f(context));
            Logger.Trace($"Final expression value = {val}");
            return val;
        }

        /// <summary>
        /// Replace all {} symbols with their values from the symbol table
        /// </summary>
        /// <param name="expression">The expression we want to replace symbols in</param>
        /// <returns>The same expression with symbols replaced</returns>
        private string ReplaceSymbols(string expression)
        {
            foreach (Match match in Regex.Matches(expression, "{([0-9a-zA-Z_]+)}"))
            {
                var symbol = match.Groups[1].Value;
                var value = 0;
                if(_syms.SymbolExists(symbol))
                    value = _syms.SymbolToAddress(symbol);
                else
                    throw new InvalidExpressionException($"The symbol '{symbol}' does not exist in the symbol file.");
                
                var capture = match.Groups[0].Value;
				
                expression = expression.Replace(capture, value.ToString());
            }

            return expression;
        }

        /// <summary>
        /// Replace all hex strings starting with $ with their int equivalent
        /// </summary>
        /// <param name="expression">The expression to process</param>
        /// <returns>The expression with hex values converted to integer</returns>
        private string ReplaceHexStrings(string expression)
        {
            foreach (Match match in Regex.Matches(expression, "(\\$[0-9a-f]+)", RegexOptions.IgnoreCase))
            {
                var hex = match.Groups[1].Value;
                var value = hex.ParseNumber();
                var capture = match.Groups[0].Value;

                expression = expression.Replace(capture, value.ToString());
            }

            return expression;
        }
    }
}