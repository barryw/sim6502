/*
Copyright (c) 2020 Barry Walker. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met: 

1. Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer. 
2. Redistributions in binary form must reproduce the above copyright notice,
this list of conditions and the following disclaimer in the documentation
and/or other materials provided with the distribution. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies, 
either expressed or implied, of the FreeBSD Project.
*/
using System;
using System.Data;
using System.Text.RegularExpressions;
using NCalc;
using NLog;
// ReSharper disable UnusedMember.Local

namespace sim6502
{
    /// <summary>
    /// Let's us express our assertions in a much nicer way
    /// </summary>
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
        /// <param name="expression">The expression that we'd like to evaluate</param>
        /// <returns>The integer value of the entire expression</returns>
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
                int value;
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