using System.IO;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Dfa;
using Antlr4.Runtime.Sharpen;
using NLog;

namespace sim6502.Grammar
{
    public class SimErrorListener : BaseErrorListener
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine,
            string msg, RecognitionException e)
        {
            Logger.Fatal($"Line {line.ToString()}:{charPositionInLine.ToString()} {msg}");
            UnderlineError(recognizer, offendingSymbol, line, charPositionInLine);
        }

        private static void UnderlineError(IRecognizer recognizer, IToken offendingToken, int line, int charPosition)
        {
            var tokens = (CommonTokenStream)recognizer.InputStream;
            var input = tokens.TokenSource.InputStream.ToString();
            var lines = input.Split("\n");
            var errorLine = lines[line - 1];
            Logger.Fatal(errorLine);
            var output = new StringBuilder();
            
            for (var i = 0; i < charPosition; i++)
            {
                output.Append(" ");
            }
            var start = offendingToken.StartIndex;
            var stop = offendingToken.StopIndex;
            if (start >= 0 && stop >= 0)
            {
                for (var i = start; i <= stop; i++)
                {
                    output.Append("^");
                }
            }
            Logger.Fatal(output.ToString());
        }

        public override void ReportAmbiguity(Parser recognizer, DFA dfa, int startIndex, int stopIndex, bool exact, BitSet ambigAlts,
            ATNConfigSet configs)
        {
            base.ReportAmbiguity(recognizer, dfa, startIndex, stopIndex, exact, ambigAlts, configs);
        }
    }
}