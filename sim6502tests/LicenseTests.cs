using FluentAssertions;
using Xunit;

namespace sim6502tests;

public class LicenseTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "sim6502.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must be able to locate the repository root");
        return dir!.FullName;
    }

    [Fact]
    public void Repository_HasGpl3LicenseFile()
    {
        var path = Path.Combine(RepoRoot(), "LICENSE");
        File.Exists(path).Should().BeTrue("LICENSE must exist at the repository root");
        var text = File.ReadAllText(path);
        text.Should().Contain("GNU GENERAL PUBLIC LICENSE");
        text.Should().Contain("Version 3, 29 June 2007");
    }

    [Fact]
    public void Notice_CreditsUpstreamAuthors()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "NOTICE"));
        text.Should().Contain("Gideon Zweijtzer");
        text.Should().Contain("Aaron Mell");
        text.Should().Contain("1541ultimate");
    }

    [Fact]
    public void Readme_DocumentsTheU64SimBackend()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));

        text.Should().Contain("u64sim");
        text.Should().Contain("--u64sim-fs-root");
        text.Should().Contain("--u64sim-uci-latency");
        text.Should().Contain("uci_status");
        text.Should().Contain("system(c64)");
    }

    [Fact]
    public void Changelog_RecordsTheLicenceChangeAndReservedWord()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "CHANGELOG.md"));

        text.Should().Contain("4.0.0");
        text.Should().Contain("GPL-3.0");
        text.Should().Contain("reserved word");
    }

    [Fact]
    public void ProjectVersion_IsFourPointZero()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "sim6502", "sim6502.csproj"));
        text.Should().Contain("<Version>4.0.0</Version>");
    }
}
