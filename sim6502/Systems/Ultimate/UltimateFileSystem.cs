// Models the Ultimate's filesystem namespace as seen through the UCI DOS targets.
// Behaviour corresponds to the Path/FileManager abstractions used by
// GideonZ/1541ultimate software/filemanager/dos.cc (GPL-3.0), reimplemented over
// a host directory. Original author of the upstream behaviour: Gideon Zweijtzer.
// See NOTICE.

using NLog;

namespace sim6502.Systems.Ultimate;

/// <summary>One entry from an Ultimate directory listing.</summary>
/// <param name="Name">Entry name with no path component.</param>
/// <param name="Attributes">FAT attribute byte.</param>
/// <param name="Size">Size in bytes; zero for directories.</param>
/// <param name="Modified">Last write time, used for the FAT date and time fields.</param>
public readonly record struct UltimateDirEntry(
    string Name,
    byte Attributes,
    long Size,
    DateTime Modified);

/// <summary>
/// Exposes a host directory as the Ultimate's mounted filesystem, rooted at
/// <c>/Usb0</c> by default -- pass a different <c>mountName</c> to the
/// constructor to match real hardware, which enumerates its stick as
/// <c>/USB1</c>.
///
/// The host tree is copied to a temporary directory at construction and the copy
/// is deleted on dispose, so tests operate on throwaway state and fixture files
/// are never mutated. Every path is canonicalised and prefix checked against the
/// working root before it is handed back, so <c>..</c> cannot climb out.
///
/// Symlinks present in the host fixture tree are not copied into the working
/// copy, so the copy starts link-free. The containment check itself is lexical:
/// <see cref="Path.GetFullPath(string)"/> canonicalises <c>.</c> and <c>..</c>
/// but does not resolve symlinks, so a link created inside the working copy
/// after construction would be followed straight out of it. The working copy is
/// therefore assumed to remain link-free — nothing in the current command set
/// can create one; a future task that adds link creation must add a physical
/// resolve check before returning a path.
/// </summary>
public sealed class UltimateFileSystem : IDisposable
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>FAT AM_DIR.</summary>
    public const byte AttributeDirectory = 0x10;
    /// <summary>FAT AM_ARC — what the Ultimate reports for ordinary files.</summary>
    public const byte AttributeArchive = 0x20;

    private readonly string _mountName;
    private readonly List<string> _current = new();
    private bool _disposed;

    public UltimateFileSystem(string hostRoot, string mountName = "Usb0")
    {
        if (string.IsNullOrWhiteSpace(hostRoot))
            throw new ArgumentException("A host root directory is required", nameof(hostRoot));
        if (string.IsNullOrWhiteSpace(mountName))
            throw new ArgumentException("A mount name is required", nameof(mountName));

        var source = Path.GetFullPath(hostRoot);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException(
                $"Ultimate filesystem root not found: '{hostRoot}'");

        _mountName = mountName;
        WorkingRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), "sim6502-u64sim-" + Guid.NewGuid().ToString("N")));

        CopyTree(source, WorkingRoot);
        Logger.Debug($"Ultimate filesystem '{MountRoot}' backed by '{source}', " +
                     $"working copy at '{WorkingRoot}'");
    }

    /// <summary>Canonical host path of the throwaway working copy.</summary>
    public string WorkingRoot { get; }

    /// <summary>The Ultimate-side mount point, e.g. <c>/Usb0</c>.</summary>
    public string MountRoot => "/" + _mountName;

    /// <summary>Current directory as the C64 sees it, e.g. <c>/Usb0/data</c>.</summary>
    public string CurrentPath =>
        _current.Count == 0 ? MountRoot : MountRoot + "/" + string.Join('/', _current);

    /// <summary>
    /// Change directory. Accepts absolute paths under the mount point and relative
    /// paths, and understands <c>.</c> and <c>..</c>. Returns false and leaves the
    /// current directory untouched if the target does not exist or is not a directory.
    /// </summary>
    public bool ChangeDirectory(string path)
    {
        if (!TryNormalise(path, out var segments))
            return false;

        var host = ToHostPath(segments);
        if (host == null || !Directory.Exists(host))
            return false;

        _current.Clear();
        _current.AddRange(segments);
        return true;
    }

    /// <summary>
    /// Map an Ultimate path to a host path. Returns null when the path is malformed
    /// or resolves outside the working root. The returned path is not guaranteed to
    /// exist — callers create, read, or stat it as the command requires.
    /// </summary>
    public string? ResolveToHostPath(string ultimatePath)
    {
        return TryNormalise(ultimatePath, out var segments) ? ToHostPath(segments) : null;
    }

    /// <summary>
    /// List the current directory: directories first, then files, each group sorted
    /// by ordinal name comparison so listings are stable across platforms.
    /// </summary>
    public IReadOnlyList<UltimateDirEntry> ListCurrentDirectory()
    {
        var host = ToHostPath(_current);
        if (host == null || !Directory.Exists(host))
            return Array.Empty<UltimateDirEntry>();

        var entries = new List<UltimateDirEntry>();

        foreach (var dir in Directory.GetDirectories(host).OrderBy(p => p, StringComparer.Ordinal))
        {
            var info = new DirectoryInfo(dir);
            entries.Add(new UltimateDirEntry(info.Name, AttributeDirectory, 0, info.LastWriteTime));
        }

        foreach (var file in Directory.GetFiles(host).OrderBy(p => p, StringComparer.Ordinal))
        {
            var info = new FileInfo(file);
            entries.Add(new UltimateDirEntry(info.Name, AttributeArchive, info.Length, info.LastWriteTime));
        }

        return entries;
    }

    /// <summary>
    /// Split an Ultimate path into normalised segments relative to the mount root.
    /// Returns false for malformed input or a mount name we do not serve.
    /// <c>..</c> at the root is absorbed rather than treated as an escape, matching
    /// the upstream Path behaviour (plain chroot semantics): the segment is simply
    /// dropped and normal appends resume with whatever follows. The actual boundary
    /// is enforced afterwards by <see cref="ToHostPath"/>, which canonicalises the
    /// result and prefix-checks it against <see cref="WorkingRoot"/>.
    /// </summary>
    private bool TryNormalise(string path, out List<string> segments)
    {
        segments = new List<string>();

        if (path == null) return false;
        if (path.Contains('\0')) return false;

        var trimmed = path.Trim();
        var absolute = trimmed.StartsWith('/');
        var body = trimmed;

        if (absolute)
        {
            var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return true; // "/" means the mount root

            if (!string.Equals(parts[0], _mountName, StringComparison.OrdinalIgnoreCase))
                return false; // a mount we do not serve

            body = string.Join('/', parts.Skip(1));
        }
        else
        {
            segments.AddRange(_current);
        }

        foreach (var segment in body.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;

            if (segment == "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
                continue; // at the root this is a no-op, not an escape
            }

            segments.Add(segment);
        }

        return true;
    }

    /// <summary>
    /// Combine normalised segments with the working root and confirm the result is
    /// genuinely inside it. This is the second, independent guard: even if
    /// normalisation were wrong, nothing outside the root is ever returned.
    /// </summary>
    private string? ToHostPath(IReadOnlyList<string> segments)
    {
        string candidate;
        try
        {
            candidate = Path.GetFullPath(segments.Count == 0
                ? WorkingRoot
                : Path.Combine(WorkingRoot, Path.Combine(segments.ToArray())));
        }
        catch (Exception ex)
        {
            Logger.Debug($"Ultimate path could not be canonicalised: {ex.Message}");
            return null;
        }

        if (candidate == WorkingRoot)
            return candidate;

        var rootWithSeparator = WorkingRoot.EndsWith(Path.DirectorySeparatorChar)
            ? WorkingRoot
            : WorkingRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            Logger.Warn($"Rejected Ultimate path resolving outside '{MountRoot}': '{candidate}'");
            return null;
        }

        return candidate;
    }

    /// <summary>
    /// Recursively copy a tree, skipping symlinks so the working copy cannot be
    /// used to reach anything outside itself.
    /// </summary>
    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var dir in Directory.GetDirectories(source))
        {
            var info = new DirectoryInfo(dir);
            if (info.LinkTarget != null)
            {
                Logger.Warn($"Skipping symlinked directory '{info.Name}' when building " +
                            "the Ultimate working copy");
                continue;
            }
            CopyTree(dir, Path.Combine(destination, info.Name));
        }

        foreach (var file in Directory.GetFiles(source))
        {
            var info = new FileInfo(file);
            if (info.LinkTarget != null)
            {
                Logger.Warn($"Skipping symlinked file '{info.Name}' when building " +
                            "the Ultimate working copy");
                continue;
            }
            File.Copy(file, Path.Combine(destination, info.Name), overwrite: true);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (Directory.Exists(WorkingRoot))
                Directory.Delete(WorkingRoot, recursive: true);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not remove the Ultimate working copy '{WorkingRoot}': {ex.Message}");
        }
    }
}
