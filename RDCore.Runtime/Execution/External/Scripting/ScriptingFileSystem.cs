using System.IO.Abstractions;
using System.IO.Enumeration;
using System.Text;
using RDCore.Runtime.Execution.Files;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution.External.Scripting;

/// <summary>
/// The file system a <c>FileSystemObject</c> acts on: the session's own, through the path handling of its VBA file functions, so that a path means the same
/// to both, in the same current directory. What an operation refuses is the error scrrun.dll raises for it.
/// </summary>
internal sealed class ScriptingFileSystem
{
    public const int ForReading = 1;
    public const int ForWriting = 2;
    public const int ForAppending = 8;

    // the bits of scrrun.dll's FileAttribute: a program sets the first three and Archive; the others are the file system's to say.
    private const int ReadOnlyBit = 1;
    private const int HiddenBit = 2;
    private const int SystemBit = 4;
    private const int DirectoryBit = 16;
    private const int ArchiveBit = 32;
    private const int AliasBit = 1024;
    private const int CompressedBit = 2048;

    private static readonly char[] Separators = ['\\', '/'];

    private readonly SessionFileChannels channels;

    private ScriptingFileSystem(SessionFileChannels channels) => this.channels = channels;

    /// <summary>
    /// The file system of a session whose files are the platform's own channels; <see langword="null"/> for any other.
    /// </summary>
    /// <param name="session">The session.</param>
    public static ScriptingFileSystem? Of(IRuntimeSession session) => session.Files is SessionFileChannels channels ? new(channels) : null;

    public IFileSystem Files => channels.FileSystem;

    // names are one whatever their case where the file system's are, as Windows' are.
    public static StringComparer Names => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsSeparator(char character) => character is '\\' or '/';

    private static bool HasWildcard(string path) => path.AsSpan().IndexOfAny('*', '?') >= 0;

    // scrrun.dll's reading of a path, the same on every platform: \ and / both separate, and a drive is a letter and a colon, or a UNC server and share.
    public static string DriveName(string path)
    {
        if (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]))
        {
            return path[..2];
        }

        if (path.Length < 3 || !IsSeparator(path[0]) || !IsSeparator(path[1]))
        {
            return string.Empty;
        }

        var server = path.AsSpan(2).IndexOfAny(Separators);
        var share = server < 0 ? -1 : path.AsSpan(3 + server).IndexOfAny(Separators);
        return share < 0 ? path : path[..(3 + server + share)];
    }

    // the last part of a path, without the separator it may end with; a root's is nothing.
    public static string FileName(string path)
    {
        var rest = path.AsSpan(DriveName(path).Length).TrimEnd(Separators);
        return rest[(rest.LastIndexOfAny(Separators) + 1)..].ToString();
    }

    // the name before its last dot (.hidden has none), and the extension after it.
    public static string BaseName(string path)
    {
        var name = FileName(path);
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[..dot];
    }

    public static string ExtensionName(string path)
    {
        var name = FileName(path);
        var dot = name.LastIndexOf('.');
        return dot < 0 ? string.Empty : name[(dot + 1)..];
    }

    // the folder a path is in: none for a root, the drive's root for what is directly in it (C:\a is in C:\), the drive for a name relative to it (C:a).
    public static string ParentFolderName(string path)
    {
        var drive = DriveName(path);
        var rest = path.AsSpan(drive.Length).TrimEnd(Separators);
        if (rest.IsEmpty)
        {
            return string.Empty;
        }

        var cut = rest.LastIndexOfAny(Separators);
        if (cut < 0)
        {
            return drive;
        }

        var parent = rest[..cut].TrimEnd(Separators);
        return !parent.IsEmpty ? string.Concat(drive, parent)
            : drive.Length == 2 ? string.Concat(drive, rest.Slice(cut, 1))
            : drive;
    }

    // a name joined to a path by the file system's separator, unless one of them has one there or the path is a drive (C:b).
    public string BuildPath(string path, string name)
        => path.Length == 0 ? name
            : name.Length == 0 ? path
            : IsSeparator(path[^1]) || IsSeparator(name[0]) || path is [var letter, ':'] && char.IsAsciiLetter(letter) ? path + name
            : $"{path}{Files.Path.DirectorySeparatorChar}{name}";

    public VBRuntimeErrorId? TryAbsolute(string path, out string full)
    {
        full = string.Empty;
        if (Native(path) is not { } native)
        {
            return VBRuntimeErrorId.PathNotFound;
        }

        try
        {
            full = Full(native.Length == 0 ? "." : native);
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return VBRuntimeErrorId.InvalidProcedureCallOrArgument;
        }
    }

    // a path as the file system has it, in the current directory unless it is fully qualified; null once the current directory is gone (76).
    private string? Native(string path) => channels.TryResolve(ref path) ? path : null;

    // a full path, without the separator it may end with, which a root keeps (C:\).
    private string Full(string native)
    {
        var full = Files.Path.GetFullPath(native);
        return full.Length > (Files.Path.GetPathRoot(full)?.Length ?? 0) ? full.TrimEnd(Separators) : full;
    }

    private bool ParentExists(string native)
        => Files.Path.GetDirectoryName(Files.Path.GetFullPath(native)) is not { Length: > 0 } parent || Files.Directory.Exists(parent);

    public bool FileExists(string path) => Native(path) is { } native && !HasWildcard(native) && Files.File.Exists(native);

    public bool FolderExists(string path) => Native(path) is { } native && !HasWildcard(native) && Files.Directory.Exists(native);

    public VBRuntimeErrorId? TryGetFile(string path, out string full)
    {
        full = string.Empty;
        if (Native(path) is not { } native)
        {
            return VBRuntimeErrorId.PathNotFound;
        }

        if (HasWildcard(native) || !Files.File.Exists(native))
        {
            return VBRuntimeErrorId.FileNotFound;
        }

        full = Full(native);
        return null;
    }

    public VBRuntimeErrorId? TryGetFolder(string path, out string full)
    {
        full = string.Empty;
        if (Native(path) is not { } native || HasWildcard(native) || !Files.Directory.Exists(native))
        {
            return VBRuntimeErrorId.PathNotFound;
        }

        full = Full(native);
        return null;
    }

    // a drive is named by its letter (C), with a colon (C:) and its root's separator (C:\), by a share (\\server\share), or by the root a file system
    // mounts it at (/): anything more names no drive (5). A drive whose root is not there is one the machine has not (68).
    public VBRuntimeErrorId? TryGetDrive(string spec, out string name)
    {
        name = spec is [var letter] && char.IsAsciiLetter(letter) ? $"{char.ToUpperInvariant(letter)}:" : DriveName(spec);
        if (name.Length == 2)
        {
            name = $"{char.ToUpperInvariant(name[0])}:";
        }

        if (name.Length == 0 || spec.Length > name.Length + 1 || spec.Length == name.Length + 1 && !IsSeparator(spec[^1]))
        {
            if (Mounted(spec) is not { } mounted)
            {
                return VBRuntimeErrorId.InvalidProcedureCallOrArgument;
            }

            name = mounted;
            return null;
        }

        return Files.Directory.Exists(RootOf(name)) ? null : VBRuntimeErrorId.DeviceUnavailable;
    }

    // the drive a file system mounts at a root it names, where drives have no letters.
    private string? Mounted(string spec)
        => spec.Length == 0 || Files.Path.DirectorySeparatorChar == '\\' ? null
            : Drives().FirstOrDefault(name => name.Equals(spec, StringComparison.Ordinal) || name.Equals(spec.TrimEnd('/'), StringComparison.Ordinal));

    // the machine's drives, by the name a Drive object has: C: where drives have letters, the root they are mounted at where they have none.
    public IReadOnlyList<string> Drives()
        => [.. Files.DriveInfo.GetDrives().Select(drive => DriveNameOf(drive.Name)).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string DriveNameOf(string root)
        => root.Length >= 2 && root[1] == ':' ? root[..2].ToUpperInvariant() : root.Length > 1 ? root.TrimEnd(Separators) : root;

    public IDriveInfo? DriveInfoOf(string name)
    {
        try
        {
            return Files.DriveInfo.New(name.Length == 2 ? name + Files.Path.DirectorySeparatorChar : name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public string RootOf(string drive) => drive.Length == 2 ? drive + Files.Path.DirectorySeparatorChar : drive;

    // the drive a full path is on.
    public string DriveOf(string full)
        => DriveName(full) is { Length: > 0 } drive ? (drive.Length == 2 ? drive.ToUpperInvariant() : drive)
            : Drives().Where(full.StartsWith).MaxBy(root => root.Length) ?? "/";

    // Windows' folders, and the one temporary files go in; a platform that has no Windows folder has none to give (76).
    public VBRuntimeErrorId? TryGetSpecialFolder(int folder, out string full)
    {
        full = folder switch
        {
            0 => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            1 => Environment.GetFolderPath(Environment.SpecialFolder.System),
            2 => Full(Files.Path.GetTempPath()),
            _ => string.Empty,
        };
        return folder is < 0 or > 2 ? VBRuntimeErrorId.InvalidProcedureCallOrArgument
            : full.Length == 0 ? VBRuntimeErrorId.PathNotFound
            : null;
    }

    // rad, five hexadecimal digits and .tmp, as scrrun.dll names one.
    public static string TempName() => $"rad{Random.Shared.Next(0x100000):X5}.tmp";

    public VBRuntimeErrorId? TryCreateFolder(string path, out string full)
    {
        full = string.Empty;
        if (Native(path) is not { } native)
        {
            return VBRuntimeErrorId.PathNotFound;
        }

        if (Files.Directory.Exists(native) || Files.File.Exists(native))
        {
            return VBRuntimeErrorId.FileAlreadyExists;
        }

        if (!ParentExists(native))
        {
            return VBRuntimeErrorId.PathNotFound;
        }

        var made = Full(native);
        full = made;
        return Attempt(() => Files.Directory.CreateDirectory(made));
    }

    // the files or the folders a specification names: itself, or what the wildcards of its last part match in its folder, in the order of their names.
    private List<string> Matching(string native, bool folders)
    {
        var mask = Files.Path.GetFileName(native);
        if (!HasWildcard(mask))
        {
            return (folders ? Files.Directory.Exists(native) : Files.File.Exists(native)) ? [Full(native)] : [];
        }

        if (Files.Path.GetDirectoryName(Files.Path.GetFullPath(native)) is not { } directory || !Files.Directory.Exists(directory))
        {
            return [];
        }

        var found = folders ? Files.Directory.EnumerateDirectories(directory) : Files.Directory.EnumerateFiles(directory);
        return [.. found.Where(entry => FileSystemName.MatchesWin32Expression(mask, Files.Path.GetFileName(entry), ignoreCase: true))
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    // a read-only file is deleted only when it is forced to be (70).
    public VBRuntimeErrorId? DeleteFile(string spec, bool force)
    {
        if (Native(spec) is not { } native)
        {
            return VBRuntimeErrorId.PathNotFound;
        }

        var matched = Matching(native, folders: false);
        if (matched.Count == 0)
        {
            return VBRuntimeErrorId.FileNotFound;
        }

        foreach (var file in matched)
        {
            if ((Writable(file, force) ?? Attempt(() => Files.File.Delete(file))) is { } refused)
            {
                return refused;
            }
        }

        return null;
    }

    public VBRuntimeErrorId? DeleteFolder(string spec, bool force)
    {
        if (Native(spec) is not { } native)
        {
            return VBRuntimeErrorId.PathNotFound;
        }

        var matched = Matching(native, folders: true);
        if (matched.Count == 0)
        {
            return VBRuntimeErrorId.PathNotFound;
        }

        foreach (var folder in matched)
        {
            var held = Files.Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories).Append(folder);
            foreach (var entry in held)
            {
                if (Writable(entry, force) is { } refused)
                {
                    return refused;
                }
            }

            if (Attempt(() => Files.Directory.Delete(folder, recursive: true)) is { } failed)
            {
                return failed;
            }
        }

        return null;
    }

    // a read-only file or folder is changed only when it is forced to be, which makes it writable first.
    private VBRuntimeErrorId? Writable(string full, bool force)
    {
        var attributes = Files.File.GetAttributes(full);
        if (!attributes.HasFlag(FileAttributes.ReadOnly))
        {
            return null;
        }

        return force ? Attempt(() => Files.File.SetAttributes(full, attributes & ~FileAttributes.ReadOnly)) : VBRuntimeErrorId.PermissionDenied;
    }

    // a destination that ends with a separator, or that wildcards copy to, is the folder the files go in, which must be there (76); any other is the copy.
    public VBRuntimeErrorId? CopyFile(string source, string destination, bool overwrite)
        => Transfer(source, destination, folders: false, (from, to) => CopyFileTo(from, to, overwrite), out _);

    public VBRuntimeErrorId? MoveFile(string source, string destination, out string? moved)
        => Transfer(source, destination, folders: false, MoveTo, out moved);

    public VBRuntimeErrorId? CopyFolder(string source, string destination, bool overwrite)
        => Transfer(source, destination, folders: true, (from, to) => CopyFolderTo(from, to, overwrite), out _);

    public VBRuntimeErrorId? MoveFolder(string source, string destination, out string? moved)
        => Transfer(source, destination, folders: true, MoveTo, out moved);

    private VBRuntimeErrorId? Transfer(string source, string destination, bool folders, Func<string, string, VBRuntimeErrorId?> each, out string? last)
    {
        last = null;
        if (Native(source) is not { } from || Native(destination) is not { } to)
        {
            return VBRuntimeErrorId.PathNotFound;
        }

        var matched = Matching(from, folders);
        if (matched.Count == 0)
        {
            return folders ? VBRuntimeErrorId.PathNotFound : VBRuntimeErrorId.FileNotFound;
        }

        var into = HasWildcard(Files.Path.GetFileName(from)) || to.Length > 0 && IsSeparator(to[^1]);
        if (into && !Files.Directory.Exists(to))
        {
            return VBRuntimeErrorId.PathNotFound;
        }

        foreach (var entry in matched)
        {
            var target = into ? Files.Path.Combine(Full(to), Files.Path.GetFileName(entry)) : Full(to);
            if (!ParentExists(target))
            {
                return VBRuntimeErrorId.PathNotFound;
            }

            if (each(entry, target) is { } refused)
            {
                return refused;
            }

            last = target;
        }

        return null;
    }

    // a file is copied over another only when it may be (58), and never over a folder or a read-only file (70).
    private VBRuntimeErrorId? CopyFileTo(string from, string to, bool overwrite)
    {
        if (Files.Directory.Exists(to))
        {
            return VBRuntimeErrorId.PermissionDenied;
        }

        if (Files.File.Exists(to))
        {
            if (!overwrite)
            {
                return VBRuntimeErrorId.FileAlreadyExists;
            }

            if (Files.File.GetAttributes(to).HasFlag(FileAttributes.ReadOnly))
            {
                return VBRuntimeErrorId.PermissionDenied;
            }
        }

        return Attempt(() => Files.File.Copy(from, to, overwrite: true));
    }

    // a folder copied onto one that is there is merged into it, a file of its at a time.
    private VBRuntimeErrorId? CopyFolderTo(string from, string to, bool overwrite)
    {
        if (Files.File.Exists(to))
        {
            return VBRuntimeErrorId.FileAlreadyExists;
        }

        if (Attempt(() => Files.Directory.CreateDirectory(to)) is { } failed)
        {
            return failed;
        }

        foreach (var file in Files.Directory.EnumerateFiles(from))
        {
            if (CopyFileTo(file, Files.Path.Combine(to, Files.Path.GetFileName(file)), overwrite) is { } refused)
            {
                return refused;
            }
        }

        foreach (var folder in Files.Directory.EnumerateDirectories(from))
        {
            if (CopyFolderTo(folder, Files.Path.Combine(to, Files.Path.GetFileName(folder)), overwrite) is { } refused)
            {
                return refused;
            }
        }

        return null;
    }

    // nothing is moved onto a file or a folder that is there (58).
    private VBRuntimeErrorId? MoveTo(string from, string to)
        => Files.File.Exists(to) || Files.Directory.Exists(to) ? VBRuntimeErrorId.FileAlreadyExists
            : Attempt(() =>
            {
                if (Files.File.Exists(from))
                {
                    Files.File.Move(from, to);
                }
                else
                {
                    Files.Directory.Move(from, to);
                }
            });

    // the Name of a File or a Folder set: it is renamed in its folder, and a name that is there is 58.
    public VBRuntimeErrorId? Rename(string full, string name, out string renamed)
    {
        renamed = Files.Path.Combine(Files.Path.GetDirectoryName(full) ?? string.Empty, name);
        return Files.File.Exists(renamed) || Files.Directory.Exists(renamed) ? VBRuntimeErrorId.FileAlreadyExists : MoveTo(full, renamed);
    }

    public int AttributesOf(string full)
    {
        var held = Files.File.GetAttributes(full);
        return (held.HasFlag(FileAttributes.ReadOnly) ? ReadOnlyBit : 0)
            | (held.HasFlag(FileAttributes.Hidden) ? HiddenBit : 0)
            | (held.HasFlag(FileAttributes.System) ? SystemBit : 0)
            | (held.HasFlag(FileAttributes.Directory) ? DirectoryBit : 0)
            | (held.HasFlag(FileAttributes.Archive) ? ArchiveBit : 0)
            | (held.HasFlag(FileAttributes.ReparsePoint) ? AliasBit : 0)
            | (held.HasFlag(FileAttributes.Compressed) ? CompressedBit : 0);
    }

    public VBRuntimeErrorId? SetAttributes(string full, int attributes)
    {
        var given = ((attributes & ReadOnlyBit) != 0 ? FileAttributes.ReadOnly : 0)
            | ((attributes & HiddenBit) != 0 ? FileAttributes.Hidden : 0)
            | ((attributes & SystemBit) != 0 ? FileAttributes.System : 0)
            | ((attributes & ArchiveBit) != 0 ? FileAttributes.Archive : 0)
            | (Files.File.GetAttributes(full) & FileAttributes.Directory);
        return Attempt(() => Files.File.SetAttributes(full, given == 0 ? FileAttributes.Normal : given));
    }

    public IFileSystemInfo InfoOf(string full, bool folder) => folder ? Files.DirectoryInfo.New(full) : Files.FileInfo.New(full);

    public long SizeOf(string full) => Files.FileInfo.New(full).Length;

    // a folder's size is that of every file in it and in its folders.
    public long FolderSizeOf(string full)
        => Files.Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Sum(file => Files.FileInfo.New(file).Length);

    public IReadOnlyList<string> FilesIn(string folder) => [.. Files.Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase)];

    public IReadOnlyList<string> FoldersIn(string folder) => [.. Files.Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase)];

    public bool IsRoot(string full) => string.Equals(Files.Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase);

    // a file's type as the shell names one it has no registered name of: its extension and File.
    public static string TypeOf(string full, bool folder)
        => folder ? "File folder" : ExtensionName(full) is { Length: > 0 } extension ? $"{extension.ToUpperInvariant()} File" : "File";

    // OpenTextFile and OpenAsTextStream: a file read, written over or appended to. One that is not there is 53, unless it is to be created.
    public VBRuntimeErrorId? TryOpenText(string path, int mode, bool create, bool unicode, out ScriptingTextStream? stream)
    {
        stream = null;
        if (mode is not (ForReading or ForWriting or ForAppending))
        {
            return VBRuntimeErrorId.InvalidProcedureCallOrArgument;
        }

        if (Native(path) is not { } native)
        {
            return VBRuntimeErrorId.PathNotFound;
        }

        if (Files.Directory.Exists(native))
        {
            return VBRuntimeErrorId.PermissionDenied;
        }

        var exists = Files.File.Exists(native);
        if (!exists && (!create || !ParentExists(native)))
        {
            return create ? VBRuntimeErrorId.PathNotFound : VBRuntimeErrorId.FileNotFound;
        }

        ScriptingTextStream? opened = null;
        var failure = Attempt(() => opened = mode == ForReading ? Reading(native, unicode) : Writing(native, mode == ForAppending && exists, unicode));
        stream = opened;
        return failure;
    }

    // CreateTextFile: a new file, or one written over where it may be (58).
    public VBRuntimeErrorId? TryCreateText(string path, bool overwrite, bool unicode, out ScriptingTextStream? stream)
    {
        stream = null;
        if (Native(path) is not { } native)
        {
            return VBRuntimeErrorId.PathNotFound;
        }

        if (Files.Directory.Exists(native) || Files.File.Exists(native) && !overwrite)
        {
            return VBRuntimeErrorId.FileAlreadyExists;
        }

        if (!ParentExists(native))
        {
            return VBRuntimeErrorId.PathNotFound;
        }

        ScriptingTextStream? opened = null;
        var failure = Attempt(() => opened = Writing(native, appending: false, unicode));
        stream = opened;
        return failure;
    }

    // a Unicode file is UTF-16LE after its byte order mark, an ANSI one is of the session's code page.
    private string Decoded(byte[] bytes, bool unicode)
        => unicode ? Encoding.Unicode.GetString(bytes.AsSpan(bytes is [0xFF, 0xFE, ..] ? 2 : 0)) : channels.AnsiEncoding.GetString(bytes);

    private byte[] ReadAll(string native)
    {
        using var input = Files.FileStream.New(native, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        return bytes.ToArray();
    }

    private ScriptingTextStream Reading(string native, bool unicode) => ScriptingTextStream.Reading(Decoded(ReadAll(native), unicode));

    // what an appended file has already is where its line and column start; a new Unicode file begins with its byte order mark.
    private ScriptingTextStream Writing(string native, bool appending, bool unicode)
    {
        var existing = appending ? ReadAll(native) : [];
        var output = Files.FileStream.New(native, appending ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        var encoding = unicode ? Encoding.Unicode : channels.AnsiEncoding;
        if (unicode && existing.Length == 0)
        {
            output.Write([0xFF, 0xFE]);
        }

        return ScriptingTextStream.Writing(output, encoding, Decoded(existing, unicode));
    }

    // what refuses an operation the checks let through: access, or a file another program holds, as scrrun.dll has them both (70).
    private static VBRuntimeErrorId? Attempt(Action operation)
    {
        try
        {
            operation();
            return null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return VBRuntimeErrorId.PermissionDenied;
        }
    }
}
