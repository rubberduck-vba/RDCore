using System.Text.RegularExpressions;

namespace RDCore.SDK.ConsoleIO;

/// <summary>
/// How <see cref="SourcePathAnonymizer.Scrub"/> rewrites a source-file path it finds in error /
/// stack-trace text.
/// </summary>
public enum SourcePathScrubMode
{
    /// <summary>
    /// Rewrite each path to one relative to the RDCore repository root — e.g.
    /// <c>RDCore.Parsing/AST/DeclarationsParseTreeListener.cs:line 241</c>. Falls back to
    /// <see cref="FileName"/> for a path that cannot be anchored to an RDCore project folder.
    /// This is the default and the only mode anything wires up today.
    /// </summary>
    RepoRelative,

    /// <summary>
    /// Replace each path with its file name only — e.g.
    /// <c>DeclarationsParseTreeListener.cs:line 241</c>.
    /// </summary>
    FileName,

    /// <summary>
    /// Replace each path with the literal <c>&lt;source&gt;</c>, keeping only the line number — e.g.
    /// <c>&lt;source&gt;:line 241</c>.
    /// </summary>
    Redacted,
}

/// <summary>
/// Removes an absolute source-file path from exception / stack-trace text before that text can be
/// packaged into a DTO and sent to another process.
/// </summary>
/// <remarks>
/// When a build ships PDBs (the dev platform is <c>Debug</c>; a <c>Release</c> publish still emits
/// portable PDBs with absolute document paths), a caught exception's
/// <see cref="System.Exception.ToString"/> carries frames like
/// <c>… in /home/somebody/src/RDCore/RDCore.Parsing/…/Foo.cs:line 12</c>, disclosing the build
/// machine's directory layout and user name. This rewrites those frames at the DTO funnel; the
/// unredacted text is still written to the process log. This is a defensive net — the source fix is
/// to set <c>PathMap</c> / <c>ContinuousIntegrationBuild</c> at build time so Roslyn never writes an
/// absolute document path in the first place.
/// <para>A pure function with no state, DI, or configuration.</para>
/// </remarks>
public static partial class SourcePathAnonymizer
{
    // the .NET stack-frame format is invariant: " in <path>:line <n>". non-greedy up to ":line".
    [GeneratedRegex(@" in (?<path>.+?):line (?<line>\d+)")]
    private static partial Regex StackFramePath();

    // anchors the repo-relative rewrite on one of RDCore's own project folders, as a full path
    // segment. an ancestor or fork named RDCore.<x> that is not a real project does not match.
    [GeneratedRegex(@"[/\\](?<anchor>RDCore\.(?:SDK|Parsing|LanguageServer|CLI|Runtime|Diagnostics|Tests))[/\\]")]
    private static partial Regex RepoAnchor();

    /// <summary>
    /// Rewrites every source path in <paramref name="text"/> per <paramref name="mode"/>. Text with
    /// no recognizable stack frame is returned unchanged.
    /// </summary>
    /// <param name="text">Exception or stack-trace text, typically <see cref="System.Exception.ToString"/>.</param>
    /// <param name="mode">How to rewrite each path. Defaults to <see cref="SourcePathScrubMode.RepoRelative"/>.</param>
    public static string Scrub(string text, SourcePathScrubMode mode = SourcePathScrubMode.RepoRelative)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(":line ", StringComparison.Ordinal))
        {
            return text;
        }

        return StackFramePath().Replace(text, match =>
            $" in {Rewrite(match.Groups["path"].Value, mode)}:line {match.Groups["line"].Value}");
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

        // the last anchor is the project the file actually lives in (a checkout under a directory
        // that happens to contain "RDCore.fork/" then keeps only the inner "RDCore.Parsing/...").
        Match? anchor = null;
        for (var match = RepoAnchor().Match(normalized); match.Success; match = match.NextMatch())
        {
            anchor = match;
        }

        return anchor is not null ? normalized[anchor.Groups["anchor"].Index..] : FileNameOf(normalized);
    }
}
