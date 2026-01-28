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
    public static class SuggestionEngine
    {
        private static readonly string[] Functions =
        {
            "jsr", "assert", "memfill", "memdump", "peekbyte", "peekword",
            "memorycmp", "memorychk", "load", "symbols", "test", "suite",
            "setup", "suites"
        };

        private static readonly string[] Options =
        {
            "stop_on_rts", "stop_on_address", "fail_on_brk", "strip_header",
            "timeout", "skip", "trace", "tags"
        };

        private static readonly string[] Registers = { "A", "X", "Y", "SP", "PC" };
        private static readonly string[] Flags = { "C", "Z", "I", "D", "B", "V", "N" };

        private static readonly string[] AllKeywords = Functions
            .Concat(Options)
            .Concat(Registers)
            .Concat(Flags)
            .ToArray();

        public static string? SuggestKeyword(string input)
        {
            return FindClosest(input, AllKeywords, maxDistance: 2);
        }

        public static string? SuggestFunction(string input)
        {
            return FindClosest(input, Functions, maxDistance: 2);
        }

        public static string? SuggestRegister(string input)
        {
            return FindClosest(input, Registers, maxDistance: 1);
        }

        public static string? SuggestFlag(string input)
        {
            return FindClosest(input, Flags, maxDistance: 1);
        }

        public static string? SuggestSymbol(string input, IEnumerable<string> symbols)
        {
            return FindClosest(input, symbols, maxDistance: 3);
        }

        private static string? FindClosest(string input, IEnumerable<string> candidates, int maxDistance)
        {
            if (string.IsNullOrEmpty(input))
                return null;

            string? bestMatch = null;
            var bestDistance = int.MaxValue;
            var matchCount = 0;

            foreach (var candidate in candidates)
            {
                var distance = LevenshteinDistance(input.ToLowerInvariant(), candidate.ToLowerInvariant());

                if (distance < bestDistance && distance <= maxDistance)
                {
                    bestDistance = distance;
                    bestMatch = candidate;
                    matchCount = 1;
                }
                else if (distance == bestDistance && distance <= maxDistance)
                {
                    matchCount++;
                }
            }

            // Only return if exactly one close match (avoid ambiguous suggestions)
            return matchCount == 1 ? bestMatch : null;
        }

        /// <summary>
        /// Calculate the Levenshtein distance between two strings.
        /// This is the minimum number of single-character edits (insertions, deletions, substitutions)
        /// required to change one string into the other.
        /// </summary>
        public static int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
                return string.IsNullOrEmpty(target) ? 0 : target.Length;

            if (string.IsNullOrEmpty(target))
                return source.Length;

            var sourceLength = source.Length;
            var targetLength = target.Length;

            var matrix = new int[sourceLength + 1, targetLength + 1];

            // Initialize first column
            for (var i = 0; i <= sourceLength; i++)
                matrix[i, 0] = i;

            // Initialize first row
            for (var j = 0; j <= targetLength; j++)
                matrix[0, j] = j;

            // Fill the matrix
            for (var i = 1; i <= sourceLength; i++)
            {
                for (var j = 1; j <= targetLength; j++)
                {
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;

                    matrix[i, j] = Math.Min(
                        Math.Min(
                            matrix[i - 1, j] + 1,      // Deletion
                            matrix[i, j - 1] + 1),     // Insertion
                        matrix[i - 1, j - 1] + cost);  // Substitution
                }
            }

            return matrix[sourceLength, targetLength];
        }
    }
}
