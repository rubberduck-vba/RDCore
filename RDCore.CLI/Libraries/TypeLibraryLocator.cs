using Microsoft.Win32;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace RDCore.CLI.Libraries;

/// <summary>
/// A type library of this machine, loaded.
/// </summary>
/// <param name="Library">The library.</param>
/// <param name="Name">The name the library gives itself: what a project references it by.</param>
/// <param name="File">The file it was loaded from, when the machine says which.</param>
internal sealed record LoadedTypeLibrary(ITypeLib Library, string Name, string? File);

/// <summary>
/// Finds and loads the type libraries of the machine the platform runs on: the Windows registry says which libraries there are and where they are.
/// </summary>
/// <remarks>
/// Only <see cref="Load"/> by name needs the registry's list of every library, and a library is found by the name it gives itself and by the description the
/// registry holds for it ("Microsoft Scripting Runtime" is <c>Scripting</c>). Nothing else about a library identifies it to the platform: the identifier and
/// version a machine gives it are a fact of the machine.
/// </remarks>
internal static class TypeLibraryLocator
{
    private const int RegKindNone = 2;

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int LoadTypeLibEx([MarshalAs(UnmanagedType.LPWStr)] string file, int regKind, out ITypeLib library);

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int LoadRegTypeLib(ref Guid guid, short major, short minor, int lcid, out ITypeLib library);

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int QueryPathOfRegTypeLib(ref Guid guid, ushort major, ushort minor, int lcid, [MarshalAs(UnmanagedType.BStr)] out string path);

    /// <summary>
    /// Loads a library.
    /// </summary>
    /// <param name="reference">
    /// A file (<c>scrrun.dll</c>, <c>EXCEL.EXE</c>, a <c>.tlb</c>), a library a file holds after its first, by the number of its resource (<c>MSVBVM60.DLL\3</c>), the
    /// identifier a machine gave a library, or the name of one.
    /// </param>
    /// <param name="progress">Told what is being done when it takes a while.</param>
    /// <returns>The library, or <see langword="null"/> when the machine has none of the reference.</returns>
    public static LoadedTypeLibrary? Load(string reference, Action<string>? progress = null)
    {
        if (File.Exists(reference))
        {
            return FromFile(Path.GetFullPath(reference));
        }

        if (ResourceOf(reference) is var (file, resource))
        {
            return FromFile($@"{Path.GetFullPath(file)}\{resource}");
        }

        if (Guid.TryParse(reference, out var guid))
        {
            return Registered(guid) is { } registered ? FromFile(registered) : null;
        }

        return ByName(reference, progress);
    }

    /// <summary>
    /// The name of the file a library was loaded from, without the folder it is in on this machine: with the number of its resource when it is not the file's
    /// first library (<c>MSVBVM60.DLL\3</c>), as a <c>.vbp</c> references it.
    /// </summary>
    /// <param name="path">The path the library was loaded from.</param>
    public static string FileNameOf(string path)
        => ResourceOf(path) is var (file, resource) ? $@"{Path.GetFileName(file)}\{resource}" : Path.GetFileName(path);

    // a type library a file holds besides its first is the file's resource of that number, which LoadTypeLibEx loads when it is written after the file's path.
    private static (string File, int Resource)? ResourceOf(string reference)
    {
        var separator = reference.LastIndexOfAny(['\\', '/']);
        return separator > 0 && int.TryParse(reference.AsSpan(separator + 1), System.Globalization.NumberStyles.None, null, out var resource) && resource > 0
            && File.Exists(reference[..separator])
                ? (reference[..separator], resource)
                : null;
    }

    private static LoadedTypeLibrary? FromFile(string path)
        => LoadTypeLibEx(path, RegKindNone, out var library) >= 0 ? Described(library, path) : null;

    /// <summary>
    /// A library that is already loaded, with the file the machine registers it in, when it registers it.
    /// </summary>
    /// <param name="library">The library.</param>
    public static LoadedTypeLibrary Of(ITypeLib library)
    {
        library.GetLibAttr(out var pointer);
        try
        {
            var attributes = Marshal.PtrToStructure<TYPELIBATTR>(pointer);
            var id = attributes.guid;
            return Described(library, PathOf(id, attributes.wMajorVerNum, attributes.wMinorVerNum));
        }
        finally
        {
            library.ReleaseTLibAttr(pointer);
        }
    }

    private static LoadedTypeLibrary Described(ITypeLib library, string? path)
    {
        library.GetDocumentation(-1, out var name, out _, out _, out _);
        return new LoadedTypeLibrary(library, name, path);
    }

    // the libraries a machine has are registered under HKCR\TypeLib\{id}\{major.minor}\{lcid}\{win32|win64}.
    private static LoadedTypeLibrary? ByName(string name, Action<string>? progress)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);
        using var typeLibs = root.OpenSubKey("TypeLib");
        if (typeLibs is null)
        {
            return null;
        }

        var candidates = new List<(Guid Id, string? Description, string Path)>();
        foreach (var key in typeLibs.GetSubKeyNames().Where(key => Guid.TryParse(key, out _)))
        {
            var id = Guid.Parse(key);
            if (Registered(id) is { } path)
            {
                using var version = typeLibs.OpenSubKey($@"{key}\{VersionKey(id)}");
                candidates.Add((id, version?.GetValue(null) as string, path));
            }
        }

        // the description the registry holds is what the Object Browser shows, and it is cheaper than loading every library to read its name.
        var named = candidates
            .Where(candidate => string.Equals(candidate.Description, name, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => FromFile(candidate.Path))
            .OfType<LoadedTypeLibrary>()
            .ToList();

        if (named.Count == 0)
        {
            progress?.Invoke($"Looking among {candidates.Count} registered type libraries for '{name}'...");
            named = [.. candidates
                .Select(candidate => FromFile(candidate.Path))
                .OfType<LoadedTypeLibrary>()
                .Where(loaded => string.Equals(loaded.Name, name, StringComparison.OrdinalIgnoreCase))];
        }

        // several libraries of a machine can be of one name - the versions of ADO are registered apart - and the name is all that identifies a library to the
        // platform: the one that is described is the latest, however the machine happens to list them.
        return named.OrderByDescending(loaded => VersionOf(loaded.Library)).FirstOrDefault();
    }

    private static (int Major, int Minor) VersionOf(ITypeLib library)
    {
        library.GetLibAttr(out var pointer);
        try
        {
            var attributes = Marshal.PtrToStructure<TYPELIBATTR>(pointer);
            return (attributes.wMajorVerNum, attributes.wMinorVerNum);
        }
        finally
        {
            library.ReleaseTLibAttr(pointer);
        }
    }

    // the file of the latest version of a registered library.
    private static string? Registered(Guid id)
    {
        var (major, minor) = LatestVersion(id);
        return major < 0 ? null : PathOf(id, major, minor);
    }

    // the file a version of a library is registered in. A version is registered under each locale it is for, as HKCR\TypeLib\{id}\{major.minor}\{lcid}, and
    // asking for one locale finds no other: VBRUN is registered for English (9) and for no neutral locale (0). The neutral one is asked for first.
    private static string? PathOf(Guid id, int major, int minor)
    {
        foreach (var lcid in RegisteredLocales(id, major, minor))
        {
            if (QueryPathOfRegTypeLib(ref id, (ushort)major, (ushort)minor, lcid, out var path) >= 0 && !string.IsNullOrWhiteSpace(path))
            {
                return path.Trim('"');
            }
        }

        return null;
    }

    private static IEnumerable<int> RegisteredLocales(Guid id, int major, int minor)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);
        using var key = root.OpenSubKey($@"TypeLib\{id:B}\{major:x}.{minor:x}");
        return (key?.GetSubKeyNames() ?? [])
            .Select(name => int.TryParse(name, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lcid) ? lcid : -1)
            .Where(lcid => lcid >= 0)
            .Order()
            .ToArray();
    }

    private static (int Major, int Minor) LatestVersion(Guid id)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);
        using var key = root.OpenSubKey($@"TypeLib\{id:B}");
        var versions = (key?.GetSubKeyNames() ?? [])
            .Select(version => version.Split('.'))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
                && int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            .Select(parts => (Major: int.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture), Minor: int.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture)))
            .OrderByDescending(version => version.Major)
            .ThenByDescending(version => version.Minor)
            .ToArray();

        return versions.Length == 0 ? (-1, -1) : versions[0];
    }

    private static string VersionKey(Guid id)
    {
        var (major, minor) = LatestVersion(id);
        return $"{major:x}.{minor:x}";
    }
}
