using System.IO;
using CommandLine;
using NLog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using sim6502.UnitTests;

namespace sim6502
{
    internal class Sim6502Cli
    {
        private Processor Processor { get; set; }
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
        
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
        public Sim6502Cli()
        {
            var config = new NLog.Config.LoggingConfiguration();
            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, logconsole);
            LogManager.Configuration = config;
        }
        
        private static int Main(string[] args)
        {
            var cli = new Sim6502Cli();
            
            Logger.Debug("Initializing 6502 simulator.");
            var proc = new Processor();
            proc.Reset();
            cli.Processor = proc;
            Logger.Debug("6502 simulator initialized and reset.");

            return Parser.Default
                .ParseArguments<Options>(args)
                .MapResult(
                    (Options opts) => cli.RunCli(opts),
                    errs => 1);
            
        }

        /// <summary>
        /// Run the 6502 test CLI
        /// </summary>
        /// <param name="opts">Options specified on the command line</param>
        /// <returns></returns>
        private int RunCli(Options opts)
        {
            try
            {
                var symbols = LoadSymbolFile(opts.SymbolFile);
                var tests = DeserializeTestsYaml(opts.TestYaml);
            }
            catch
            {
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// Deserialize our test yaml into a graph of objects
        /// </summary>
        /// <param name="testYamlFilename"></param>
        /// <returns></returns>
        private Tests DeserializeTestsYaml(string testYamlFilename)
        {
            Tests tests = null;
            
            if (!FileExists(testYamlFilename))
            {
                Logger.Fatal($"The yaml file specified '{testYamlFilename}' does not exist.");
                throw new FileNotFoundException();
            }
            
            try
            {
                var testYaml = File.ReadAllText(testYamlFilename);
            
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                
                tests = deserializer.Deserialize<Tests>(testYaml);
                foreach (var loadfile in tests.Init.LoadFiles)
                {
                    Logger.Debug($"Loading {loadfile.Filename} at {loadfile.Address}");
                    if (!FileExists(loadfile.Filename))
                    {
                        Logger.Fatal($"Failed to load {loadfile.Filename} - it does not exist.");
                        throw new FileNotFoundException();
                    }
                    else
                    {
                        LoadFile(loadfile.Filename, loadfile.AddressParsed);
                    }
                }
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
        private SymbolFile LoadSymbolFile(string symbolFilename)
        {
            if ("".Equals(symbolFilename) || symbolFilename == null)
                return null;
            
            if (!FileExists(symbolFilename))
            {
                Logger.Fatal($"The symbol file specified '{symbolFilename}' does not exist.");
                throw new FileNotFoundException();
            }

            var symbolFile = File.ReadAllText(symbolFilename);
            return new SymbolFile(symbolFile);
        }

        /// <summary>
        /// Check to see if a file exists. This is used for roms and c64 programs
        /// </summary>
        /// <param name="filename">The name of the file to check</param>
        /// <returns>true if the file exists, or false otherwise</returns>
        private bool FileExists(string filename)
        {
            return File.Exists(filename);
        }

        /// <summary>
        /// Load a file from disk into the 6502 sim's address space
        /// </summary>
        /// <param name="filename">The filename of the file to load</param>
        /// <param name="address">The 16-bit address within the 6502 sim to load the file</param>
        private void LoadFile(string filename, int address)
        {
            using var file = new FileStream(filename, FileMode.Open, FileAccess.Read);
            Processor.LoadProgram(address, StreamToBytes(file));
        }

        /// <summary>
        /// Convert a stream to a byte array
        /// </summary>
        /// <param name="stream">The stream to convert, which will be a FileStream</param>
        /// <returns>The contents of the stream as a byte[]</returns>
        private byte[] StreamToBytes(Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}