using System.Runtime.InteropServices;
using System.Text;

namespace RDCore.CLI.App.Build;

// the .NET application host the SDK makes an application's executable from, out of its packs
// (packs/Microsoft.NETCore.App.Host.<rid>/<version>/runtimes/<rid>/native/). Its 1024-byte slot begins with a placeholder, the hex SHA-256 of "foobar":
// the assembly's path, relative to the executable, is written over it in UTF-8, and the rest of the slot is zeroed.
internal static class AppHost
{
    internal static readonly byte[] Placeholder = Encoding.UTF8.GetBytes("c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2");

    internal const int SlotLength = 1024;

    public static string CurrentRuntime => RuntimeInformation.RuntimeIdentifier;

    public static string ExecutableName(string program, string runtime) => IsWindows(runtime) ? $"{program}.exe" : program;

    // the pack a host is out of, and its version, by the pack's layout; the platform's pack, of no known version, for a host found elsewhere.
    public static (string Pack, string Version) PackOf(string host, string runtime)
    {
        var version = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(host))));
        var pack = Path.GetFileName(Path.GetDirectoryName(version));
        return version is not null && pack is not null && pack.StartsWith(PackPrefix, StringComparison.Ordinal)
            ? (pack, Path.GetFileName(version))
            : ($"{PackPrefix}{runtime}", "-");
    }

    private const string PackPrefix = "Microsoft.NETCore.App.Host.";

    public static string? Find(string runtime, string? dotnetRoot = null)
    {
        var root = dotnetRoot ?? DotnetRoot();
        if (root is null)
        {
            return null;
        }

        var pack = Path.Combine(root, "packs", $"{PackPrefix}{runtime}");
        if (!Directory.Exists(pack))
        {
            return null;
        }

        var major = Environment.Version.Major;
        var versions = Directory.GetDirectories(pack)
            .Select(directory => (Directory: directory, Version: Version.TryParse(Path.GetFileName(directory).Split('-')[0], out var version) ? version : null))
            .Where(candidate => candidate.Version?.Major == major)
            .OrderByDescending(candidate => candidate.Version);
        foreach (var (directory, _) in versions)
        {
            var host = Path.Combine(directory, "runtimes", runtime, "native", IsWindows(runtime) ? "apphost.exe" : "apphost");
            if (File.Exists(host))
            {
                return host;
            }
        }

        return null;
    }

    public static byte[] Patch(ReadOnlySpan<byte> host, string assembly)
    {
        var path = Encoding.UTF8.GetBytes(assembly);
        if (path.Length == 0 || path.Length > SlotLength)
        {
            throw new ArgumentException($"the path of the assembly is longer than {SlotLength} bytes", nameof(assembly));
        }

        var at = host.IndexOf(Placeholder);
        if (at < 0 || host[(at + Placeholder.Length)..].IndexOf(Placeholder) >= 0)
        {
            throw new InvalidDataException("the application host has no placeholder for the assembly's path, or more than one");
        }

        var patched = host.ToArray();
        var slot = patched.AsSpan(at, Math.Max(Placeholder.Length, path.Length));
        slot.Clear();
        path.CopyTo(slot);
        return patched;
    }

    private static bool IsWindows(string runtime) => runtime.StartsWith("win", StringComparison.OrdinalIgnoreCase);

    // the root of the installation rdc runs on: DOTNET_ROOT when it is set, otherwise three folders above the shared framework rdc runs on
    // (<root>/shared/Microsoft.NETCore.App/<version>/).
    private static string? DotnetRoot()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } set && Directory.Exists(set))
        {
            return set;
        }

        var framework = RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Directory.GetParent(framework)?.Parent?.Parent?.FullName;
    }
}
