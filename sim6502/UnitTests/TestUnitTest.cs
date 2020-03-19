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

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies, 
either expressed or implied, of the FreeBSD Project.
*/

using System.Collections.Generic;
using System.Linq;
using NLog;
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

        [YamlMember(Alias = "set_memory", ApplyNamingConventions = false)]
        public List<TestUnitTestSetMemory> SetMemory { get; set; }

        [YamlMember(Alias = "jump_address", ApplyNamingConventions = false)]
        public string JumpAddress { get; set; }

        [YamlMember(Alias = "stop_on", ApplyNamingConventions = false)]
        public string StopOn { get; set; }

        [YamlMember(Alias = "assert", ApplyNamingConventions = false)]
        public List<TestAssertion> Assertions { get; set; }

        [YamlMember(Alias = "fail_on_brk", ApplyNamingConventions = false)]
        public bool FailOnBrk { get; set; }

        /// <summary>
        /// Run a single unit test
        /// </summary>
        /// <param name="proc">A reference to the running 6502</param>
        /// <param name="expr">A reference to our expression parser</param>
        /// <returns></returns>
        public bool RunUnitTest(Processor proc, ExpressionParser expr)
        {
            // Where is the code we want to test?
            var jumpAddress = expr.Evaluate(JumpAddress);
            Logger.Debug($"Running routine located at {jumpAddress.ToHex()}");
            var testPassed = proc.RunRoutine(jumpAddress, StopOn, FailOnBrk);

            // Run the test's assertions after we've run the code under test
            foreach (var unused in Assertions.Select(assertion => assertion.PerformAssertion(proc, expr, this))
                .Where(assertionPassed => !assertionPassed))
            {
                testPassed = false;
            }

            var disposition = testPassed ? "PASSED" : "FAILED";
            Logger.Log(testPassed ? LogLevel.Info : LogLevel.Fatal, $"{Name} : {Description} : {disposition}");

            return testPassed;
        }

        /// <summary>
        /// Called before every test to set everything back to the state that the tests expect
        /// </summary>
        /// <param name="proc">A reference to the running 6502</param>
        /// <param name="expr">A reference to our expression parser</param>
        public void DoTestInit(Processor proc, ExpressionParser expr)
        {
            proc.ResetMemory();
            if (SetMemory == null) return;
            
            foreach (var mem in SetMemory)
            {
                mem.SetMemory(proc, expr);
            }
        }
    }
}