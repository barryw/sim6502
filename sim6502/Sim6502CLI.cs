using System;
using System.IO;
using CommandLine;
using NLog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
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
                var symbols = LoadSymbolFile(opts.SymbolFile);
                var tests = DeserializeTestsYaml(opts.TestYaml);
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

        /// <summary>
        /// Deserialize our test yaml into a graph of objects
        /// </summary>
        /// <param name="testYamlFilename"></param>
        /// <returns></returns>
        private static Tests DeserializeTestsYaml(string testYamlFilename)
        {
            Tests tests;
            Utility.FileExists(testYamlFilename);
            try
            {
                var testYaml = File.ReadAllText(testYamlFilename);
            
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                
                tests = deserializer.Deserialize<Tests>(testYaml);
            }
            catch (YamlDotNet.Core.YamlException ye)
            {
                Logger.Fatal($"Failed to parse test yaml file: {ye.Message}, {ye.InnerException.Message}");
                throw;
            }

            return tests;
        }
        
        /// <summary>
        /// Load the Kickassembler generated symbol file.
        /// </summary>
        /// <param name="symbolFilename">The path to the symbol file</param>
        /// <returns>A SymbolFile object that makes it easier to work with the symbols</returns>
        private static SymbolFile LoadSymbolFile(string symbolFilename)
        {
            if ("".Equals(symbolFilename) || symbolFilename == null)
                return null;
            
            Utility.FileExists(symbolFilename);
            
            var symbolFile = File.ReadAllText(symbolFilename);
            return new SymbolFile(symbolFile);
        }
    }
}