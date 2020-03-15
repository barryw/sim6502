using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace sim6502
{
    public class SymbolFile
    {
        private readonly string[] _symbolfile;

        private Dictionary<string, int> Symbols { get; } = new Dictionary<string, int>();

        /// <summary>
        /// Pass in the contents of the symbol file - not the path to it
        /// </summary>
        /// <param name="symbolfile">The contents of the symbolfile</param>
        public SymbolFile(string symbolfile)
        {
            _symbolfile = symbolfile.Trim().Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            );
            Parse();
        }

        /// <summary>
        /// Parse the symbol file
        /// </summary>
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
                } else if (l.StartsWith(".namespace"))
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

        /// <summary>
        /// Use the symbol file to translate a symbol to a 16-bit address
        /// </summary>
        /// <param name="symbol">The symbol to translate</param>
        /// <returns>The 16-bit address if found. An exception is thrown if it's not found</returns>
        public int SymbolToAddress(string symbol)
        {
            return Symbols[symbol];
        }

        /// <summary>
        /// Try and translate an address to a symbol if one exists. If not, just return the address as a string
        /// </summary>
        /// <param name="address">The address to try and get a symbol for</param>
        /// <param name="asHex">If set to true, will return the address as a hex string if the symbol isn't found</param>
        /// <returns></returns>
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
        
        /// <summary>
        /// Load the Kickassembler generated symbol file.
        /// </summary>
        /// <param name="symbolFilename">The path to the symbol file</param>
        /// <returns>A SymbolFile object that makes it easier to work with the symbols</returns>
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