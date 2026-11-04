using System.Text.RegularExpressions;

namespace RDCore.SDK.Workspace;

/// <summary>
/// Resolves a VBA module's programmatic name.
/// </summary>
/// <remarks>
/// A module is named by its module-level <c>Attribute VB_Name = "…"</c> directive. The source file
/// name is only a fallback, for source that declares no such attribute (a hand-authored fragment, a
/// module that was never round-tripped through the VBE). Exported VBA always emits the attribute as
/// the first physical line of the file.
/// <para>
/// The language server, which parses every module, resolves the declared name from the AST
/// (<c>ModuleNodeExtensions.GetDeclaredName</c>); components without a parser (the environment host,
/// the project-file dedup check) scan the raw source through <see cref="FromSource"/>. Both paths
/// then apply the same <see cref="FromFileName"/> fallback, so the module symbol the host composes
/// and the member symbols the language server defines against it line up on one name.
/// </para>
/// </remarks>
public static partial class ModuleName
{
    /// <summary>
    /// The module name declared by <paramref name="source"/>'s module-level <c>Attribute VB_Name</c>,
    /// or the <paramref name="relativeUri"/> file name (without its extension) when the source
    /// declares none.
    /// </summary>
    /// <param name="source">The raw module source, or <c>null</c> when it could not be read.</param>
    /// <param name="relativeUri">The module's workspace-relative path, used for the fallback.</param>
    public static string Resolve(string? source, string relativeUri)
        => FromSource(source) ?? FromFileName(relativeUri);

    /// <summary>
    /// The file name of <paramref name="relativeUri"/>, without its directory part or extension.
    /// </summary>
    /// <remarks>
    /// Platform-agnostic: both <c>/</c> and <c>\</c> separate segments regardless of the host OS, so
    /// a <c>.rdproj</c> authored on Windows resolves the same way on the Linux CI.
    /// </remarks>
    public static string FromFileName(string relativeUri)
    {
        var normalized = relativeUri.Replace('\\', '/');
        var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        var lastDot = fileName.LastIndexOf('.');
        return lastDot > 0 ? fileName[..lastDot] : fileName;
    }

    /// <summary>
    /// The value of <paramref name="source"/>'s module-level <c>Attribute VB_Name</c> directive, or
    /// <c>null</c> when the source declares none.
    /// </summary>
    /// <remarks>
    /// A member-qualified attribute (<c>Attribute Foo.VB_Name</c>) is not a module name and is
    /// ignored. The value is a string literal, so surrounding quotes are stripped and a doubled
    /// <c>""</c> escape is unescaped.
    /// </remarks>
    public static string? FromSource(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        var match = VBNameAttribute().Match(source);
        return match.Success ? match.Groups["name"].Value.Replace("\"\"", "\"") : null;
    }

    // module-level `Attribute VB_Name = "Name"` on its own physical line (\r? so a CRLF line ending
    // does not defeat the end-of-line anchor). the value group admits a doubled-quote escape; a
    // leading member qualifier (`Foo.VB_Name`) is excluded by the [ \t]+ after VB_Name.
    [GeneratedRegex(@"^[ \t]*Attribute[ \t]+VB_Name[ \t]*=[ \t]*""(?<name>(?:[^""]|"""")*)""[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VBNameAttribute();
}
