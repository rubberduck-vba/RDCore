using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace RDCore.SDK.ConsoleIO;

/// <summary>
/// How <see cref="SourcePathAnonymizer.Scrub"/> rewrites build-machine source-file paths it finds in
/// error detail before that text can cross a process boundary.
/// </summary>
public enum SourcePathScrubMode
{
    /// <summary>
    /// Rewrite each absolute path to a path relative to the RDCore repository root — e.g.
    /// <c>RDCore.Parsing/AST/DeclarationsParseTreeListener.cs:line 241</c>. Falls back to
    /// <see cref="FileName"/> for a path that cannot be anchored to the repo. This is the default.
    /// </summary>
    RepoRelative,

    /// <summary>
    /// Replace each absolute path with its file name only — e.g.
    /// <c>DeclarationsParseTreeListener.cs:line 241</c>.
    /// </summary>
    FileName,

    /// <summary>
    /// Replace each absolute path with the literal <c>&lt;source&gt;</c>, keeping only the line number —
    /// e.g. <c>&lt;source&gt;:line 241</c>.
    /// </summary>
    Redacted,
}

/// <summary>
/// Removes the build machine's absolute source-file paths from exception / stack-trace text.
/// </summary>
/// <remarks>
/// The dev platform is published as a <c>Debug</c> build with PDBs, so a caught exception's
/// <see cref="System.Exception.ToString"/> carries frames like
/// <c>… in C:\Users\somebody\src\RDCore\RDCore.Parsing\…\Foo.cs:line 12</c>. When that string is
/// packaged into a DTO and sent over JSON-RPC to another process (or on to an editor), it discloses
/// the build machine's directory layout and user name. This scrubs those paths at the DTO funnel;
/// the unredacted text still goes to the process log file.
/// <para>
/// A pure function with no state or DI — the only input beyond the text is the
/// <see cref="SourcePathScrubMode"/> a caller resolves from configuration.
/// </para>
/// </remarks>
public static partial class SourcePathAnonymizer
{
    // the .NET stack-frame format is invariant: " in <path>:line <n>". non-greedy up to ":line".
    [GeneratedRegex(@" in (?<path>.+?):line (?<line>\d+)")]
    private static partial Regex StackFramePath();

    // anchors the repo-relative rewrite on a foreign path: the RDCore project folder segment
    // (RDCore.Parsing, RDCore.SDK, …) that every RDCore source path contains exactly once near its front.
    [GeneratedRegex(@"[/\\](?<anchor>RDCore\.[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*)[/\\]")]
    private static partial Regex RepoAnchor();

    /// <summary>
    /// The repository root of the build that compiled this assembly, captured from
    /// <see cref="CallerFilePathAttribute"/>, with separators normalized to <c>/</c>; <c>null</c> when
    /// it cannot be derived (e.g. a trimmed build). This is exactly the prefix that appears on the
    /// other frames of a stack trace produced by the same build.
    /// </summary>
    private static readonly string? _buildRepoRoot = DeriveBuildRepoRoot();

    private static string? DeriveBuildRepoRoot([CallerFilePath] string thisFile = "")
    {
        const string knownSuffix = "RDCore.SDK/ConsoleIO/SourcePathAnonymizer.cs";
        var normalized = thisFile.Replace('\\', '/');
        var cut = normalized.LastIndexOf(knownSuffix, StringComparison.OrdinalIgnoreCase);
        return cut > 0 ? normalized[..cut].TrimEnd('/') : null;
    }

    /// <summary>
    /// Rewrites every build-machine source path in <paramref name="text"/> per <paramref name="mode"/>.
    /// Text with no recognizable path is returned unchanged.
    /// </summary>
    /// <param name="text">Exception or stack-trace text, typically <see cref="System.Exception.ToString"/>.</param>
    /// <param name="mode">How to rewrite each path.</param>
    public static string Scrub(string text, SourcePathScrubMode mode = SourcePathScrubMode.RepoRelative)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(":line ", StringComparison.Ordinal))
        {
            return text;
        }

        return StackFramePath().Replace(text, match =>
        {
            var path = match.Groups["path"].Value;
            var line = match.Groups["line"].Value;
            return $" in {Rewrite(path, mode)}:line {line}";
        });
    }

    private static string Rewrite(string path, SourcePathScrubMode mode) => mode switch
    {
        SourcePathScrubMode.Redacted => "<source>",
        SourcePathScrubMode.FileName => FileNameOf(path),
        _ => RepoRelative(path),
    };

    private static string FileNameOf(string path)
    {
        var slash = path.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static string RepoRelative(string path)
    {
        var normalized = path.Replace('\\', '/');

        if (_buildRepoRoot is { Length: > 0 }
            && normalized.StartsWith(_buildRepoRoot, StringComparison.OrdinalIgnoreCase))
        {
            return normalized[_buildRepoRoot.Length..].TrimStart('/');
        }

        // foreign build machine (e.g. CI): anchor on the first RDCore project folder in the path.
        var anchor = RepoAnchor().Match(normalized);
        if (anchor.Success)
        {
            return normalized[anchor.Groups["anchor"].Index..];
        }

        return FileNameOf(normalized);
    }
}
