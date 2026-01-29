// sim6502-lsp/Program.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;

namespace sim6502_lsp;

class Program
{
    static async Task Main(string[] args)
    {
        var server = await LanguageServer.From(options =>
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(x => x
                    .AddNLog()
                    .SetMinimumLevel(LogLevel.Debug))
                .WithServices(ConfigureServices)
        );

        await server.WaitForExit;
    }

    static void ConfigureServices(IServiceCollection services)
    {
        // Services will be added here as we build features
    }
}
