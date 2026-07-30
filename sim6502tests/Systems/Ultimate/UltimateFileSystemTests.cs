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

        // The security property is that the result never escapes the working
        // root — not that it avoids echoing the caller's leaf names. Under plain
        // chroot semantics a leading ".." at the root is absorbed as a no-op, so
        // "../../../../etc/passwd" resolves to WorkingRoot/etc/passwd: a path
        // that is safely *inside* the sandbox and simply does not exist there.
        // That is the correct, safe outcome — a DOS open of it returns FILE NOT
        // FOUND — and asserting on leaf names such as "etc/passwd" would be
        // substring-blacklist thinking: rejecting a safe path because of what
        // it is named rather than where it actually resolves.
        //
        // So the only things worth asserting are: either rejected outright, or
        // canonically inside the working root.
        if (host == null) return;

        var canonical = Path.GetFullPath(host);
        var rootWithSeparator = fs.WorkingRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fs.WorkingRoot
            : fs.WorkingRoot + Path.DirectorySeparatorChar;
        (canonical == fs.WorkingRoot || canonical.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            .Should().BeTrue($"'{canonical}' must not escape the working root '{fs.WorkingRoot}'");
    }

    [Fact]
    public void ResolveToHostPath_DotDotAtRoot_ThenRelativePath_ResolvesNormally()
    {
        // Pins the legitimate case a prior (reverted) fix broke: a ".." that
        // underflows at the root must be absorbed as a no-op, with the rest of
        // the path still resolving normally afterwards. That fix set a
        // "climbed above root" flag on underflow and discarded every segment
        // that followed, so "../data/bytes.bin" resolved to WorkingRoot instead
        // of WorkingRoot/data/bytes.bin — a legitimate relative path silently
        // landing in the wrong place.
        using var fs = NewFs();
        var host = fs.ResolveToHostPath("../data/bytes.bin");
        host.Should().NotBeNull();
        host.Should().Be(Path.Combine(fs.WorkingRoot, "data", "bytes.bin"));
        File.ReadAllBytes(host!).Should().Equal(1, 2, 3, 4);
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
