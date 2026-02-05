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

using System.Diagnostics;
using NLog;

namespace sim6502.Backend;

public class ViceLauncher : IDisposable
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    private Process? _viceProcess;
    private readonly int _port;

    public ViceLauncher(int port = 6510)
    {
        _port = port;
    }

    public void Launch()
    {
        var arguments = BuildArguments(_port);
        var executableName = FindViceExecutable();

        Logger.Info($"Launching VICE: {executableName} {arguments}");

        _viceProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executableName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        _viceProcess.Start();
        Logger.Info($"VICE started with PID {_viceProcess.Id}");

        WaitForMcpServer();
    }

    private void WaitForMcpServer()
    {
        var connection = new ViceConnection("127.0.0.1", _port);
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow - startTime < timeout)
        {
            if (connection.Ping())
            {
                Logger.Info("VICE MCP server is ready.");
                connection.Dispose();
                return;
            }
            Thread.Sleep(500);
        }

        connection.Dispose();
        throw new TimeoutException(
            $"VICE MCP server did not start within {timeout.TotalSeconds}s on port {_port}");
    }

    public static string BuildArguments(int port)
    {
        return $"-mcpserver -mcpserverport {port} +confirmexit";
    }

    private static string FindViceExecutable()
    {
        var candidates = new[] { "x64sc", "x64" };

        foreach (var name in candidates)
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = name,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });

                if (process == null) continue;
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    return output;
            }
            catch
            {
                // Continue to next candidate
            }
        }

        throw new FileNotFoundException(
            "Could not find VICE executable (x64sc or x64). " +
            "Ensure VICE is installed and available in PATH.");
    }

    public void Dispose()
    {
        if (_viceProcess is { HasExited: false })
        {
            Logger.Info("Stopping VICE process...");
            try
            {
                _viceProcess.Kill(entireProcessTree: true);
                _viceProcess.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to stop VICE cleanly: {ex.Message}");
            }
        }
        _viceProcess?.Dispose();
    }
}
