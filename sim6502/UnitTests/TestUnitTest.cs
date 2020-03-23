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
using System.Linq;
using NLog;
using sim6502.Expressions;
using sim6502.Proc;
using sim6502.Utilities;
using YamlDotNet.Serialization;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable CollectionNeverUpdated.Global

namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class TestUnitTest
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        [YamlMember(Alias = "name", ApplyNamingConventions = false)]
        public string Name { get; set; }

        [YamlMember(Alias = "description", ApplyNamingConventions = false)]
        public string Description { get; set; }

        [YamlMember(Alias = "set_registers", ApplyNamingConventions = false)]
        public List<TestUnitTestSetRegisters> SetRegisters { get; set; }
        
        [YamlMember(Alias = "set_memory", ApplyNamingConventions = false)]
        public List<TestUnitTestSetMemory> SetMemory { get; set; }

        [YamlMember(Alias = "jump_address", ApplyNamingConventions = false)]
        public string JumpAddress { get; set; }

        [YamlMember(Alias = "stop_on", ApplyNamingConventions = false)]
        public string StopOn { get; set; } = "rts";

        [YamlMember(Alias = "assert", ApplyNamingConventions = false)]
        public List<TestAssertion> Assertions { get; set; }

        [YamlMember(Alias = "fail_on_brk", ApplyNamingConventions = false)]
        public bool FailOnBrk { get; set; } = true;

        public bool RunUnitTest(Processor proc, ExpressionParser expr)
        {
            var jumpAddress = expr.Evaluate(JumpAddress, this, null);
            if (jumpAddress == -1)
                return false;

            Logger.Debug($"Running routine located at {jumpAddress.ToHex()}");
            var testPassed = proc.RunRoutine(jumpAddress, StopOn, FailOnBrk);

            foreach (var unused in Assertions.Select(assertion => assertion.PerformAssertion(proc, expr, this))
                .Where(assertionPassed => !assertionPassed))
                testPassed = false;

            var disposition = testPassed ? "PASSED" : "FAILED";
            Logger.Log(testPassed ? LogLevel.Info : LogLevel.Fatal, $"{Name} : {Description} : {disposition}");

            return testPassed;
        }

        public void DoTestInit(Processor proc, ExpressionParser expr)
        {
            DoSetMemory(proc, expr);
            DoSetRegisters(proc, expr);
        }

        private void DoSetMemory(Processor proc, ExpressionParser expr)
        {
            proc.ResetMemory();
            if (SetMemory == null) return;

            foreach (var mem in SetMemory) mem.SetMemory(proc, expr, this);
        }

        private void DoSetRegisters(Processor proc, ExpressionParser expr)
        {
            if (SetRegisters == null) return;
            foreach(var reg in SetRegisters) reg.SetRegisters(proc, expr, this);
        }
    }
}