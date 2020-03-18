using System;
using CommandLine;
using NLog;
using sim6502.UnitTests;
// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace sim6502
{
    internal class Sim6502Cli
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
        
        private Processor _processor;
        private ExpressionParser _expr;
        
        // ReSharper disable once ClassNeverInstantiated.Local
        private class Options
        {
            [Option('d', "debug", Required = false, Default = false, HelpText = "Enable or disable debug mode")]
            public bool Debug { get; set; }
            
            [Option('s', "symbolfile", Required = false, HelpText = "The path to the generated symbols file")]
            public string SymbolFile { get; set; }
            
            [Option('y', "yaml", Required = true, HelpText = "The path to your yaml test spec file.")]
            public string TestYaml { get; set; }
        }

        private static int Main(string[] args)
        {
            var cli = new Sim6502Cli();
            
            Logger.Debug("Initializing 6502 simulator.");
            cli._processor = new Processor();
            cli._processor.Reset();
            Logger.Debug("6502 simulator initialized and reset.");
            Logger.Info("6502 Simulator Test Runner CLI Copyright © 2020 Barry Walker. All Rights Reserved.");
            
            return Parser.Default
                .ParseArguments<Options>(args)
                .MapResult(
                    opts => cli.RunCli(opts),
                    errs => 1);
            
        }

        /// <summary>
        /// Run the 6502 test CLI
        /// </summary>
        /// <param name="opts">Options specified on the command line</param>
        /// <returns>0 if all tests passed, >0 otherwise</returns>
        private int RunCli(Options opts)
        {
            LogManager.Configuration.Variables["cliLogLevel"] = opts.Debug ? "Debug" : "Info";
            LogManager.ReconfigExistingLoggers();
            
            int retval;
            try
            {
                var symbols = SymbolFile.LoadSymbolFile(opts.SymbolFile);
                var tests = TestYaml.DeserializeTestsYaml(opts.TestYaml);
                _expr = new ExpressionParser(_processor, symbols);

                var allPassed = tests.UnitTests.RunUnitTests(_processor, _expr, tests.Init.LoadFiles);
                var numPassed = tests.UnitTests.TotalTestsPassed;
                var numFailed = tests.UnitTests.TotalTestsFailed;
                var numRun = tests.UnitTests.TotalTestsRan;
                
                var disposition = allPassed ? "PASSED" : "FAILED";
                Logger.Info(
                    string.Format(new PluralFormatProvider(), "{0:test;tests} passed, {1:test;tests} failed, {2:test;tests} total.", numPassed, numFailed, numRun));
                Logger.Log(allPassed ? LogLevel.Info : LogLevel.Fatal, $"Complete Test Suite : {disposition}");
                

                retval = allPassed ? 0 : 1;
            }
            catch(Exception ex)
            {
                Logger.Fatal(ex, $"Failed to run tests: {ex.Message}");
                retval = 1;
            }

            return retval;
        }
    }
}