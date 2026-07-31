using System;
using System.IO;
using System.Linq;
using System.Threading;
using FluentAssertions;
using sim6502;
using sim6502.Backend;
using Xunit;

namespace sim6502tests;

/// <summary>
/// Exercises the CLI entry points (RunCli/RunTests/BuildBackendConfigs) directly
/// via InternalsVisibleTo, rather than spawning the built binary as a
/// subprocess. Covers the happy path, the documented failure exit codes, the
/// backend-disposal fix in RunTests' finally block, and CLI-option-to-backend-
/// config wiring.
/// </summary>
public class Sim6502CliTests
{
    private static Sim6502Cli.Options NewOptions(string suiteFile, string backend = "sim") => new()
    {
        SuiteFile = suiteFile,
        Backend = backend
    };

    private static string WriteTempSuite(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), "sim6502-clitest-" + Guid.NewGuid().ToString("N") + ".suite");
        File.WriteAllText(path, contents);
        return path;
    }

    /// <summary>Directories matching sim6502-u64sim-* under the temp path, by exact name.</summary>
    private static string[] SnapshotU64SimTempDirs() =>
        Directory.GetDirectories(Path.GetTempPath(), "sim6502-u64sim-*");

    /// <summary>
    /// A throwaway directory to use as --u64sim-fs-root. UltimateFileSystem
    /// requires the root to exist and copies its contents, so it needs at
    /// least one file.
    /// </summary>
    private static string CreateFixtureRoot()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "sim6502-clitest-fsroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "readme.txt"), "fixture");
        return fixture;
    }

    [Fact]
    public void RunCli_MissingSuiteFile_ReturnsTestFailure()
    {
        var opts = NewOptions(Path.Combine(Path.GetTempPath(), "sim6502-clitest-does-not-exist-" + Guid.NewGuid() + ".suite"));

        var exitCode = Sim6502Cli.RunCli(opts);

        // File.Exists is false, so RunCli throws FileNotFoundException itself
        // (before ever calling RunTests) and its own catch turns that into TestFailure.
        exitCode.Should().Be(ExitCode.TestFailure);
    }

    [Fact]
    public void RunCli_ParseError_ReturnsParseErrorExitCode()
    {
        var path = WriteTempSuite("suites { suite(\"broken\" {");
        try
        {
            var exitCode = Sim6502Cli.RunCli(NewOptions(path));
            exitCode.Should().Be(ExitCode.ParseError);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RunCli_FailingSuite_ReturnsTestFailureExitCode()
    {
        // A bare assert() with nothing executed is itself an automatic failure
        // ("This test never executed anything"), so the failing case needs a
        // real execution call (uci()) whose result is then asserted wrong.
        var fsRoot = CreateFixtureRoot();
        var path = WriteTempSuite("""
            suites {
              suite("failing suite") {
                system(c64)
                test("always-fails", "the UCI identify call is asserted wrong") {
                  uci($01, $01)
                  assert(uci_status("99,NOPE"), "IDENTIFY did not say 99,NOPE")
                }
              }
            }
            """);
        try
        {
            var opts = NewOptions(path, backend: "u64sim");
            opts.U64SimFsRoot = fsRoot;

            var exitCode = Sim6502Cli.RunCli(opts);
            exitCode.Should().Be(ExitCode.TestFailure);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(fsRoot, recursive: true);
        }
    }

    [Fact]
    public void RunCli_PassingSuite_ReturnsSuccessExitCode()
    {
        var fsRoot = CreateFixtureRoot();
        var path = WriteTempSuite("""
            suites {
              suite("passing suite") {
                system(c64)
                test("always-passes", "the UCI identify call succeeds") {
                  uci($01, $01)
                  assert(uci_status("00,OK"), "IDENTIFY succeeded")
                }
              }
            }
            """);
        try
        {
            var opts = NewOptions(path, backend: "u64sim");
            opts.U64SimFsRoot = fsRoot;

            var exitCode = Sim6502Cli.RunCli(opts);
            exitCode.Should().Be(ExitCode.Success);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(fsRoot, recursive: true);
        }
    }

    [Fact]
    public void RunCli_U64SimRunThrowsInvalidOperation_StillDisposesBackendAndLeavesNoTempDir()
    {
        // run() requires a high-level backend (novavm); U64SimBackend is not
        // one, so RequireHighLevelBackend("run()") throws InvalidOperationException
        // mid-walk. ExitSuite never runs, so only the CLI's outer finally block
        // (sbl?.DisposeBackendIfOwned()) can clean up the backend it constructed.
        var fsRoot = CreateFixtureRoot();

        var suitePath = WriteTempSuite("""
            suites {
              suite("disposal") {
                system(c64)
                test("run-requires-highlevel-backend", "run() is rejected on u64sim") {
                  run()
                }
              }
            }
            """);

        var before = SnapshotU64SimTempDirs();
        try
        {
            var opts = NewOptions(suitePath, backend: "u64sim");
            opts.U64SimFsRoot = fsRoot;

            var exitCode = Sim6502Cli.RunCli(opts);

            // walker.Walk threw; RunTests' finally caught it via the outer
            // RunCli try/catch, which reports it as a plain test failure.
            exitCode.Should().Be(ExitCode.TestFailure);

            // RunCli has already returned, so if disposal worked, the two
            // directories U64SimBackend created for this run are already gone
            // by this point — "new since before" should be empty immediately.
            // dotnet test runs other classes in parallel, and several of them
            // (BackendFactoryTests, Systems/Ultimate/*) also construct
            // sim6502-u64sim-* directories; one of those can legitimately be
            // mid-flight (created but not yet disposed by its own test) right
            // when we snapshot. Give any such directory a brief window to be
            // cleaned up by its actual owner before treating it as a leak from
            // this test — a real leak from OUR run never disappears, since our
            // disposal already ran synchronously before this line.
            var candidates = SnapshotU64SimTempDirs().Except(before).ToArray();
            var deadline = DateTime.UtcNow.AddSeconds(2);
            var leaked = candidates.Where(Directory.Exists).ToArray();
            while (leaked.Length > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
                leaked = leaked.Where(Directory.Exists).ToArray();
            }

            leaked.Should().BeEmpty(
                "U64SimBackend.Dispose() should have removed both UltimateFileSystem working copies");
        }
        finally
        {
            File.Delete(suitePath);
            Directory.Delete(fsRoot, recursive: true);
        }
    }

    [Fact]
    public void BuildBackendConfigs_U64Sim_CarriesFsRootAndUciLatency()
    {
        var opts = NewOptions("unused.suite", backend: "u64sim");
        opts.U64SimFsRoot = "/some/fixture/root";
        opts.U64SimUciLatency = 128;
        opts.U64SimMount = "USB1";

        var configs = Sim6502Cli.BuildBackendConfigs(opts);

        configs.Vice.Should().BeNull();
        configs.NovaVm.Should().BeNull();
        configs.U64Sim.Should().NotBeNull();
        configs.U64Sim!.FsRoot.Should().Be("/some/fixture/root");
        configs.U64Sim.UciLatencyCycles.Should().Be(128);
        configs.U64Sim.MountName.Should().Be("USB1");
    }

    [Fact]
    public void BuildBackendConfigs_Vice_CarriesHostPortTimeoutAndWarp()
    {
        var opts = NewOptions("unused.suite", backend: "vice");
        opts.ViceHost = "10.0.0.5";
        opts.VicePort = 1234;
        opts.ViceTimeout = 9999;
        opts.ViceWarp = false;

        var configs = Sim6502Cli.BuildBackendConfigs(opts);

        configs.U64Sim.Should().BeNull();
        configs.NovaVm.Should().BeNull();
        configs.Vice.Should().NotBeNull();
        configs.Vice!.Host.Should().Be("10.0.0.5");
        configs.Vice.Port.Should().Be(1234);
        configs.Vice.TimeoutMs.Should().Be(9999);
        configs.Vice.WarpMode.Should().BeFalse();
    }

    [Fact]
    public void BuildBackendConfigs_NovaVm_CarriesHostPortAndTimeout()
    {
        var opts = NewOptions("unused.suite", backend: "novavm");
        opts.NovaVmHost = "10.0.0.6";
        opts.NovaVmPort = 4321;
        opts.NovaVmTimeout = 8888;

        var configs = Sim6502Cli.BuildBackendConfigs(opts);

        configs.U64Sim.Should().BeNull();
        configs.Vice.Should().BeNull();
        configs.NovaVm.Should().NotBeNull();
        configs.NovaVm!.Host.Should().Be("10.0.0.6");
        configs.NovaVm.Port.Should().Be(4321);
        configs.NovaVm.TimeoutMs.Should().Be(8888);
    }

    [Fact]
    public void BuildBackendConfigs_SimBackend_AllConfigsAreNull()
    {
        var opts = NewOptions("unused.suite", backend: "sim");

        var configs = Sim6502Cli.BuildBackendConfigs(opts);

        configs.Vice.Should().BeNull();
        configs.NovaVm.Should().BeNull();
        configs.U64Sim.Should().BeNull();
    }

    [Fact]
    public void BuildBackendConfigs_U64_PopulatesHostAndTimeouts()
    {
        var opts = new Sim6502Cli.Options
        {
            SuiteFile = "unused.suite",
            Backend = "u64",
            U64Host = "192.168.1.62",
            U64Port = 80,
            U64Timeout = 7000
        };

        var configs = Sim6502Cli.BuildBackendConfigs(opts);

        configs.U64.Should().NotBeNull();
        configs.U64!.Host.Should().Be("192.168.1.62");
        configs.U64.Port.Should().Be(80);
        configs.U64.HttpTimeoutMs.Should().Be(7000);
    }

    [Fact]
    public void BuildBackendConfigs_NonU64_LeavesU64ConfigNull()
    {
        var opts = NewOptions("unused.suite", backend: "sim");
        Sim6502Cli.BuildBackendConfigs(opts).U64.Should().BeNull();
    }
}
