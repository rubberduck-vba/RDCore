using System.Runtime.InteropServices;
using System.Text;

namespace RDCore.CLI.App.Build;

/// <summary>
/// The native executable that starts a built program: the .NET application host of the target platform, told the name of the program's assembly.
/// </summary>
/// <remarks>
/// The host is the one the .NET SDK makes an executable of an application with, from its packs
/// (<c>packs/Microsoft.NETCore.App.Host.&lt;rid&gt;/&lt;version&gt;/runtimes/&lt;rid&gt;/native/</c>). It holds a slot of 1024 bytes that begins with a
/// placeholder - the SHA-256 of <c>foobar</c>, in hexadecimal - where the path of the application's assembly, relative to the executable, is written
/// over it in UTF-8 and the rest of the placeholder set to zeros.
/// </remarks>
internal static class AppHost
{
    /// <summary>The placeholder the path of the application's assembly is written over.</summary>
    internal static readonly byte[] Placeholder = Encoding.UTF8.GetBytes("c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2");

    /// <summary>How long the path written over the placeholder can be, in UTF-8 bytes: the size of the slot.</summary>
    internal const int SlotLength = 1024;

    /// <summary>The runtime identifier of the platform rdc runs on: what a program is built for unless said otherwise.</summary>
    public static string CurrentRuntime => RuntimeInformation.RuntimeIdentifier;

    /// <summary>The name of the executable of a program, on the platform of <paramref name="runtime"/>.</summary>
    public static string ExecutableName(string program, string runtime) => IsWindows(runtime) ? $"{program}.exe" : program;

    /// <summary>
    /// The application host for <paramref name="runtime"/>, in the packs of the .NET installation rdc runs on: of the newest version of the major version
    /// rdc runs on. <see langword="null"/> when there is none.
    /// </summary>
    /// <param name="runtime">The runtime identifier, <c>win-x64</c>, <c>linux-x64</c>.</param>
    /// <param name="dotnetRoot">The root of the .NET installation; the one rdc runs on when omitted.</param>
    public static string? Find(string runtime, string? dotnetRoot = null)
    {
        var root = dotnetRoot ?? DotnetRoot();
        if (root is null)
        {
            return null;
        }

        var pack = Path.Combine(root, "packs", $"Microsoft.NETCore.App.Host.{runtime}");
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

    /// <summary>
    /// The application host <paramref name="host"/> made the executable of the assembly <paramref name="assembly"/>: the path written over the placeholder.
    /// </summary>
    /// <param name="host">The content of the application host.</param>
    /// <param name="assembly">The path of the program's assembly, relative to the executable: its file name, when they are side by side.</param>
    /// <returns>The content of the executable.</returns>
    /// <exception cref="ArgumentException">The path is longer than the slot.</exception>
    /// <exception cref="InvalidDataException">The host has no placeholder, or more than one.</exception>
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
