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

using System.IO;
using Antlr4.Runtime;
using sim6502.Errors;

namespace sim6502.Grammar
{
    /// <summary>
    /// Error listener that collects lexer and parser errors into an ErrorCollector.
    /// Implements both BaseErrorListener (for parser) and IAntlrErrorListener&lt;int&gt; (for lexer).
    /// </summary>
    public class SimErrorListener : BaseErrorListener, IAntlrErrorListener<int>
    {
        private readonly ErrorCollector _collector;

        public SimErrorListener(ErrorCollector collector)
        {
            _collector = collector;
        }

        /// <summary>
        /// Handle parser syntax errors.
        /// </summary>
        public override void SyntaxError(
            TextWriter output,
            IRecognizer recognizer,
            IToken offendingSymbol,
            int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            var tokenText = offendingSymbol?.Text ?? "";
            var tokenLength = tokenText.Length > 0 ? tokenText.Length : 1;

            // Try to provide a helpful suggestion
            var hint = GenerateHint(tokenText, msg);

            _collector.AddError(
                ErrorPhase.Parser,
                line,
                charPositionInLine,
                tokenLength,
                CleanMessage(msg),
                hint);
        }

        /// <summary>
        /// Handle lexer syntax errors.
        /// </summary>
        public void SyntaxError(
            TextWriter output,
            IRecognizer recognizer,
            int offendingSymbol,
            int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            _collector.AddError(
                ErrorPhase.Lexer,
                line,
                charPositionInLine,
                1,
                CleanMessage(msg),
                null);
        }

        /// <summary>
        /// Clean up ANTLR's default error messages to be more user-friendly.
        /// </summary>
        private static string CleanMessage(string msg)
        {
            // Replace ANTLR's token names with more readable versions
            msg = msg.Replace("'<EOF>'", "end of file");
            msg = msg.Replace("EOF", "end of file");

            // Simplify "no viable alternative" messages
            if (msg.Contains("no viable alternative"))
            {
                msg = "unexpected token";
            }

            // Simplify "mismatched input" messages
            if (msg.StartsWith("mismatched input"))
            {
                var parts = msg.Split(" expecting ");
                if (parts.Length == 2)
                {
                    msg = $"unexpected {parts[0].Replace("mismatched input ", "")}, expected {CleanExpectingList(parts[1])}";
                }
            }

            // Simplify "extraneous input" messages
            if (msg.StartsWith("extraneous input"))
            {
                msg = msg.Replace("extraneous input", "unexpected");
            }

            // Simplify "missing" messages
            if (msg.StartsWith("missing "))
            {
                var expected = msg.Replace("missing ", "");
                msg = $"missing {CleanExpectingList(expected)}";
            }

            return msg;
        }

        /// <summary>
        /// Clean up ANTLR's "expecting" lists to be more readable.
        /// </summary>
        private static string CleanExpectingList(string expecting)
        {
            // Remove curly braces from sets
            expecting = expecting.Trim('{', '}');

            // Replace common token names
            expecting = expecting.Replace("'('", "'('");
            expecting = expecting.Replace("')'", "')'");
            expecting = expecting.Replace("'{'", "'{'");
            expecting = expecting.Replace("'}'", "'}'");
            expecting = expecting.Replace("','", "','");

            return expecting;
        }

        /// <summary>
        /// Generate a helpful hint based on the error context.
        /// </summary>
        private static string? GenerateHint(string tokenText, string msg)
        {
            if (string.IsNullOrEmpty(tokenText) || tokenText == "<EOF>")
                return null;

            // Try to suggest a similar keyword
            var suggestion = SuggestionEngine.SuggestKeyword(tokenText);
            if (suggestion != null)
            {
                return $"Did you mean '{suggestion}'?";
            }

            // Provide hints for common mistakes
            if (msg.Contains("expecting ')'"))
            {
                return "Check for matching parentheses";
            }

            if (msg.Contains("expecting '}'"))
            {
                return "Check for matching braces";
            }

            if (msg.Contains("expecting '\"'"))
            {
                return "Check for matching quotes";
            }

            return null;
        }
    }
}
