using System.Text.RegularExpressions;

namespace RDCore.SDK.Workspace;

/// <summary>
/// Reads RD-VBA-only <c>'@AnnotationName</c> comment annotations off a module's raw source: the
/// legacy-Rubberduck-style syntax the grammar already recognises (<c>annotationList</c> /
/// <c>annotation</c> / <c>annotationName</c>), read here as plain text rather than off the AST.
/// </summary>
/// <remarks>
/// <para>
/// The annotation is read from the source text: the parser builds no node for an annotation, which
/// can be written at the end of nearly any statement. To MS-VBA and any other VBA host the annotation
/// is a comment, so a workspace stays import- and export-compatible with the VBIDE.
/// </para>
/// <para>
/// The annotation vocabulary is open-ended: an extension can own annotations of its own, such as
/// <c>@TestMethod</c> for a unit-testing extension, without a change to the SDK. <c>Option Strict</c>
/// is owned by the core, and this type reads its annotation with a regular expression of its own.
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
    /// <c>Option Strict</c> is not an MS-VBAL directive: it is RD-VBA's own dial for turning select
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
