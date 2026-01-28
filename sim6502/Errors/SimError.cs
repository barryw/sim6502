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

namespace sim6502.Errors
{
    public enum ErrorSeverity
    {
        Warning,
        Error
    }

    public enum ErrorPhase
    {
        Lexer,
        Parser,
        Semantic,
        Runtime
    }

    public class SimError
    {
        public ErrorSeverity Severity { get; }
        public ErrorPhase Phase { get; }
        public string FilePath { get; }
        public int Line { get; }
        public int Column { get; }
        public int Length { get; }
        public string Message { get; }
        public string? Hint { get; }

        public SimError(
            ErrorSeverity severity,
            ErrorPhase phase,
            string filePath,
            int line,
            int column,
            int length,
            string message,
            string? hint = null)
        {
            Severity = severity;
            Phase = phase;
            FilePath = filePath;
            Line = line;
            Column = column;
            Length = Math.Max(1, length);
            Message = message;
            Hint = hint;
        }

        public override string ToString()
        {
            var severity = Severity == ErrorSeverity.Error ? "Error" : "Warning";
            return $"{severity} at {FilePath}:{Line}:{Column} - {Message}";
        }
    }
}
