using System.IO;
using NLog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace sim6502.UnitTests
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class TestYaml
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
        
        /// <summary>
        /// Deserialize our test yaml into a graph of objects
        /// </summary>
        /// <param name="testYamlFilename">The path to the test yaml</param>
        /// <returns>An object graph for the tests that we want to run</returns>
        public static Tests DeserializeTestsYaml(string testYamlFilename)
        {
            Tests tests;
            
            try
            {
                Utility.FileExists(testYamlFilename);
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
    }
}