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
}
