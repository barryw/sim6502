using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class UltimateFileSystemTests : IDisposable
{
    private readonly string _fixture;

    public UltimateFileSystemTests()
    {
        _fixture = Path.Combine(Path.GetTempPath(), "u64sim-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_fixture, "data"));
        Directory.CreateDirectory(Path.Combine(_fixture, "data", "nested"));
        File.WriteAllText(Path.Combine(_fixture, "hello.txt"), "hello");
        File.WriteAllBytes(Path.Combine(_fixture, "data", "bytes.bin"), new byte[] { 1, 2, 3, 4 });
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    private UltimateFileSystem NewFs() => new(_fixture);

    [Fact]
    public void CurrentPath_StartsAtMountRoot()
    {
        using var fs = NewFs();
        fs.MountRoot.Should().Be("/Usb0");
        fs.CurrentPath.Should().Be("/Usb0");
    }

    [Fact]
    public void Constructor_CopiesFixtureSoOriginalIsNeverMutated()
    {
        using var fs = NewFs();
        var host = fs.ResolveToHostPath("hello.txt");
        host.Should().NotBeNull();
        host.Should().NotStartWith(_fixture, "the working tree must be a copy, not the fixture");

        File.WriteAllText(host!, "overwritten");
        File.ReadAllText(Path.Combine(_fixture, "hello.txt")).Should().Be("hello");
    }

    [Fact]
    public void Dispose_RemovesTheWorkingCopy()
    {
        string host;
        using (var fs = NewFs())
        {
            host = fs.ResolveToHostPath("hello.txt")!;
            File.Exists(host).Should().BeTrue();
        }
        File.Exists(host).Should().BeFalse();
    }

    [Fact]
    public void ChangeDirectory_Relative_Succeeds()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0/data");
        fs.ChangeDirectory("nested").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0/data/nested");
    }

    [Fact]
    public void ChangeDirectory_Absolute_Succeeds()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("/Usb0/data/nested").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0/data/nested");
    }

    [Fact]
    public void ChangeDirectory_DotIsNoOp()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data").Should().BeTrue();
        fs.ChangeDirectory(".").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0/data");
    }

    [Fact]
    public void ChangeDirectory_DotDot_GoesUp()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data/nested").Should().BeTrue();
        fs.ChangeDirectory("..").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0/data");
    }

    [Fact]
    public void ChangeDirectory_DotDotAtRoot_IsNoOpNotAnEscape()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("..").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0");
        fs.ChangeDirectory("../../..").Should().BeTrue();
        fs.CurrentPath.Should().Be("/Usb0");
    }

    [Fact]
    public void ChangeDirectory_NonexistentPath_FailsAndLeavesPathUnchanged()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data").Should().BeTrue();
        fs.ChangeDirectory("nope").Should().BeFalse();
        fs.CurrentPath.Should().Be("/Usb0/data");
    }

    [Fact]
    public void ChangeDirectory_IntoAFile_Fails()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("hello.txt").Should().BeFalse();
        fs.CurrentPath.Should().Be("/Usb0");
    }

    [Fact]
    public void ChangeDirectory_WrongMountName_Fails()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("/SdCard/data").Should().BeFalse();
        fs.CurrentPath.Should().Be("/Usb0");
    }

    [Theory]
    [InlineData("../../../../etc/passwd")]
    [InlineData("/Usb0/../../etc/passwd")]
    [InlineData("data/../../../etc/passwd")]
    public void ResolveToHostPath_TraversalAttempts_StayInsideTheRoot(string attempt)
    {
        using var fs = NewFs();
        var host = fs.ResolveToHostPath(attempt);

        // Either rejected outright, or clamped to somewhere inside the working root.
        if (host != null)
            host.Should().StartWith(fs.WorkingRoot);
        host.Should().NotContain("etc" + Path.DirectorySeparatorChar + "passwd");
    }

    [Fact]
    public void ResolveToHostPath_EmbeddedNul_IsRejected()
    {
        using var fs = NewFs();
        fs.ResolveToHostPath("hel\0lo.txt").Should().BeNull();
    }

    [Fact]
    public void ResolveToHostPath_RelativeToCurrentDirectory()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data").Should().BeTrue();
        var host = fs.ResolveToHostPath("bytes.bin");
        host.Should().NotBeNull();
        File.ReadAllBytes(host!).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void ResolveToHostPath_AbsoluteIgnoresCurrentDirectory()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data/nested").Should().BeTrue();
        var host = fs.ResolveToHostPath("/Usb0/hello.txt");
        host.Should().NotBeNull();
        File.ReadAllText(host!).Should().Be("hello");
    }

    [Fact]
    public void ListCurrentDirectory_ReportsDirectoriesAndFilesWithFatAttributes()
    {
        using var fs = NewFs();
        var entries = fs.ListCurrentDirectory();

        entries.Should().HaveCount(2);
        var dir = entries.Single(e => e.Name == "data");
        dir.Attributes.Should().Be(UltimateFileSystem.AttributeDirectory);

        var file = entries.Single(e => e.Name == "hello.txt");
        file.Attributes.Should().Be(UltimateFileSystem.AttributeArchive);
        file.Size.Should().Be(5);
    }

    [Fact]
    public void ListCurrentDirectory_DirectoriesBeforeFilesEachAlphabetical()
    {
        using var fs = NewFs();
        File.WriteAllText(Path.Combine(fs.WorkingRoot, "aaa.txt"), "a");
        Directory.CreateDirectory(Path.Combine(fs.WorkingRoot, "zzz"));

        var names = fs.ListCurrentDirectory().Select(e => e.Name).ToArray();
        names.Should().Equal("data", "zzz", "aaa.txt", "hello.txt");
    }

    [Fact]
    public void ListCurrentDirectory_EmptyDirectory_IsEmpty()
    {
        using var fs = NewFs();
        fs.ChangeDirectory("data/nested").Should().BeTrue();
        fs.ListCurrentDirectory().Should().BeEmpty();
    }

    [Fact]
    public void Constructor_MissingHostRoot_Throws()
    {
        var act = () => new UltimateFileSystem(Path.Combine(_fixture, "does-not-exist"));
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Symlinks_AreNotCopiedIntoTheWorkingTree()
    {
        var outside = Path.Combine(Path.GetTempPath(), "u64sim-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
        try
        {
            try
            {
                File.CreateSymbolicLink(Path.Combine(_fixture, "escape.txt"),
                                        Path.Combine(outside, "secret.txt"));
            }
            catch (Exception)
            {
                return; // platform forbids symlink creation; nothing to assert
            }

            using var fs = NewFs();
            File.Exists(Path.Combine(fs.WorkingRoot, "escape.txt")).Should().BeFalse();
            fs.ListCurrentDirectory().Select(e => e.Name).Should().NotContain("escape.txt");
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }
}
