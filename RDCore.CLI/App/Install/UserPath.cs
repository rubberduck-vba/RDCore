using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RDCore.CLI.App.Install;

/// <summary>
/// The user's own <c>Path</c> on Windows: the one new processes of this user start with, beside the system's.
/// </summary>
internal interface IUserPathVariable
{
    /// <summary>
    /// The user's <c>Path</c> as it is stored, its <c>%VARIABLE%</c> references unexpanded; null when the user has none.
    /// </summary>
    string? Read();

    /// <summary>
    /// Stores <paramref name="value"/> as the user's <c>Path</c>, and tells running programs the environment changed.
    /// </summary>
    /// <param name="value">The <c>Path</c>, with its <c>%VARIABLE%</c> references unexpanded.</param>
    void Write(string value);
}

/// <summary>
/// The user's <c>Path</c> in the registry (<c>HKEY_CURRENT_USER\Environment</c>). It is read and written as stored, an expandable
/// string: <see cref="Environment.SetEnvironmentVariable(string, string?, EnvironmentVariableTarget)"/> would store it expanded, and the
/// user's own <c>%USERPROFILE%</c> references with it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsUserPathVariable : IUserPathVariable
{
    private const string EnvironmentKey = "Environment";
    private const string Name = "Path";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKey);
        return key?.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void Write(string value)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(EnvironmentKey))
        {
            key.SetValue(Name, value, RegistryValueKind.ExpandString);
        }

        // as SetEnvironmentVariable does: Explorer, and what it starts after, take the new Path.
        _ = SendMessageTimeout(HwndBroadcast, WmSettingChange, 0, EnvironmentKey, SmtoAbortIfHung, 5000, out _);
    }

    private static readonly nint HwndBroadcast = 0xFFFF;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(nint hWnd, uint msg, nuint wParam, string lParam, uint flags, uint timeout, out nuint result);
}

/// <summary>
/// What putting <c>rdc</c> on the user's PATH did.
/// </summary>
internal enum PathChange
{
    /// <summary>
    /// It was there already: nothing changed.
    /// </summary>
    Unchanged,

    /// <summary>
    /// It is there now.
    /// </summary>
    Changed,
}

/// <summary>
/// Puts the current platform's <c>rdc</c> on the user's PATH, replacing what an earlier <c>rdc install</c> put there for another version.
/// On Windows, the folder of <c>rdc.exe</c> in the user's <c>Path</c>; on Linux and macOS, a link <c>~/.local/bin/rdc</c> to <c>rdc</c>,
/// and <c>~/.local/bin</c> on PATH in the login profile when the shell does not put it there.
/// </summary>
/// <param name="root">The shared root the current platform is deployed in.</param>
/// <param name="home">The user's home folder.</param>
/// <param name="processPath">The PATH <c>rdc</c> runs with.</param>
/// <param name="windowsPath">The user's <c>Path</c> on Windows; null elsewhere.</param>
internal sealed class UserPath(SharedPlatformRoot root, string home, string? processPath, IUserPathVariable? windowsPath)
{
    internal const string ProfileBlockStart = "# >>> rdc install >>>";
    internal const string ProfileBlockEnd = "# <<< rdc install <<<";

    /// <summary>
    /// The link on Linux and macOS: <c>~/.local/bin/rdc</c>.
    /// </summary>
    public string Link => Path.Combine(LinkFolder, "rdc");

    private string LinkFolder => Path.Combine(home, ".local", "bin");

    /// <summary>
    /// The login profile a shell reads PATH from: <c>~/.zprofile</c> on macOS, whose shell is zsh, and <c>~/.profile</c> elsewhere.
    /// </summary>
    public string Profile => Path.Combine(home, OperatingSystem.IsMacOS() ? ".zprofile" : ".profile");

    /// <summary>
    /// Puts <paramref name="rdc"/> on the user's Windows <c>Path</c>.
    /// </summary>
    /// <param name="rdc">The current version's <c>rdc.exe</c>.</param>
    public PathChange OnWindowsPath(string rdc)
    {
        ArgumentNullException.ThrowIfNull(windowsPath);
        var folder = Path.GetDirectoryName(rdc)!;
        var stored = windowsPath.Read() ?? string.Empty;
        var entries = stored.Split(';');

        // an entry under the shared root's versions folder is one an rdc install put there, for this version or another.
        var others = entries.Where(entry => !IsDeployedVersionFolder(entry)).ToList();
        var ours = entries.Where(IsDeployedVersionFolder).ToList();
        if (ours.Count == 1 && SameFolder(ours[0], folder))
        {
            return PathChange.Unchanged;
        }

        while (others.Count > 0 && others[^1].Length == 0)
        {
            others.RemoveAt(others.Count - 1);
        }

        others.Add(folder);
        windowsPath.Write(string.Join(';', others));
        return PathChange.Changed;
    }

    /// <summary>
    /// Links <c>~/.local/bin/rdc</c> to <paramref name="rdc"/>, and adds <c>~/.local/bin</c> to the login profile when PATH does not have it:
    /// null when the link is a file of the user's, which is not replaced.
    /// </summary>
    /// <param name="rdc">The current version's <c>rdc</c>.</param>
    /// <param name="report">Told what changed, and what is as it was, one message each.</param>
    public PathChange? OnUnixPath(string rdc, ICollection<string> report)
    {
        var change = PathChange.Unchanged;
        var existing = new FileInfo(Link);
        if (existing.LinkTarget is { } target)
        {
            var resolved = Path.GetFullPath(target, LinkFolder);
            if (!string.Equals(resolved, rdc, StringComparison.Ordinal))
            {
                if (!IsUnder(resolved, root.Versions))
                {
                    return null;
                }

                existing.Delete();
                File.CreateSymbolicLink(Link, rdc);
                change = PathChange.Changed;
            }
        }
        else if (existing.Exists || Directory.Exists(Link))
        {
            return null;
        }
        else
        {
            Directory.CreateDirectory(LinkFolder);
            File.CreateSymbolicLink(Link, rdc);
            change = PathChange.Changed;
        }

        report.Add(string.Format(change == PathChange.Changed ? Resources.Install_Linked : Resources.Install_AlreadyLinked, Link, rdc));
        if (!OnProcessPath(LinkFolder))
        {
            var profile = File.Exists(Profile) ? File.ReadAllText(Profile) : string.Empty;
            if (profile.Contains(ProfileBlockStart, StringComparison.Ordinal))
            {
                report.Add(string.Format(Resources.Install_ProfileHasBlock, LinkFolder, Profile));
            }
            else
            {
                var separator = profile.Length == 0 || profile.EndsWith('\n') ? string.Empty : "\n";
                File.AppendAllText(Profile, separator + string.Join('\n',
                    ProfileBlockStart,
                    "# puts ~/.local/bin, where rdc install links rdc, on PATH",
                    "case \":$PATH:\" in *\":$HOME/.local/bin:\"*) ;; *) export PATH=\"$HOME/.local/bin:$PATH\" ;; esac",
                    ProfileBlockEnd) + "\n");
                report.Add(string.Format(Resources.Install_ProfileBlockAdded, LinkFolder, Profile));
                change = PathChange.Changed;
            }
        }

        return change;
    }

    private bool OnProcessPath(string folder)
        => (processPath ?? string.Empty).Split(Path.PathSeparator).Any(entry => entry.Length > 0 && SameFolder(entry, folder));

    private bool IsDeployedVersionFolder(string entry)
        => entry.Trim().Length > 0 && IsUnder(Expanded(entry), root.Versions);

    private static bool SameFolder(string entry, string folder)
        => string.Equals(Normalized(Expanded(entry)), Normalized(folder), SharedPlatformRoot.PathComparison);

    private static bool IsUnder(string path, string folder)
        => Normalized(path).StartsWith(Normalized(folder) + Path.DirectorySeparatorChar, SharedPlatformRoot.PathComparison);

    private static string Expanded(string entry) => Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));

    private static string Normalized(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
