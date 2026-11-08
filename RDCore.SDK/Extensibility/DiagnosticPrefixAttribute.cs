using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.SDK.Extensibility;

/// <summary>
/// Says the prefix of the codes of the diagnostics an extension reports (<see cref="RDCoreDiagnosticCode.Prefix"/>): three letters <c>A</c> to <c>Z</c>, as in
/// <c>XYZ00042</c> (<strong>RD-VBAL §1.1.4</strong>).
/// </summary>
/// <remarks>
/// What this says is written into the extension's manifest when it is described (<see cref="IExtensionsProvider.Describe"/>), as
/// <see cref="ExtensionInfo.DiagnosticPrefix"/>. The language server starts an extension only when its prefix is its own (<see cref="IExtensionsProvider.Discover"/>):
/// the first-party prefixes (<c>RDC</c>, <c>RDX</c>) are the core extension's, the language's (<c>VBC</c>, <c>VBR</c>, <c>VBA</c>) are no extension's, and two
/// extensions that declare the same prefix are neither started.
/// <para>
/// A finding the extension reports under another prefix is dropped, and one under its own prefix is reported under the extension's name.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class DiagnosticPrefixAttribute : Attribute
{
    /// <summary>
    /// Declares the prefix of the codes of the extension's diagnostics.
    /// </summary>
    /// <param name="prefix">Three letters, <c>A</c> to <c>Z</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> is not three letters <c>A</c> to <c>Z</c>.</exception>
    public DiagnosticPrefixAttribute(string prefix)
    {
        if (!RDCoreDiagnosticCode.IsPrefix(prefix))
        {
            throw new ArgumentException($"A diagnostic code prefix is three letters A to Z: '{prefix}' is not.", nameof(prefix));
        }

        Prefix = prefix;
    }

    /// <summary>
    /// The prefix of the codes of the extension's diagnostics.
    /// </summary>
    public string Prefix { get; }
}
