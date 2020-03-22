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
using CommandLine;
using NLog;
using sim6502.Expressions;
using sim6502.Proc;
using sim6502.UnitTests;
using sim6502.Utilities;

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
            [Option('t', "trace", SetName = "trace", Required = false, Default = false,
                HelpText = "Enable or disable trace mode")]
            public bool Trace { get; set; }

            [Option('d', "debug", SetName = "debug", Required = false, Default = false,
                HelpText = "Enable or disable debug mode")]
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
            Logger.Info("https://github.com/barryw/sim6502");

            return Parser.Default
                .ParseArguments<Options>(args)
                .MapResult(
                    opts => cli.RunCli(opts),
                    errs => 1);
        }

        private int RunCli(Options opts)
        {
            SetLogLevel(opts);

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
                    string.Format(new PluralFormatProvider(),
                        "{0:test;tests} passed, {1:test;tests} failed, {2:test;tests} total.", numPassed, numFailed,
                        numRun));
                Logger.Log(allPassed ? LogLevel.Info : LogLevel.Fatal, $"Complete Test Suite : {disposition}");

                return allPassed ? 0 : 1;
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, $"Failed to run tests: {ex.Message}, {ex.StackTrace}");
                return 1;
            }
        }

        private static void SetLogLevel(Options opts)
        {
            LogManager.Configuration.Variables["cliLogLevel"] = LogLevel.Info.Name;
            if (opts.Debug)
                LogManager.Configuration.Variables["cliLogLevel"] = LogLevel.Debug.Name;
            if (opts.Trace)
                LogManager.Configuration.Variables["cliLogLevel"] = LogLevel.Trace.Name;

            LogManager.ReconfigExistingLoggers();
        }
    }
}