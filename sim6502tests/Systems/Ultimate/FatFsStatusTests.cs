using FluentAssertions;
using sim6502.Systems.Ultimate;
using Xunit;

namespace sim6502tests.Systems.Ultimate;

public class FatFsStatusTests
{
    // Values are byte-exact from upstream software/filesystem/file_system.cc
    // get_error_string(). Confirmed on real hardware (fw 3.14d): OPEN_FILE on a
    // missing file answers "FILE DOESN'T EXIST", not "82,FILE NOT FOUND".
    [Theory]
    [InlineData("FILE DOESN'T EXIST")]
    [InlineData("PATH DOESN'T EXIST")]
    [InlineData("INVALID NAME")]
    [InlineData("ACCESS DENIED")]
    [InlineData("FILE EXISTS")]
    [InlineData("WRITE PROTECTED")]
    [InlineData("DIRECTORY NOT EMPTY")]
    [InlineData("DISK IS FULL")]
    public void Table_ContainsUpstreamString(string expected)
    {
        var all = new[]
        {
            FatFsStatus.FileDoesntExist, FatFsStatus.PathDoesntExist,
            FatFsStatus.InvalidName, FatFsStatus.AccessDenied,
            FatFsStatus.FileExists, FatFsStatus.WriteProtected,
            FatFsStatus.DirectoryNotEmpty, FatFsStatus.DiskFull
        };
        all.Should().Contain(expected);
    }

    [Fact]
    public void FileDoesntExist_HasNoNumericPrefix()
    {
        // The DOS status strings carry a "NN," prefix; the FatFs strings do not.
        // Getting this wrong is exactly the bug this task fixes.
        FatFsStatus.FileDoesntExist.Should().Be("FILE DOESN'T EXIST");
        FatFsStatus.FileDoesntExist.Should().NotContain(",");
    }
}
