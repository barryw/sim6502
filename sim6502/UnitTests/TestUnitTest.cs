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
            var testPassed = true;

            // Where is the code we want to test?
            var jumpAddress = expr.Evaluate(JumpAddress);
            Logger.Debug($"Running routine located at {jumpAddress.ToHex()}");
            var funcExecuted = proc.RunRoutine(jumpAddress, StopOn, FailOnBrk);
            if (!funcExecuted)
                testPassed = false;

            // Run the test's assertions after we've run the code under test
            foreach (var unused in Assertions.Select(assertion => assertion.PerformAssertion(proc, expr, this)).Where(assertionPassed => !assertionPassed))
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