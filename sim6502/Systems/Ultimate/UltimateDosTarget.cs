// Ported from GideonZ/1541ultimate (GPL-3.0):
//   software/filemanager/dos.cc
//   software/filemanager/dos.h
// Original author: Gideon Zweijtzer. See NOTICE.

using System.Text;
using NLog;

namespace sim6502.Systems.Ultimate;

/// <summary>
/// The Ultimate DOS command target, served at UCI targets $01 and $02. Each
/// instance keeps its own current directory, open file, and data-mode state, so
/// two of them can be in use at once without interfering.
/// </summary>
public sealed class UltimateDosTarget : ICommandTarget, IDisposable
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    // ── Command codes (dos.h lines 11-38) ──
    public const byte CmdIdentify      = 0x01;
    public const byte CmdOpenFile      = 0x02;
    public const byte CmdCloseFile     = 0x03;
    public const byte CmdReadData      = 0x04;
    public const byte CmdWriteData     = 0x05;
    public const byte CmdFileSeek      = 0x06;
    public const byte CmdFileInfo      = 0x07;
    public const byte CmdFileStat      = 0x08;
    public const byte CmdDeleteFile    = 0x09;
    public const byte CmdRenameFile    = 0x0A;
    public const byte CmdCopyFile      = 0x0B;
    public const byte CmdChangeDir     = 0x11;
    public const byte CmdGetPath       = 0x12;
    public const byte CmdOpenDir       = 0x13;
    public const byte CmdReadDir       = 0x14;
    public const byte CmdCopyUiPath    = 0x15;
    public const byte CmdCreateDir     = 0x16;
    public const byte CmdCopyHomePath  = 0x17;
    public const byte CmdLoadReu       = 0x21;
    public const byte CmdSaveReu       = 0x22;
    public const byte CmdMountDisk     = 0x23;
    public const byte CmdUnmountDisk   = 0x24;
    public const byte CmdSwapDisk      = 0x25;
    public const byte CmdGetTime       = 0x26;
    public const byte CmdSetTime       = 0x27;
    public const byte CmdEcho          = 0xF0;

    // ── File attribute flags for OPEN_FILE ──
    public const byte FileAttributeRead         = 0x01;
    public const byte FileAttributeWrite        = 0x02;
    public const byte FileAttributeCreateNew    = 0x04;
    public const byte FileAttributeCreateAlways = 0x08;

    // ── Status strings, byte-exact from dos.cc lines 15-30 ──
    public const string StatusDirectoryEmpty   = "01,DIRECTORY EMPTY";
    public const string StatusTruncated        = "02,REQUEST TRUNCATED";
    public const string StatusNotInDataMode    = "81,NOT IN DATA MODE";
    public const string StatusFileNotFound     = "82,FILE NOT FOUND";
    public const string StatusNoSuchDirectory  = "83,NO SUCH DIRECTORY";
    public const string StatusNoFileToClose    = "84,NO FILE TO CLOSE";
    public const string StatusNoFileOpen       = "85,NO FILE OPEN";
    public const string StatusCannotReadDir    = "86,CAN'T READ DIRECTORY";
    public const string StatusInternalError    = "87,INTERNAL ERROR";
    public const string StatusNoInformation    = "88,NO INFORMATION AVAILABLE";
    public const string StatusNotADiskImage    = "89,NOT A DISK IMAGE";
    public const string StatusDriveNotPresent  = "90,DRIVE NOT PRESENT";
    public const string StatusIncompatible     = "91,INCOMPATIBLE IMAGE";
    public const string StatusProhibited       = "98,FUNCTION PROHIBITED";

    /// <summary>Read chunk size, matching dos.cc get_more_data.</summary>
    public const int ReadChunkSize = 512;

    private enum DosState
    {
        Idle,
        InFile,
        InDirectory
    }

    private readonly UltimateFileSystem _fileSystem;
    private readonly string _version;

    private DosState _state = DosState.Idle;
    private FileStream? _file;
    private int _remaining;
    private IReadOnlyList<UltimateDirEntry> _directory = Array.Empty<UltimateDirEntry>();
    private int _directoryIndex;
    private bool _disposed;

    public UltimateDosTarget(UltimateFileSystem fileSystem, string version = "ULTIMATE-II DOS V1.2")
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _version = version;
    }

    public UciReply ParseCommand(byte[] command)
    {
        if (command.Length < 2)
        {
            Logger.Warn("DOS: command shorter than two bytes");
            return UciReply.Empty(UciConstants.StatusUnknownCommand);
        }

        return command[1] switch
        {
            CmdIdentify  => new UciReply(Encoding.ASCII.GetBytes(_version), UciConstants.StatusOk, true),
            CmdChangeDir => ChangeDirectory(ReadString(command, 2)),
            CmdGetPath   => UciReply.Ok(Encoding.ASCII.GetBytes(_fileSystem.CurrentPath)),
            CmdCreateDir => CreateDirectory(ReadString(command, 2)),
            CmdEcho      => new UciReply(command, UciConstants.StatusOk, true),
            CmdOpenFile  => OpenFile(command),
            CmdCloseFile => CloseFile(),
            CmdReadData  => BeginRead(command),
            CmdWriteData => WriteData(command),
            CmdFileSeek  => Seek(command),

            CmdFileInfo   => OpenFileInfo(),
            CmdFileStat   => FileStat(ReadString(command, 2)),
            CmdDeleteFile => Delete(ReadString(command, 2)),
            CmdRenameFile => RenameOrCopy(command, copy: false),
            CmdCopyFile   => RenameOrCopy(command, copy: true),
            CmdOpenDir    => OpenDirectory(),
            CmdReadDir    => BeginReadDirectory(),

            // Recognised commands deferred to a later milestone. Answering
            // "not implemented" rather than "unknown command" keeps the gap
            // visible instead of looking like a malformed request.
            CmdCopyUiPath or CmdCopyHomePath or CmdLoadReu or CmdSaveReu or
            CmdMountDisk or CmdUnmountDisk or CmdSwapDisk or CmdGetTime or CmdSetTime
                => UciReply.Empty(UciConstants.StatusNotImplemented),

            _ => UciReply.Empty(UciConstants.StatusUnknownCommand)
        };
    }

    public UciReply GetMoreData()
    {
        switch (_state)
        {
            case DosState.Idle:
                Logger.Debug("DOS: more data requested while idle");
                return UciReply.Empty(StatusNotInDataMode);

            case DosState.InFile:
                return ReadNextChunk();

            case DosState.InDirectory:
                return NextDirectoryEntry();

            default:
                Logger.Warn($"DOS: unhandled data-mode state {_state}");
                _state = DosState.Idle;
                return UciReply.Empty(StatusInternalError);
        }
    }

    public void Abort(int bytesConsumed)
    {
        Logger.Debug($"DOS: aborted after {bytesConsumed} response bytes");
        _state = DosState.Idle;
    }

    /// <summary>
    /// Drop all transient state: closes any open file and leaves data mode. Used by
    /// the control target's REBOOT.
    /// </summary>
    public void ResetState()
    {
        _file?.Dispose();
        _file = null;
        _state = DosState.Idle;
        _remaining = 0;
        _directory = Array.Empty<UltimateDirEntry>();
        _directoryIndex = 0;
    }

    private UciReply ChangeDirectory(string path)
    {
        // UltimateFileSystem.ChangeDirectory leaves the current path untouched on
        // failure, so there is nothing to roll back here.
        return _fileSystem.ChangeDirectory(path)
            ? UciReply.Empty(UciConstants.StatusOk)
            : UciReply.Empty(StatusNoSuchDirectory);
    }

    private UciReply CreateDirectory(string name)
    {
        var host = _fileSystem.ResolveToHostPath(name);
        if (host == null)
            return UciReply.Empty(StatusNoSuchDirectory);

        if (Directory.Exists(host) || File.Exists(host))
            return UciReply.Empty(StatusInternalError);

        try
        {
            Directory.CreateDirectory(host);
            return UciReply.Empty(UciConstants.StatusOk);
        }
        catch (Exception ex)
        {
            Logger.Warn($"DOS: could not create directory '{name}': {ex.Message}");
            return UciReply.Empty(StatusInternalError);
        }
    }

    private UciReply OpenFile(byte[] command)
    {
        if (command.Length < 3)
            return UciReply.Empty(StatusInternalError);

        var attributes = command[2];
        var name = ReadString(command, 3);

        var host = _fileSystem.ResolveToHostPath(name);
        if (host == null)
        {
            Logger.Warn($"DOS: open rejected for out-of-mount path '{name}'");
            return UciReply.Empty(FatFsStatus.FileDoesntExist);
        }

        // FatFs flag semantics: CREATE_ALWAYS truncates, CREATE_NEW must not exist,
        // otherwise the file must already be there.
        var mode = (attributes & FileAttributeCreateAlways) != 0 ? FileMode.Create
                 : (attributes & FileAttributeCreateNew) != 0    ? FileMode.CreateNew
                 : FileMode.Open;

        var wantsWrite = (attributes & FileAttributeWrite) != 0;
        var wantsRead  = (attributes & FileAttributeRead) != 0 || !wantsWrite;

        var access = wantsRead && wantsWrite ? FileAccess.ReadWrite
                   : wantsWrite              ? FileAccess.Write
                   : FileAccess.Read;

        // .NET forbids creating a file opened read-only; widen so the flag
        // combination the C64 asked for still works.
        if (mode != FileMode.Open && access == FileAccess.Read)
            access = FileAccess.ReadWrite;

        _file?.Dispose();
        _file = null;
        _state = DosState.Idle;

        try
        {
            _file = new FileStream(host, mode, access);
            return UciReply.Empty(UciConstants.StatusOk);
        }
        catch (FileNotFoundException)
        {
            return UciReply.Empty(FatFsStatus.FileDoesntExist);
        }
        catch (DirectoryNotFoundException)
        {
            return UciReply.Empty(FatFsStatus.PathDoesntExist);
        }
        catch (UnauthorizedAccessException)
        {
            return UciReply.Empty(FatFsStatus.AccessDenied);
        }
        catch (IOException) when (mode == FileMode.CreateNew)
        {
            return UciReply.Empty(FatFsStatus.FileExists);
        }
        catch (Exception ex)
        {
            // Upstream maps every FatFs result through get_error_string; the
            // cases above cover the ones .NET distinguishes. Anything else is a
            // genuine internal failure.
            Logger.Warn($"DOS: could not open '{name}': {ex.Message}");
            return UciReply.Empty(StatusInternalError);
        }
    }

    private UciReply CloseFile()
    {
        if (_file == null)
            return UciReply.Empty(StatusNoFileToClose);

        _file.Dispose();
        _file = null;
        _state = DosState.Idle;
        return UciReply.Empty(UciConstants.StatusOk);
    }

    private UciReply BeginRead(byte[] command)
    {
        if (_file == null)
            return UciReply.Empty(StatusNoFileOpen);

        if (command.Length < 4)
            return UciReply.Empty(StatusInternalError);

        _remaining = (command[3] << 8) | command[2];
        _state = DosState.InFile;
        return GetMoreData();
    }

    private UciReply WriteData(byte[] command)
    {
        if (_file == null)
            return UciReply.Empty(StatusNoFileOpen);

        // Bytes 2 and 3 are dummies; the payload starts at byte 4.
        var offset = 4;
        var count = Math.Max(0, command.Length - offset);

        try
        {
            if (count > 0)
                _file.Write(command, offset, count);
            _file.Flush();
            return UciReply.Empty(UciConstants.StatusOk);
        }
        catch (Exception ex)
        {
            Logger.Warn($"DOS: write of {count} bytes failed: {ex.Message}");
            return UciReply.Empty(StatusInternalError);
        }
    }

    private UciReply Seek(byte[] command)
    {
        if (_file == null)
            return UciReply.Empty(StatusNoFileOpen);

        if (command.Length < 6)
            return UciReply.Empty(StatusInternalError);

        var position = (long)command[2]
                     | ((long)command[3] << 8)
                     | ((long)command[4] << 16)
                     | ((long)command[5] << 24);

        try
        {
            // FatFs clamps a seek past the end on a read-only file rather than
            // failing, so clamp here too.
            _file.Position = Math.Min(position, _file.Length);
            return UciReply.Empty(UciConstants.StatusOk);
        }
        catch (Exception ex)
        {
            Logger.Warn($"DOS: seek to {position} failed: {ex.Message}");
            return UciReply.Empty(StatusInternalError);
        }
    }

    private UciReply ReadNextChunk()
    {
        var length = Math.Min(_remaining, ReadChunkSize);
        var buffer = new byte[length];
        int transferred;

        try
        {
            transferred = length == 0 ? 0 : _file!.Read(buffer, 0, length);
        }
        catch (Exception ex)
        {
            // dos.cc leaves *status unassigned on this path — an upstream defect.
            // Assign the error status explicitly instead.
            Logger.Warn($"DOS: read failed: {ex.Message}");
            _state = DosState.Idle;
            return UciReply.Empty(StatusInternalError);
        }

        _remaining -= transferred;

        var lastPart = transferred != length || _remaining == 0;
        if (lastPart)
            _state = DosState.Idle;

        var data = transferred == length ? buffer : buffer[..transferred];
        return new UciReply(data, UciConstants.StatusEmpty, lastPart);
    }

    /// <summary>FAT packed date: year since 1980 in bits 15-9, month 8-5, day 4-0.</summary>
    internal static ushort FatDate(DateTime when)
    {
        if (when.Year < 1980) return 0;
        return (ushort)(((when.Year - 1980) << 9) | (when.Month << 5) | when.Day);
    }

    /// <summary>FAT packed time: hour in bits 15-11, minute 10-5, two-second units 4-0.</summary>
    internal static ushort FatTime(DateTime when)
        => (ushort)((when.Hour << 11) | (when.Minute << 5) | (when.Second / 2));

    /// <summary>
    /// Build the t_dos_info reply: size, FAT date and time, space-padded three
    /// character extension, attribute, then the name with no terminator.
    /// </summary>
    private static byte[] BuildInfo(string name, long size, byte attributes, DateTime modified)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        var data = new byte[12 + nameBytes.Length];

        BitConverter.TryWriteBytes(data.AsSpan(0, 4), (uint)Math.Min(size, uint.MaxValue));
        BitConverter.TryWriteBytes(data.AsSpan(4, 2), FatDate(modified));
        BitConverter.TryWriteBytes(data.AsSpan(6, 2), FatTime(modified));

        data[8] = data[9] = data[10] = (byte)' ';
        var extension = Path.GetExtension(name);
        if (extension.StartsWith('.')) extension = extension[1..];
        extension = extension.ToUpperInvariant();
        for (var i = 0; i < Math.Min(3, extension.Length); i++)
            data[8 + i] = (byte)extension[i];

        data[11] = attributes;
        Array.Copy(nameBytes, 0, data, 12, nameBytes.Length);
        return data;
    }

    private UciReply FileStat(string name)
    {
        var host = _fileSystem.ResolveToHostPath(name);
        if (host == null)
            return UciReply.Empty(StatusFileNotFound);

        var leaf = Path.GetFileName(host);

        if (Directory.Exists(host))
        {
            var info = new DirectoryInfo(host);
            return UciReply.Ok(BuildInfo(
                leaf, 0, UltimateFileSystem.AttributeDirectory, info.LastWriteTime));
        }

        if (File.Exists(host))
        {
            var info = new FileInfo(host);
            return UciReply.Ok(BuildInfo(
                leaf, info.Length, UltimateFileSystem.AttributeArchive, info.LastWriteTime));
        }

        return UciReply.Empty(StatusFileNotFound);
    }

    // Named OpenFileInfo, not FileInfo: a method called FileInfo would shadow the
    // System.IO.FileInfo type inside this class and break every use of it below.
    private UciReply OpenFileInfo()
    {
        if (_file == null)
            return UciReply.Empty(StatusNoFileOpen);

        try
        {
            var info = new FileInfo(_file.Name);
            return UciReply.Ok(BuildInfo(
                info.Name, info.Length, UltimateFileSystem.AttributeArchive, info.LastWriteTime));
        }
        catch (Exception ex)
        {
            Logger.Warn($"DOS: could not stat the open file: {ex.Message}");
            return UciReply.Empty(StatusFileNotFound);
        }
    }

    private UciReply Delete(string name)
    {
        var host = _fileSystem.ResolveToHostPath(name);
        if (host == null)
            return UciReply.Empty(StatusFileNotFound);

        try
        {
            if (File.Exists(host))
            {
                File.Delete(host);
                return UciReply.Empty(UciConstants.StatusOk);
            }

            if (Directory.Exists(host))
            {
                // Non-recursive, matching f_unlink: a populated directory fails.
                Directory.Delete(host, recursive: false);
                return UciReply.Empty(UciConstants.StatusOk);
            }

            return UciReply.Empty(StatusFileNotFound);
        }
        catch (Exception ex)
        {
            Logger.Warn($"DOS: could not delete '{name}': {ex.Message}");
            return UciReply.Empty(StatusInternalError);
        }
    }

    /// <summary>
    /// RENAME_FILE and COPY_FILE share a wire format: the source name at byte 2,
    /// a NUL, then the destination name.
    /// </summary>
    private UciReply RenameOrCopy(byte[] command, bool copy)
    {
        var source = ReadString(command, 2);
        var separator = 2 + source.Length;

        if (separator >= command.Length)
        {
            Logger.Warn("DOS: rename/copy command carries no destination name");
            return UciReply.Empty(StatusInternalError);
        }

        var destination = ReadString(command, separator + 1);
        if (destination.Length == 0)
            return UciReply.Empty(StatusInternalError);

        var sourceHost = _fileSystem.ResolveToHostPath(source);
        if (sourceHost == null || (!File.Exists(sourceHost) && !Directory.Exists(sourceHost)))
            return UciReply.Empty(StatusFileNotFound);

        var destinationHost = _fileSystem.ResolveToHostPath(destination);
        if (destinationHost == null)
        {
            Logger.Warn($"DOS: rename/copy destination '{destination}' is outside the mount");
            return UciReply.Empty(StatusInternalError);
        }

        if (File.Exists(destinationHost) || Directory.Exists(destinationHost))
            return UciReply.Empty(StatusInternalError);

        try
        {
            if (copy) File.Copy(sourceHost, destinationHost);
            else if (Directory.Exists(sourceHost)) Directory.Move(sourceHost, destinationHost);
            else File.Move(sourceHost, destinationHost);

            return UciReply.Empty(UciConstants.StatusOk);
        }
        catch (Exception ex)
        {
            Logger.Warn($"DOS: {(copy ? "copy" : "rename")} of '{source}' failed: {ex.Message}");
            return UciReply.Empty(StatusInternalError);
        }
    }

    private UciReply OpenDirectory()
    {
        _directory = _fileSystem.ListCurrentDirectory();
        _directoryIndex = 0;

        return UciReply.Empty(_directory.Count == 0
            ? StatusDirectoryEmpty
            : UciConstants.StatusOk);
    }

    private UciReply BeginReadDirectory()
    {
        if (_directory.Count == 0)
        {
            Logger.Debug("DOS: READ_DIR without a preceding OPEN_DIR");
            return UciReply.Empty(StatusCannotReadDir);
        }

        _directoryIndex = 0;
        _state = DosState.InDirectory;
        return GetMoreData();
    }

    private UciReply NextDirectoryEntry()
    {
        if (_directoryIndex >= _directory.Count)
        {
            _state = DosState.Idle;
            return UciReply.Empty(StatusInternalError);
        }

        var entry = _directory[_directoryIndex++];
        var nameBytes = Encoding.ASCII.GetBytes(entry.Name);

        var data = new byte[1 + nameBytes.Length];
        data[0] = entry.Attributes;
        Array.Copy(nameBytes, 0, data, 1, nameBytes.Length);

        var lastPart = _directoryIndex >= _directory.Count;
        if (lastPart)
            _state = DosState.Idle;

        return new UciReply(data, lastPart ? UciConstants.StatusOk : UciConstants.StatusEmpty, lastPart);
    }

    /// <summary>
    /// Read an ASCII string from the command, ending at an embedded NUL or the end
    /// of the command. Upstream writes a NUL at command[length] and reads a C
    /// string, which is the same thing.
    /// </summary>
    internal static string ReadString(byte[] command, int offset)
    {
        if (offset >= command.Length) return string.Empty;

        var end = Array.IndexOf(command, (byte)0x00, offset);
        if (end < 0 || end > command.Length) end = command.Length;

        return Encoding.ASCII.GetString(command, offset, end - offset);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _file?.Dispose();
        _file = null;
        _fileSystem.Dispose();
    }
}
