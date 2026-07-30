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
    public const string StatusNotImplemented   = "99,FUNCTION NOT IMPLEMENTED";
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
            return UciReply.Empty(StatusFileNotFound);
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
            return UciReply.Empty(StatusFileNotFound);
        }
        catch (DirectoryNotFoundException)
        {
            return UciReply.Empty(StatusNoSuchDirectory);
        }
        catch (Exception ex)
        {
            // Upstream surfaces FatFs error text here. Porting that table buys no
            // test value, so failures map onto the documented DOS statuses.
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
