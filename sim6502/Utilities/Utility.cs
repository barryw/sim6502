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
using NLog;
using sim6502.Proc;

namespace sim6502.Utilities
{
    public static class Utility
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        public static bool Empty(this string str)
        {
            return string.IsNullOrEmpty(str);
        }

        public static string ToHex(this int number)
        {
            var numDigits = 1;
            if (number >= 16)
                numDigits++;
            if (number >= 256)
                numDigits++;
            if (number >= 4096)
                numDigits++;

            var hex = number.ToString($"X{numDigits}").ToLower();
            return $"${hex}";
        }

        public static int ParseNumber(this string number)
        {
            int retval;
            if (number.StartsWith("$"))
            {
                number = number.Replace("$", "0x");
                retval = Convert.ToInt32(number, 16);
            }
            else if (number.StartsWith("0x"))
            {
                retval = Convert.ToInt32(number, 16);
            }
            else
            {
                retval = int.Parse(number);
            }

            return retval;
        }

        public static int GetProgramLoadAddress(string filename)
        {
            var buffer = new byte[2];
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                fs.Read(buffer, 0, 2);
            }

            return GetProgramLoadAddress(buffer);
        }

        public static int GetProgramLoadAddress(byte[] program)
        {
            return program[1] * 256 + program[0];
        }

        public static void LoadFileIntoProcessor(Processor proc, int address, string filename, bool stripHeader = false)
        {
            Logger.Debug($"Loading {filename} @ {address.ToHex()}");
            FileExists(filename);
            using var file = new FileStream(filename, FileMode.Open, FileAccess.Read);
            var program = new List<byte>(StreamToBytes(file));

            if (stripHeader)
            {
                program.RemoveAt(0);
                program.RemoveAt(0);
            }

            proc.LoadProgram(address, program.ToArray());
        }

        private static IEnumerable<byte> StreamToBytes(Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        public static void FileExists(string filename)
        {
            if (File.Exists(filename)) return;
            Logger.Fatal($"The file '{filename}' does not exist.");
            throw new FileNotFoundException();
        }
    }
}