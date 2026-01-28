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
    public class ErrorCollector
    {
        private readonly List<SimError> _errors = new();
        private string[] _sourceLines = Array.Empty<string>();
        private string _filePath = string.Empty;

        public void SetSource(string content, string filePath)
        {
            _sourceLines = content.Split('\n');
            _filePath = filePath;
        }

        public void AddError(ErrorPhase phase, int line, int column, int length, string message, string? hint = null)
        {
            _errors.Add(new SimError(
                ErrorSeverity.Error,
                phase,
                _filePath,
                line,
                column,
                length,
                message,
                hint));
        }

        public void AddWarning(ErrorPhase phase, int line, int column, int length, string message, string? hint = null)
        {
            _errors.Add(new SimError(
                ErrorSeverity.Warning,
                phase,
                _filePath,
                line,
                column,
                length,
                message,
                hint));
        }

        public bool HasErrors => _errors.Any(e => e.Severity == ErrorSeverity.Error);
        public bool HasWarnings => _errors.Any(e => e.Severity == ErrorSeverity.Warning);
        public int ErrorCount => _errors.Count(e => e.Severity == ErrorSeverity.Error);
        public int WarningCount => _errors.Count(e => e.Severity == ErrorSeverity.Warning);

        public IReadOnlyList<SimError> Errors => _errors.AsReadOnly();
        public string[] SourceLines => _sourceLines;
        public string FilePath => _filePath;

        public void Clear()
        {
            _errors.Clear();
        }
    }
}
