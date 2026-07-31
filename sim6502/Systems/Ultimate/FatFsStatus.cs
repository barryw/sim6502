// Ported from GideonZ/1541ultimate (GPL-3.0):
//   software/filesystem/file_system.cc  FileSystem::get_error_string
// Original author: Gideon Zweijtzer. See NOTICE.

namespace sim6502.Systems.Ultimate;

/// <summary>
/// FatFs result strings, as the Ultimate's DOS surfaces them.
///
/// These are NOT the "NN,MESSAGE" DOS status strings in
/// <see cref="UltimateDosTarget"/>. Upstream dos.cc returns FatFs text on some
/// paths and a DOS status on others, and which one applies is per-command.
/// DOS_CMD_OPEN_FILE (dos.cc:111-124) uses these; confirmed on hardware
/// running fw 3.14d, which answers "FILE DOESN'T EXIST" for a missing file.
///
/// This is deliberately the subset of the upstream table that .NET's own
/// exceptions distinguish in <see cref="UltimateDosTarget"/>'s OpenFile
/// (<see cref="FileDoesntExist"/>, <see cref="PathDoesntExist"/>,
/// <see cref="AccessDenied"/>, <see cref="FileExists"/>), not a full port --
/// every other FatFs result .NET doesn't separately distinguish falls through
/// to a generic internal-error status instead. The remaining four constants
/// below have no caller.
/// </summary>
public static class FatFsStatus
{
    public const string FileDoesntExist   = "FILE DOESN'T EXIST";
    public const string PathDoesntExist   = "PATH DOESN'T EXIST";
    public const string InvalidName       = "INVALID NAME";
    public const string AccessDenied      = "ACCESS DENIED";
    public const string FileExists        = "FILE EXISTS";
    public const string WriteProtected    = "WRITE PROTECTED";
    public const string DirectoryNotEmpty = "DIRECTORY NOT EMPTY";
    public const string DiskFull          = "DISK IS FULL";
}
