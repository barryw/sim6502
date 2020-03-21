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

        /// <summary>
        /// Check whether a string is null or empty
        /// </summary>
        /// <param name="str">The string to check</param>
        /// <returns></returns>
        public static bool Empty(this string str)
        {
            return string.IsNullOrEmpty(str);
        }

        /// <summary>
        /// Convert an integer to a hex string
        /// </summary>
        /// <param name="number">The number to convert</param>
        /// <returns></returns>
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

        /// <summary>
        /// Parse a string and see if we can get an integer out of it
        /// </summary>
        /// <param name="number">The thing to parse</param>
        /// <returns>The thing as an integer</returns>
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

        /// <summary>
        /// Given an assembled .prg file, return its load address. This will be the file's
        /// first two bytes in little endian format.
        /// </summary>
        /// <param name="filename">The path to the file to get the load address for. Must be a .prg</param>
        /// <returns>16-bit load address</returns>
        public static int GetProgramLoadAddress(string filename)
        {
            var buffer = new byte[2];
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                fs.Read(buffer, 0, 2);
            }

            return GetProgramLoadAddress(buffer);
        }

        /// <summary>
        /// Given a program as a byte array, return its load address
        /// </summary>
        /// <param name="program">The program expressed as a byte array</param>
        /// <returns>The 16-bit load address of the program</returns>
        public static int GetProgramLoadAddress(byte[] program)
        {
            return program[1] * 256 + program[0];
        }

        /// <summary>
        /// Load a rom or program into the processor's memory
        /// </summary>
        /// <param name="proc"></param>
        /// <param name="address"></param>
        /// <param name="filename"></param>
        /// <param name="stripHeader"></param>
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

        /// <summary>
        /// Convert a stream to a byte array
        /// </summary>
        /// <param name="stream">The stream to convert, which will be a FileStream</param>
        /// <returns>The contents of the stream as a byte[]</returns>
        private static IEnumerable<byte> StreamToBytes(Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Check to see if a file exists. This is used for roms and c64 programs
        /// </summary>
        /// <param name="filename">The name of the file to check</param>
        public static void FileExists(string filename)
        {
            if (File.Exists(filename)) return;
            Logger.Fatal($"The file '{filename}' does not exist.");
            throw new FileNotFoundException();
        }

        /// <summary>
        /// Allow us to pluralize strings
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public static string Pluralize(string message)
        {
            return string.Format(new PluralFormatProvider(), message);
        }
    }
}