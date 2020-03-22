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

using System.Collections.Generic;
using sim6502.Expressions;
using sim6502.Proc;
using sim6502.Utilities;
using YamlDotNet.Serialization;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable CollectionNeverUpdated.Global

namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class TestUnitTests
    {
        public int TotalTestsRan { get; set; }
        public int TotalTestsPassed { get; set; }
        public int TotalTestsFailed { get; set; }

        [YamlMember(Alias = "program", ApplyNamingConventions = false)]
        public string Program { get; set; }

        [YamlMember(Alias = "address", ApplyNamingConventions = false)]
        public string Address { get; set; }

        [YamlMember(Alias = "tests", ApplyNamingConventions = false)]
        public List<TestUnitTest> UnitTests { get; set; }

        public int AddressParsed => Address.ParseNumber();

        public bool RunUnitTests(Processor proc, ExpressionParser expr, IEnumerable<TestInitLoadFile> roms)
        {
            var allPassed = true;

            foreach (var test in UnitTests)
            {
                test.DoTestInit(proc, expr);
                LoadRoms(proc, expr, roms);
                LoadProgram(proc);

                var passed = test.RunUnitTest(proc, expr);
                if (passed)
                {
                    TotalTestsPassed++;
                }
                else
                {
                    allPassed = false;
                    TotalTestsFailed++;
                }

                TotalTestsRan++;
            }

            return allPassed;
        }

        private void LoadProgram(Processor proc)
        {
            var address = "".Equals(Address) || Address == null
                ? Utility.GetProgramLoadAddress(Program)
                : AddressParsed;
            Utility.LoadFileIntoProcessor(proc, address, Program, true);
        }

        private static void LoadRoms(Processor proc, ExpressionParser expr, IEnumerable<TestInitLoadFile> roms)
        {
            if (roms == null) return;
            foreach (var rom in roms) Utility.LoadFileIntoProcessor(proc, rom.AddressParsed, rom.Filename);
        }
    }
}