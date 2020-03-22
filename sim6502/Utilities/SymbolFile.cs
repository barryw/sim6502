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

        private Dictionary<string, int> Symbols { get; } = new Dictionary<string, int>();
        
        public SymbolFile(string symbolfile)
        {
            _symbolfile = symbolfile.Trim().Split(
                new[] {"\r\n", "\r", "\n"},
                StringSplitOptions.None
            );
            Parse();
        }
        
        private void Parse()
        {
            var currentNamespace = "";

            foreach (var line in _symbolfile)
            {
                var l = line.TrimStart();

                if (l.StartsWith(".label"))
                {
                    var m = Regex.Match(l, @".label\s+([A-Za-z0-9_]+)=([A-Fa-f0-9$]+)", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var label = m.Groups[1].Value;
                        var address = m.Groups[2].Value;

                        Symbols.Add("".Equals(currentNamespace) ? label : $"{currentNamespace}.{label}",
                            address.ParseNumber());
                    }
                }
                else if (l.StartsWith(".namespace"))
                {
                    var m = Regex.Match(l, @".namespace ([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        currentNamespace = m.Groups[1].Value;
                    }
                }
                else
                {
                    currentNamespace = "";
                }
            }
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