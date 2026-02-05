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
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using CommandLine;
using NLog;
using sim6502.Backend;
using sim6502.Errors;
using sim6502.Grammar;
using sim6502.Grammar.Generated;
using Parser = CommandLine.Parser;

// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace sim6502
{
    internal static class ExitCode
    {
        public const int Success = 0;
        public const int TestFailure = 1;
        public const int ParseError = 2;
        public const int SemanticError = 3;
    }

    internal class Sim6502Cli
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        // ReSharper disable once ClassNeverInstantiated.Local
        private class Options
        {
            [Option('t', "trace", SetName = "trace", Required = false, Default = false,
                HelpText = "Enable or disable trace mode")]
            public bool Trace { get; set; }

            [Option('d', "debug", SetName = "debug", Required = false, Default = false,
                HelpText = "Enable or disable debug mode")]
            public bool Debug { get; set; }

            [Option('s', "suitefile", Required = true, HelpText = "The path to your suite file which contains your test suites")]
            public string SuiteFile { get; set; }

            [Option("filter", Required = false, HelpText = "Glob pattern for test names (e.g., 'castle*')")]
            public string? FilterPattern { get; set; }

            [Option("test", Required = false, HelpText = "Run single test by exact name")]
            public string? SingleTest { get; set; }

            [Option("filter-tag", Required = false, HelpText = "Comma-separated tags to include (OR logic)")]
            public string? FilterTags { get; set; }

            [Option("exclude-tag", Required = false, HelpText = "Comma-separated tags to exclude")]
            public string? ExcludeTags { get; set; }

            [Option("list", Required = false, HelpText = "List matching tests without running")]
            public bool ListOnly { get; set; }

            [Option("backend", Required = false, Default = "sim",
                HelpText = "Execution backend: 'sim' for internal simulator, 'vice' for VICE MCP")]
            public string Backend { get; set; } = "sim";

            [Option("vice-host", Required = false, Default = "127.0.0.1",
                HelpText = "VICE MCP server host")]
            public string ViceHost { get; set; } = "127.0.0.1";

            [Option("vice-port", Required = false, Default = 6510,
                HelpText = "VICE MCP server port")]
            public int VicePort { get; set; } = 6510;

            [Option("vice-timeout", Required = false, Default = 5000,
                HelpText = "Timeout in ms for VICE execution per test")]
            public int ViceTimeout { get; set; } = 5000;

            [Option("vice-warp", Required = false, Default = true,
                HelpText = "Enable warp mode in VICE during tests")]
            public bool ViceWarp { get; set; } = true;

            [Option("launch-vice", Required = false, Default = false,
                HelpText = "Auto-launch VICE process")]
            public bool LaunchVice { get; set; }
        }

        private static int Main(string[] args)
        {
            var assembly = Assembly.GetEntryAssembly()?.Location;
            var versionInfo = FileVersionInfo.GetVersionInfo(assembly);

            Logger.Info($"{versionInfo.ProductName} v{versionInfo.ProductVersion} {versionInfo.LegalCopyright}");
            Logger.Info("https://github.com/barryw/sim6502");

            return Parser.Default
                .ParseArguments<Options>(args)
                .MapResult(
                    RunCli,
                    errs => 1);
        }

        private static int RunCli(Options opts)
        {
            SetLogLevel(opts);

            try
            {
                if(!File.Exists(opts.SuiteFile))
                    throw new FileNotFoundException($"The suite file {opts.SuiteFile} could not be found.");

                return RunTests(opts);
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, $"Failed to run tests: {ex.Message}, {ex.StackTrace}");
                return ExitCode.TestFailure;
            }
        }

        private static int RunTests(Options opts)
        {
            ViceLauncher? viceLauncher = null;
            try
            {
                if (opts.LaunchVice && opts.Backend == "vice")
                {
                    viceLauncher = new ViceLauncher(opts.VicePort);
                    viceLauncher.Launch();
                }

                // Create error collector and load source
                var collector = new ErrorCollector();
                var source = File.ReadAllText(opts.SuiteFile);
                collector.SetSource(source, opts.SuiteFile);

                // Setup lexer with error listener
                var inputStream = new AntlrInputStream(source);
                var lexer = new sim6502Lexer(inputStream);
                lexer.RemoveErrorListeners();
                lexer.AddErrorListener(new SimErrorListener(collector));

                // Setup parser with error listener
                var tokens = new CommonTokenStream(lexer);
                var parser = new sim6502Parser(tokens) { BuildParseTree = true };
                parser.RemoveErrorListeners();
                parser.AddErrorListener(new SimErrorListener(collector));

                // Parse
                var tree = parser.suites();

                // Check for parse errors - don't walk a broken tree
                if (collector.HasErrors)
                {
                    return ReportErrorsAndExit(collector, ExitCode.ParseError);
                }

                // Walk tree, collecting semantic/runtime errors
                var walker = new ParseTreeWalker();
                var sbl = new SimBaseListener
                {
                    FilterPattern = opts.FilterPattern,
                    SingleTest = opts.SingleTest,
                    FilterTags = opts.FilterTags,
                    ExcludeTags = opts.ExcludeTags,
                    ListOnly = opts.ListOnly,
                    Errors = collector,
                    BackendType = opts.Backend,
                    ViceConfig = opts.Backend == "vice" ? new ViceBackendConfig
                    {
                        Host = opts.ViceHost,
                        Port = opts.VicePort,
                        TimeoutMs = opts.ViceTimeout,
                        WarpMode = opts.ViceWarp
                    } : null
                };

                walker.Walk(sbl, tree);

                // Check for semantic errors
                if (collector.HasErrors)
                {
                    return ReportErrorsAndExit(collector, ExitCode.SemanticError);
                }

                // Report any warnings (but don't fail)
                if (collector.HasWarnings)
                {
                    var output = ErrorRenderer.Render(collector);
                    Console.Error.WriteLine(output);
                }

                return sbl.TotalSuitesFailed == 0 ? ExitCode.Success : ExitCode.TestFailure;
            }
            finally
            {
                viceLauncher?.Dispose();
            }
        }

        private static int ReportErrorsAndExit(ErrorCollector collector, int exitCode)
        {
            var output = ErrorRenderer.Render(collector);
            Console.Error.WriteLine(output);
            return exitCode;
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
