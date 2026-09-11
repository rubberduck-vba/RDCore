using System.Text.RegularExpressions;

namespace RDCore.SDK.Workspace;

/// <summary>
/// Reads RD-VBA-only <c>'@AnnotationName</c> comment annotations off a module's raw source — the
/// legacy-Rubberduck-style syntax the grammar already recognises (<c>annotationList</c> /
/// <c>annotation</c> / <c>annotationName</c>), read here as plain text rather than off the AST.
/// </summary>
/// <remarks>
/// This is a first cut: a source-text scan, not a parser-built <c>AnnotationTriviaNode</c> — the
/// grammar rule exists, but nothing yet builds or attaches the node it would produce, and a general
/// annotation-to-AST wiring is a bigger, cross-cutting change (annotations nest inside
/// <c>endOfStatement</c>, reachable from nearly every rule). A real, <em>supported</em> grammar-level
/// <c>Option Strict</c> token is future work; for now the annotation is a comment as far as MS-VBA (or
/// any real VBA host) is concerned — a workspace stays import/export-compatible with the VBIDE.
/// <para>
/// 📌 A static class hard-coding one annotation's name does not scale — the annotation vocabulary is
/// open-ended by design (an extension's shape is "annotation-semantics provider + analyzers + …", e.g.
/// the planned <c>RDCore.UnitTesting</c> extension owning <c>@TestMethod</c>, or the planned
/// <c>RDCore.DimensionalAnalysis</c> extension owning <c>@Unit</c>), and nothing here should force an
/// extension to modify the SDK to claim an annotation name. <c>Option Strict</c> is core-owned, not
/// extension-owned, so this type is fine as the one-off it is <em>today</em> — but once a real
/// annotation-semantics-provider mechanism exists, this should very likely become one of its
/// consumers instead of staying a bespoke regex. Noted, not a blocker:
/// <c>C:\Dev\Claude\rdcore-annotation-semantic-provider-ticket.md</c>.
/// </para>
/// </remarks>
public static partial class ModuleAnnotations
{
    /// <summary>
    /// Whether <paramref name="source"/> carries an <c>'@OptionStrict</c> annotation anywhere in the
    /// module. <c>false</c> when there is no source to inspect.
    /// </summary>
    /// <param name="source">The raw module source, or <c>null</c> when it could not be read.</param>
    /// <remarks>
    /// <c>Option Strict</c> is not an MS-VBAL directive — it is RD-VBA's own dial for turning select
    /// semantic flags that stay legal under plain <c>Option Explicit</c> into compile errors (see
    /// <c>VBCompileErrorId.ForbiddenWithOptionStrict</c>), the same role a real <c>Option Strict</c>
    /// statement line is reserved for (<c>ModuleOptions.OptionStrict</c>) once one ships.
    /// </remarks>
    public static bool HasOptionStrict(string? source)
        => !string.IsNullOrEmpty(source) && OptionStrictAnnotation().IsMatch(source);

    // any comment line carrying the @OptionStrict annotation — possibly alongside others on the same
    // '@... line (the grammar allows one leading ' with several @name groups). Not scoped to a single
    // bare annotation: matching the word anywhere after the comment marker is enough for a first cut.
    [GeneratedRegex(@"^[ \t]*'.*@OptionStrict\b", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OptionStrictAnnotation();
}
