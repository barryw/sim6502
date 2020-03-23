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
*/

using System;
using System.Data;
using System.Text.RegularExpressions;
using NCalc;
using NLog;
using sim6502.Proc;
using sim6502.UnitTests;
using sim6502.Utilities;

// ReSharper disable UnusedMember.Local

namespace sim6502.Expressions
{
    public class ExpressionParser
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        private const string HexSearchString = "(\\$[0-9a-f]+)";
        private const string SymbolSearchString = "{([0-9a-zA-Z_.]+)}";

        private class ExpressionContext
        {
            public Processor Processor { get; set; }

            public byte Peekbyte(int address)
            {
                var val = Processor.ReadMemoryValueWithoutCycle(address);
                Logger.Trace($"peekbyte({address.ToString()}) = {val.ToString()}");
                return val;
            }

            public int Peekword(int address)
            {
                var val = Processor.ReadMemoryWordWithoutCycle(address);
                Logger.Trace($"peekword({address.ToString()}) = {val.ToString()}");
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

        public int Evaluate(string expression, TestUnitTest test, TestAssertion assertion)
        {
            try
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
                Logger.Trace($"Final expression value = {val.ToString()}");
                return val;
            }
            catch (InvalidExpressionException iex)
            {
                if (test == null && assertion == null)
                    Logger.Fatal(iex, $"{iex.Message}");
                else if (test != null && assertion == null)
                    Logger.Fatal(iex, $"{iex.Message} in test '{test.Name}'");
                else
                    Logger.Fatal(iex, $"{iex.Message} in assertion '{assertion.Description}' of test '{test.Name}'");

                return -1;
            }
        }

        private string ReplaceSymbols(string expression)
        {
            foreach (Match match in GetMatches(expression, SymbolSearchString))
            {
                var symbol = match.Groups[1].Value;
                int value;
                if (_syms.SymbolExists(symbol))
                    value = _syms.SymbolToAddress(symbol);
                else
                    throw new InvalidExpressionException($"The symbol '{symbol}' does not exist in the symbol file");

                var capture = match.Groups[0].Value;

                expression = expression.Replace(capture, value.ToString());
            }

            return expression;
        }

        private static string ReplaceHexStrings(string expression)
        {
            foreach (Match match in GetMatches(expression, HexSearchString))
            {
                var hex = match.Groups[1].Value;
                var value = hex.ParseNumber();
                var capture = match.Groups[0].Value;

                expression = expression.Replace(capture, value.ToString());
            }

            return expression;
        }

        private static MatchCollection GetMatches(string expression, string pattern)
        {
            return Regex.Matches(expression, pattern, RegexOptions.IgnoreCase);
        }
    }
}