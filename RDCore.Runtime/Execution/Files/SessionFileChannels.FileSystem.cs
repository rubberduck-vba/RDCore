using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.Execution.Files;

// what the FileSystem module does to the machine (MS-VBAL 6.1.2.5): the channels are the ones that know which files are open, which most of it has to ask,
// and the one seam file I/O goes through. What the specification leaves to the implementation, and the errors it does not name, are MS-VBA's (recorded in
// Excel, oracle\s25).
internal sealed partial class SessionFileChannels
{
    // the attributes a file can be given and read back as: what SetAttr refuses any other of (G02).
    private const VBFileAttribute SettableAttributes = VBFileAttribute.VBReadOnly | VBFileAttribute.VBHidden | VBFileAttribute.VBSystem | VBFileAttribute.VBArchive;

    // what Dir has left to give of the last pattern it was given: null when there is no pattern to go on with.
    private Queue<string>? _search;

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryGetCurrentDirectory(string? drive, out string path)
    {
        path = string.Empty;
        var current = CurrentDirectory();
        if (string.IsNullOrEmpty(drive))
        {
            path = current ?? string.Empty;
            return current is null ? VBRuntimeErrorId.PathNotFound.ToCode() : null;
        }

        if (DriveOf(drive) is not { } root)
        {
            return char.IsAsciiLetter(drive[0]) ? VBRuntimeErrorId.DeviceUnavailable.ToCode() : VBRuntimeErrorId.InvalidProcedureCallOrArgument.ToCode();
        }

        // the drive's own current directory, which the file system keeps for each drive it has (C: is where C's is).
        path = current is not null && IsOnDrive(current, root) ? current : fileSystem.Path.GetFullPath(root[..2]);
        return null;
    }

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryChangeDirectory(string path)
    {
        if (Resolve(ref path) is { } unavailable)
        {
            return unavailable;
        }

        if (string.IsNullOrEmpty(path) || !fileSystem.Directory.Exists(path))
        {
            return VBRuntimeErrorId.PathNotFound.ToCode();
        }

        fileSystem.Directory.SetCurrentDirectory(fileSystem.Path.GetFullPath(path));
        return null;
    }

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryChangeDrive(string drive)
    {
        if (string.IsNullOrEmpty(drive))
        {
            return null;
        }

        if (DriveOf(drive) is not { } root)
        {
            return char.IsAsciiLetter(drive[0]) ? VBRuntimeErrorId.DeviceUnavailable.ToCode() : VBRuntimeErrorId.InvalidProcedureCallOrArgument.ToCode();
        }

        if (CurrentDirectory() is not { } current || !IsOnDrive(current, root))
        {
            fileSystem.Directory.SetCurrentDirectory(fileSystem.Path.GetFullPath(root[..2]));
        }

        return null;
    }

    // the root of the drive a drive specification's first letter names, if the file system has one.
    private string? DriveOf(string drive)
    {
        if (!char.IsAsciiLetter(drive[0]))
        {
            return null;
        }

        var letter = char.ToUpperInvariant(drive[0]);
        return fileSystem.DriveInfo.GetDrives().Select(found => found.Name).FirstOrDefault(name => name.Length >= 2 && name[1] == ':' && char.ToUpperInvariant(name[0]) == letter);
    }

    private static bool IsOnDrive(string path, string root) => path.Length >= 2 && char.ToUpperInvariant(path[0]) == char.ToUpperInvariant(root[0]) && path[1] == ':';

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryFindFirst(string pattern, VBFileAttribute attributes, out string name)
    {
        name = string.Empty;
        if (Resolve(ref pattern) is { } unavailable)
        {
            _search = null;
            return unavailable;
        }

        _search = new Queue<string>();

        // a volume label is asked for alone: with any other attribute, vbVolume is ignored.
        if (attributes == VBFileAttribute.VBVolume)
        {
            name = VolumeLabelOf(pattern);
            _search = null;
            return null;
        }

        if (!TrySplit(pattern, out var directory, out var mask) || !fileSystem.Directory.Exists(directory))
        {
            _search = null;
            return null;
        }

        foreach (var match in Matches(directory, mask, attributes))
        {
            _search.Enqueue(match);
        }

        return TryFindNext(out name);
    }

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryFindNext(out string name)
    {
        name = string.Empty;
        if (_search is null)
        {
            return VBRuntimeErrorId.InvalidProcedureCallOrArgument.ToCode();
        }

        // the last match given, what follows is a zero-length String, and then there is no pattern to go on with (D01).
        if (!_search.TryDequeue(out var next))
        {
            _search = null;
            return null;
        }

        name = next;
        return null;
    }

    // what is in a directory that a mask matches, in the order Dir gives them: . and .. first, then by their upper-case spelling (D03, D11). A directory only
    // with vbDirectory, a hidden or a system file only with vbHidden or vbSystem.
    private IEnumerable<string> Matches(string directory, string mask, VBFileAttribute attributes)
    {
        var withDirectories = attributes.HasFlag(VBFileAttribute.VBDirectory);
        var found = new List<string>();
        if (withDirectories)
        {
            found.AddRange(new[] { ".", ".." }.Where(dots => Matches(dots, mask)));
        }

        var entries = fileSystem.DirectoryInfo.New(directory).EnumerateFileSystemInfos()
            .Where(entry => Matches(entry.Name, mask))
            .Where(entry => withDirectories || !entry.Attributes.HasFlag(FileAttributes.Directory))
            .Where(entry => attributes.HasFlag(VBFileAttribute.VBHidden) || !entry.Attributes.HasFlag(FileAttributes.Hidden))
            .Where(entry => attributes.HasFlag(VBFileAttribute.VBSystem) || !entry.Attributes.HasFlag(FileAttributes.System))
            .Select(entry => entry.Name)
            .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase);
        found.AddRange(entries);
        return found;
    }

    // a mask's * is any run of characters and ? any one, whatever their case; *.* is anything, as it always was on the file systems VBA grew up on.
    private static bool Matches(string name, string mask)
        => mask is "*" or "*.*" || System.IO.Enumeration.FileSystemName.MatchesWin32Expression(mask, name, ignoreCase: true);

    // the directory a pattern is in, and the mask of the names in it: a pattern ending in a separator is everything in the directory, and no pattern at all
    // is everything in the current one (D04, D07).
    private bool TrySplit(string pattern, out string directory, out string mask)
    {
        directory = string.Empty;
        mask = "*";
        try
        {
            if (pattern.Length == 0)
            {
                directory = fileSystem.Directory.GetCurrentDirectory();
                return true;
            }

            var full = fileSystem.Path.GetFullPath(pattern);
            if (pattern.EndsWith(fileSystem.Path.DirectorySeparatorChar) || pattern.EndsWith(fileSystem.Path.AltDirectorySeparatorChar))
            {
                directory = full;
                return true;
            }

            directory = fileSystem.Path.GetDirectoryName(full) ?? full;
            mask = fileSystem.Path.GetFileName(full);
            return mask.Length > 0;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            // a pattern no path can be made of matches nothing, as one that names no directory does (D02, D05).
            return false;
        }
    }

    private string VolumeLabelOf(string pattern)
    {
        try
        {
            var root = fileSystem.Path.GetPathRoot(fileSystem.Path.GetFullPath(pattern.Length == 0 ? "." : pattern));
            return root is { Length: > 0 } ? fileSystem.DriveInfo.New(root).VolumeLabel : string.Empty;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryCopy(string source, string destination)
    {
        if ((Resolve(ref source) ?? Resolve(ref destination)) is { } unavailable)
        {
            return unavailable;
        }

        if (HasWildcard(source))
        {
            return VBRuntimeErrorId.BadFileNameOrNumber.ToCode();
        }

        if (!fileSystem.File.Exists(source))
        {
            return VBRuntimeErrorId.FileNotFound.ToCode();
        }

        // a source open for Input is copied, open in any other mode it is not; the destination open in any mode is not replaced (Y02, Y11, Y12).
        if (_channels.Values.Any(channel => PathsMatch(channel.FullPath, source) && channel.Mode is not VBFileMode.Input)
            || _channels.Values.Any(channel => PathsMatch(channel.FullPath, destination)))
        {
            return VBRuntimeErrorId.FileAlreadyOpen.ToCode();
        }

        if (fileSystem.Directory.Exists(destination))
        {
            return VBRuntimeErrorId.PathOrFileAccessError.ToCode();
        }

        if (!ParentExists(destination))
        {
            return VBRuntimeErrorId.PathNotFound.ToCode();
        }

        return Attempt(() => fileSystem.File.Copy(source, destination, overwrite: true));
    }

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryDelete(string pattern)
    {
        if (Resolve(ref pattern) is { } unavailable)
        {
            return unavailable;
        }

        // no pattern is no file - not every file of the current directory, which is what it is to Dir.
        if (pattern.Length == 0 || !TrySplit(pattern, out var directory, out var mask) || !fileSystem.Directory.Exists(directory))
        {
            return VBRuntimeErrorId.FileNotFound.ToCode();
        }

        var files = Matches(directory, mask, VBFileAttribute.VBNormal).Select(match => fileSystem.Path.Combine(directory, match)).ToList();
        if (files.Count == 0)
        {
            return VBRuntimeErrorId.FileNotFound.ToCode();
        }

        foreach (var file in files)
        {
            if (_channels.Values.Any(channel => PathsMatch(channel.FullPath, file)))
            {
                return VBRuntimeErrorId.FileAlreadyOpen.ToCode();
            }

            // a read-only file is not deleted (K03).
            if (fileSystem.File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly))
            {
                return VBRuntimeErrorId.PathOrFileAccessError.ToCode();
            }

            if (Attempt(() => fileSystem.File.Delete(file)) is { } failed)
            {
                return failed;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryCreateDirectory(string path)
    {
        if (Resolve(ref path) is { } unavailable)
        {
            return unavailable;
        }

        if (fileSystem.Directory.Exists(path) || fileSystem.File.Exists(path))
        {
            return VBRuntimeErrorId.PathOrFileAccessError.ToCode();
        }

        // the directory it is in is not made for it (M01).
        if (!ParentExists(path))
        {
            return VBRuntimeErrorId.PathNotFound.ToCode();
        }

        return Attempt(() => fileSystem.Directory.CreateDirectory(path));
    }

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryRemoveDirectory(string path)
    {
        if (Resolve(ref path) is { } unavailable)
        {
            return unavailable;
        }

        // a file, a pattern, are no directory (M02, M03).
        if (HasWildcard(path) || !fileSystem.Directory.Exists(path))
        {
            return VBRuntimeErrorId.PathNotFound.ToCode();
        }

        if (fileSystem.Directory.EnumerateFileSystemEntries(path).Any())
        {
            return VBRuntimeErrorId.PathOrFileAccessError.ToCode();
        }

        return Attempt(() => fileSystem.Directory.Delete(path, recursive: false));
    }

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryGetAttributes(string path, out VBFileAttribute attributes)
    {
        attributes = VBFileAttribute.VBNormal;
        if ((Resolve(ref path) ?? TryFind(path)) is { } missing)
        {
            return missing;
        }

        var held = fileSystem.File.GetAttributes(path);
        attributes = (held.HasFlag(FileAttributes.ReadOnly) ? VBFileAttribute.VBReadOnly : 0)
            | (held.HasFlag(FileAttributes.Hidden) ? VBFileAttribute.VBHidden : 0)
            | (held.HasFlag(FileAttributes.System) ? VBFileAttribute.VBSystem : 0)
            | (held.HasFlag(FileAttributes.Directory) ? VBFileAttribute.VBDirectory : 0)
            | (held.HasFlag(FileAttributes.Archive) ? VBFileAttribute.VBArchive : 0);
        return null;
    }

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TrySetAttributes(string path, VBFileAttribute attributes)
    {
        if ((attributes & ~SettableAttributes) != 0)
        {
            return VBRuntimeErrorId.InvalidProcedureCallOrArgument.ToCode();
        }

        if ((Resolve(ref path) ?? TryFind(path)) is { } missing)
        {
            return missing;
        }

        // a file open for Input is given them, open in any other mode it is not (G03, G11).
        if (_channels.Values.Any(channel => PathsMatch(channel.FullPath, path) && channel.Mode is not VBFileMode.Input))
        {
            return VBRuntimeErrorId.FileAlreadyOpen.ToCode();
        }

        var directory = fileSystem.File.GetAttributes(path) & FileAttributes.Directory;
        var given = (attributes.HasFlag(VBFileAttribute.VBReadOnly) ? FileAttributes.ReadOnly : 0)
            | (attributes.HasFlag(VBFileAttribute.VBHidden) ? FileAttributes.Hidden : 0)
            | (attributes.HasFlag(VBFileAttribute.VBSystem) ? FileAttributes.System : 0)
            | (attributes.HasFlag(VBFileAttribute.VBArchive) ? FileAttributes.Archive : 0)
            | directory;
        return Attempt(() => fileSystem.File.SetAttributes(path, given == 0 ? FileAttributes.Normal : given));
    }

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryGetLength(string path, out long length)
    {
        length = 0;
        if ((Resolve(ref path) ?? TryFind(path)) is { } missing)
        {
            return missing;
        }

        if (fileSystem.Directory.Exists(path))
        {
            return null;
        }

        // what the file system has of a file a channel has open: not what an Output or an Append channel has written yet - the length it had once it was
        // opened - and what a Binary or a Random one has (L02, L11).
        if (_channels.Values.FirstOrDefault(channel => PathsMatch(channel.FullPath, path)) is { } open && open.Mode is not VBFileMode.Input)
        {
            length = open.Mode is VBFileMode.Output or VBFileMode.Append ? open.Origin : open.Length;
            return null;
        }

        length = fileSystem.FileInfo.New(path).Length;
        return null;
    }

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryReadAll(string path, out byte[] bytes)
    {
        bytes = [];
        if (path.Length == 0)
        {
            return VBRuntimeErrorId.FileNotFound.ToCode();
        }

        if (Resolve(ref path) is { } unavailable)
        {
            return unavailable;
        }

        if (HasWildcard(path))
        {
            return VBRuntimeErrorId.BadFileNameOrNumber.ToCode();
        }

        if (fileSystem.Directory.Exists(path))
        {
            return VBRuntimeErrorId.PathOrFileAccessError.ToCode();
        }

        if (!fileSystem.File.Exists(path))
        {
            return ParentExists(path) ? VBRuntimeErrorId.FileNotFound.ToCode() : VBRuntimeErrorId.PathNotFound.ToCode();
        }

        byte[] read = [];
        var error = Attempt(() => read = fileSystem.File.ReadAllBytes(path));
        bytes = read;
        return error;
    }

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryWriteAll(string path, byte[] bytes)
    {
        if (path.Length == 0)
        {
            return VBRuntimeErrorId.PathOrFileAccessError.ToCode();
        }

        if (Resolve(ref path) is { } unavailable)
        {
            return unavailable;
        }

        if (HasWildcard(path))
        {
            return VBRuntimeErrorId.BadFileNameOrNumber.ToCode();
        }

        if (fileSystem.Directory.Exists(path))
        {
            return VBRuntimeErrorId.PathOrFileAccessError.ToCode();
        }

        if (!ParentExists(path))
        {
            return VBRuntimeErrorId.PathNotFound.ToCode();
        }

        // shared as a channel opens a file: one the program has open is written too, unless its channel locked it.
        return Attempt(() =>
        {
            using var stream = fileSystem.FileStream.New(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(bytes);
        });
    }

    /// <inheritdoc/>
    public RDCoreDiagnosticCode? TryGetLastWriteTime(string path, out DateTime modified)
    {
        modified = default;
        if ((Resolve(ref path) ?? TryFind(path)) is { } missing)
        {
            return missing;
        }

        modified = fileSystem.Directory.Exists(path) ? fileSystem.Directory.GetLastWriteTime(path) : fileSystem.File.GetLastWriteTime(path);
        return null;
    }

    // a path that is no pattern, and names a file or a directory that exists (G02, G03, L01, L03, T01).
    private RDCoreDiagnosticCode? TryFind(string path)
        => HasWildcard(path) ? VBRuntimeErrorId.BadFileNameOrNumber.ToCode()
            : fileSystem.File.Exists(path) || fileSystem.Directory.Exists(path) ? null
            : VBRuntimeErrorId.FileNotFound.ToCode();

    // what refuses an operation the checks let through: access, 70; anything else about the path or the device, 75.
    private static RDCoreDiagnosticCode? Attempt(Action operation)
    {
        try
        {
            operation();
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return VBRuntimeErrorId.PermissionDenied.ToCode();
        }
        catch (IOException)
        {
            return VBRuntimeErrorId.PathOrFileAccessError.ToCode();
        }
    }
}
