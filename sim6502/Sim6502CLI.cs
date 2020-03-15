using System;
using System.IO;
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

        /// <summary>
        /// TODO: Make this shit work in the nlog.config file.
        /// </summary>
        private Sim6502Cli()
        {
            var config = new NLog.Config.LoggingConfiguration();
            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, logconsole);
            LogManager.Configuration = config;
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
        /// <returns></returns>
        private int RunCli(Options opts)
        {
            int retval;
            try
            {
                var symbols = SymbolFile.LoadSymbolFile(opts.SymbolFile);
                var tests = TestYaml.DeserializeTestsYaml(opts.TestYaml);
                _expr = new ExpressionParser(_processor, symbols);

                var allPassed = tests.UnitTests.RunUnitTests(_processor, _expr, tests.Init.LoadFiles);
                var passed = allPassed ? "PASSED" : "FAILED";
                Logger.Info($"Complete Test Suite : {passed}");

                retval = allPassed ? 0 : 1;
            }
            catch(Exception ex)
            {
                Logger.Fatal(ex, $"Failed to run tests: {ex.Message}, {ex.StackTrace}");
                retval = 1;
            }

            return retval;
        }
    }
}