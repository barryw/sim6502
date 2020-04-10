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
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace sim6502.Utilities
{
    public class SymbolFile
    {
        private readonly string[] _symbolfile;

        private readonly string[] _lineTerminationCharacters = {"\r\n", "\r", "\n"};
        private const string LabelConstant = ".label";
        private const string NamespaceConstant = ".namespace";
        private const string LabelRegex = @".label\s+([A-Za-z0-9_]+)=([A-Fa-f0-9$]+)";
        private const string NamespaceRegex = @".namespace ([A-Za-z0-9_]+)";
        
        private Dictionary<string, int> Symbols { get; } = new Dictionary<string, int>();

        public int SymbolCount => Symbols.Count;

        public SymbolFile(string symbolfile)
        {
            _symbolfile = symbolfile.Trim().Split(
                _lineTerminationCharacters,
                StringSplitOptions.None
            );
            ParseSymbolFile();
        }

        public SymbolFile(Dictionary<string, int> symbols)
        {
            Symbols = symbols;
        }

        private void ParseSymbolFile()
        {
            var currentNamespace = "";

            foreach (var currentLine in _symbolfile)
            {
                var trimmedLine = currentLine.TrimStart();

                if (trimmedLine.StartsWith(LabelConstant))
                {
                    ProcessFoundLabel(trimmedLine, currentNamespace);
                }
                else if (trimmedLine.StartsWith(NamespaceConstant))
                {
                    currentNamespace = ProcessFoundNamespace(trimmedLine);
                }
                else
                {
                    currentNamespace = "";
                }
            }
        }

        private void ProcessFoundLabel(string currentLine, string currentNamespace)
        {
            var labelMatch = Regex.Match(currentLine, LabelRegex, RegexOptions.IgnoreCase);
            if (!labelMatch.Success) return;
            
            var label = labelMatch.Groups[1].Value;
            var address = labelMatch.Groups[2].Value;

            Symbols.Add(currentNamespace.Empty() ? label : $"{currentNamespace}.{label}",
                address.ParseNumber());
        }

        private static string ProcessFoundNamespace(string currentLine)
        {
            var namespaceMatch = Regex.Match(currentLine, NamespaceRegex, RegexOptions.IgnoreCase);
            return !namespaceMatch.Success ? null : namespaceMatch.Groups[1].Value;
        }

        public bool SymbolExists(string symbol)
        {
            return Symbols.ContainsKey(symbol);
        }

        public int SymbolToAddress(string symbol)
        {
            return Symbols[symbol];
        }

        public string AddressToSymbol(int address, bool asHex = true)
        {
            var symbol = asHex ? address.ToHex() : address.ToString();

            foreach (var (key, value) in Symbols)
            {
                if (value != address) continue;
                symbol = key;
                break;
            }

            return symbol;
        }

        public static SymbolFile LoadSymbolFile(string symbolFilename)
        {
            if ("".Equals(symbolFilename) || symbolFilename == null)
                return null;

            Utility.FileExists(symbolFilename);

            var symbolFile = File.ReadAllText(symbolFilename);
            return new SymbolFile(symbolFile);
        }
    }
}