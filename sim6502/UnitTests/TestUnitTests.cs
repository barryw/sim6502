using System.Collections.Generic;
using YamlDotNet.Serialization;
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable CollectionNeverUpdated.Global

namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class TestUnitTests
    {
        [YamlMember(Alias = "program", ApplyNamingConventions = false)]
        public string Program { get; set; }
        [YamlMember(Alias = "address", ApplyNamingConventions = false)]
        public string Address { get; set; }
        [YamlMember(Alias = "tests", ApplyNamingConventions = false)]
        public List<TestUnitTest> UnitTests { get; set; }
        
        public int AddressParsed => Address.ParseNumber();

        /// <summary>
        /// Run all of the unit tests
        /// </summary>
        /// <param name="proc"></param>
        /// <param name="expr"></param>
        /// <param name="roms"></param>
        /// <returns>True if all tests completed successfully, False otherwise</returns>
        public bool RunUnitTests(Processor proc, ExpressionParser expr, IEnumerable<TestInitLoadFile> roms)
        {
            var allPassed = true;

            foreach (var test in UnitTests)
            {
                test.DoTestInit(proc, expr);
                LoadRoms(proc, expr, roms);
                LoadProgram(proc);
                
                var passed = test.RunUnitTest(proc, expr);
                if (!passed)
                    allPassed = false;
            }

            return allPassed;
        }

        /// <summary>
        /// Load the thing we're testing into the processor
        /// </summary>
        /// <param name="proc"></param>
        private void LoadProgram(Processor proc)
        {
            var address = "".Equals(Address) || Address == null ? Utility.GetProgramLoadAddress(Program) : AddressParsed;
            Utility.LoadFileIntoProcessor(proc, address, Program, true);
        }

        /// <summary>
        /// Load all of the required roms into the processor
        /// </summary>
        /// <param name="proc"></param>
        /// <param name="expr"></param>
        /// <param name="roms"></param>
        private static void LoadRoms(Processor proc, ExpressionParser expr, IEnumerable<TestInitLoadFile> roms)
        {
            foreach (var rom in roms)
            {
                Utility.LoadFileIntoProcessor(proc, rom.AddressParsed, rom.Filename);
            }
        }
    }
}