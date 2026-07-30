using System;
using System.IO;
using FluentAssertions;
using sim6502.Proc;
using sim6502.Utilities;
using Xunit;

namespace sim6502tests.Utilities;

/// <summary>
/// Covers the file-I/O and binary-literal paths of <see cref="Utility"/> that the
/// pre-existing sim6502tests.UtilityTests do not exercise. Every temp file created
/// here is removed in a finally block so the suite stays hermetic.
/// </summary>
public class UtilityFileTests
{
    private static string CreateTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sim6502-utility-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Theory]
    [InlineData("%1010", 10)]
    [InlineData("%11111111", 255)]
    [InlineData("%0", 0)]
    public void ParseNumber_BinaryLiteral_ParsesCorrectly(string input, int expected)
    {
        input.ParseNumber().Should().Be(expected);
    }

    [Fact]
    public void FileExists_ExistingFile_DoesNotThrow()
    {
        var path = CreateTempFile(new byte[] { 0x01 });
        try
        {
            var act = () => Utility.FileExists(path);
            act.Should().NotThrow();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FileExists_MissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sim6502-missing-{Guid.NewGuid():N}.bin");

        var act = () => Utility.FileExists(path);

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void LoadFileIntoProcessor_LoadsBytesAtAddress()
    {
        var path = CreateTempFile(new byte[] { 0xA9, 0x42, 0x60 }); // LDA #$42, RTS
        try
        {
            var proc = new Processor();
            Utility.LoadFileIntoProcessor(proc, 0x0600, path);

            proc.ReadMemoryValueWithoutCycle(0x0600).Should().Be(0xA9);
            proc.ReadMemoryValueWithoutCycle(0x0601).Should().Be(0x42);
            proc.ReadMemoryValueWithoutCycle(0x0602).Should().Be(0x60);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFileIntoProcessor_StripHeader_RemovesFirstTwoBytes()
    {
        // First two bytes are a PRG load address header that should be stripped.
        var path = CreateTempFile(new byte[] { 0x00, 0x08, 0xA9, 0x42 });
        try
        {
            var proc = new Processor();
            Utility.LoadFileIntoProcessor(proc, 0x0800, path, stripHeader: true);

            proc.ReadMemoryValueWithoutCycle(0x0800).Should().Be(0xA9);
            proc.ReadMemoryValueWithoutCycle(0x0801).Should().Be(0x42);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFileIntoProcessor_StripHeaderWithFewerThanTwoBytes_Throws()
    {
        var path = CreateTempFile(new byte[] { 0x01 });
        try
        {
            var proc = new Processor();
            var act = () => Utility.LoadFileIntoProcessor(proc, 0x0800, path, stripHeader: true);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*has only 1 bytes*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFileIntoProcessor_MissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sim6502-missing-{Guid.NewGuid():N}.bin");
        var proc = new Processor();

        var act = () => Utility.LoadFileIntoProcessor(proc, 0x0600, path);

        act.Should().Throw<FileNotFoundException>();
    }
}
