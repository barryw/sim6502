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

using System.Text;

namespace sim6502.Errors
{
    public static class ErrorRenderer
    {
        private const int ContextLines = 1;

        public static string Render(ErrorCollector collector)
        {
            return Render(collector.Errors, collector.SourceLines, collector.FilePath);
        }

        public static string Render(IReadOnlyList<SimError> errors, string[] sourceLines, string filePath)
        {
            if (errors.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            var sortedErrors = errors.OrderBy(e => e.Line).ThenBy(e => e.Column).ToList();

            // Calculate line number width for padding
            var maxLine = sortedErrors.Max(e => Math.Min(e.Line + ContextLines, sourceLines.Length));
            var lineWidth = maxLine.ToString().Length;

            foreach (var error in sortedErrors)
            {
                RenderError(sb, error, sourceLines, lineWidth);
                sb.AppendLine();
            }

            // Summary
            RenderSummary(sb, errors, filePath);

            return sb.ToString();
        }

        private static void RenderError(StringBuilder sb, SimError error, string[] sourceLines, int lineWidth)
        {
            // Header line
            var severity = error.Severity == ErrorSeverity.Error ? "Error" : "Warning";
            sb.AppendLine($"{severity} at {error.FilePath}:{error.Line}:{error.Column} - {error.Message}");

            // Context lines before
            var startLine = Math.Max(1, error.Line - ContextLines);
            var endLine = Math.Min(sourceLines.Length, error.Line + ContextLines);

            for (var i = startLine; i <= endLine; i++)
            {
                var lineContent = i <= sourceLines.Length ? sourceLines[i - 1] : string.Empty;
                var lineNum = i.ToString().PadLeft(lineWidth);

                if (i == error.Line)
                {
                    // Error line
                    sb.AppendLine($"  {lineNum} | {lineContent}");

                    // Pointer line
                    var pointer = BuildPointer(error.Column, error.Length, lineWidth);
                    sb.AppendLine(pointer);
                }
                else
                {
                    // Context line
                    sb.AppendLine($"  {lineNum} | {lineContent}");
                }
            }

            // Hint
            if (!string.IsNullOrEmpty(error.Hint))
            {
                sb.AppendLine($"  Hint: {error.Hint}");
            }
        }

        private static string BuildPointer(int column, int length, int lineWidth)
        {
            var sb = new StringBuilder();

            // Padding for line number area: "  " + lineWidth + " | "
            sb.Append(new string(' ', 2 + lineWidth + 3));

            // Padding to column position
            sb.Append(new string(' ', column));

            // Pointer characters
            sb.Append(new string('^', length));

            return sb.ToString();
        }

        private static void RenderSummary(StringBuilder sb, IReadOnlyList<SimError> errors, string filePath)
        {
            var errorCount = errors.Count(e => e.Severity == ErrorSeverity.Error);
            var warningCount = errors.Count(e => e.Severity == ErrorSeverity.Warning);

            var parts = new List<string>();

            if (errorCount > 0)
                parts.Add($"{errorCount} error{(errorCount == 1 ? "" : "s")}");

            if (warningCount > 0)
                parts.Add($"{warningCount} warning{(warningCount == 1 ? "" : "s")}");

            var fileName = Path.GetFileName(filePath);
            sb.AppendLine($"Found {string.Join(" and ", parts)} in {fileName}");
        }
    }
}
