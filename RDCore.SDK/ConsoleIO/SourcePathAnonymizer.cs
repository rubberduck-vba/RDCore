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
    /// This is the default.
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
/// A build that ships PDBs (the dev platform is <c>Debug</c>; a <c>Release</c> publish still emits
/// portable PDBs with absolute document paths) produces stack frames like
/// <c>… in /home/somebody/src/RDCore/RDCore.Parsing/…/Foo.cs:line 12</c>, disclosing the build
/// machine's directory layout and user name. This rewrites those frames at the DTO funnel; the
/// unredacted text is still written to the process log. It is a defensive net — the source fix is
/// <c>PathMap</c> / <c>ContinuousIntegrationBuild</c> at build time, so Roslyn never writes an
/// absolute document path.
/// <para>A pure function with no state, DI, or configuration.</para>
/// </remarks>
public static partial class SourcePathAnonymizer
{
    // the .NET stack-frame format is invariant: " in <path>:line <n>". non-greedy up to ":line".
    [GeneratedRegex(@" in (?<path>.+?):line (?<line>\d+)")]
    private static partial Regex StackFramePath();

    // anchors the rewrite on one of RDCore's own project folders as a full path segment — an outer
    // dir named "RDCore.something" the checkout sits under must not match.
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

        // take the last anchor — the project the file actually lives in, not an outer "RDCore.*" dir.
        Match? anchor = null;
        for (var match = RepoAnchor().Match(normalized); match.Success; match = match.NextMatch())
        {
            anchor = match;
        }

        return anchor is not null ? normalized[anchor.Groups["anchor"].Index..] : FileNameOf(normalized);
    }
}
