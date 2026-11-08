using RDCore.SDK.Model.Diagnostics;

namespace RDCore.SDK.Extensibility;

/// <summary>
/// Says the prefix of the codes of the diagnostics an extension reports (<see cref="RDCoreDiagnosticCode.Prefix"/>): three letters <c>A</c> to <c>Z</c>, as in
/// <c>XYZ00042</c> (<strong>RD-VBAL §1.1.4</strong>).
/// </summary>
/// <remarks>
/// What this says is written into the extension's manifest when it is described (<see cref="IExtensionsProvider.Describe"/>), as
/// <see cref="ExtensionInfo.DiagnosticPrefix"/>. The language server starts one extension of a prefix: one that declares a prefix another extension of the
/// platform already declares, or one of the language's own (<c>VBC</c>, <c>VBR</c>, <c>VBA</c>), is not started.
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
