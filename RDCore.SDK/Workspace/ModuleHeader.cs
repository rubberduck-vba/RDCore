using System.Text.RegularExpressions;

namespace RDCore.SDK.Workspace;

/// <summary>
/// Reads what a VBA module's header says about the module itself.
/// </summary>
/// <remarks>
/// A class or designer module — a <c>.cls</c>, a <c>UserForm</c> — is exported with a <c>VERSION</c>
/// header line (<c>VERSION 1.0 CLASS</c> for a class, <c>VERSION 5.00</c> for a designer) followed by
/// a <c>BEGIN … END</c> attribute block. A standard module has no such header and opens directly on
/// its <c>Attribute VB_Name</c>. The <em>presence of the VERSION line</em> is the signal, independent
/// of the file extension: RD-VBA determines a module's kind from its source, not from <c>.cls</c> vs
/// <c>.bas</c>.
/// <para>
/// RD-VBA <c>.doccls</c> files are the VBIDE rendering of a document module rather than an importable
/// export, so they carry no header and are out of scope here.
/// </para>
/// </remarks>
public static partial class ModuleHeader
{
    /// <summary>
    /// Whether <paramref name="source"/> carries a module <c>VERSION</c> header — i.e. it is a class
    /// or designer module. <c>null</c> when there is no source to inspect.
    /// </summary>
    /// <param name="source">The raw module source, or <c>null</c> when it could not be read.</param>
    public static bool? IsClassModule(string? source)
        => string.IsNullOrEmpty(source) ? null : VersionHeader().IsMatch(source);

    // the `VERSION <n>[.<n>][ CLASS]` line VBA writes as the first physical line of a class or
    // designer module, on its own line. `\r?` so a CRLF ending does not defeat the end anchor; the
    // strict shape (digits and an optional CLASS keyword only) keeps a stray comment from matching.
    [GeneratedRegex(@"^[ \t]*VERSION[ \t]+[0-9]+(?:\.[0-9]+)?(?:[ \t]+CLASS)?[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionHeader();
}
