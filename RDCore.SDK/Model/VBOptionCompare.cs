namespace RDCore.SDK.Model;

/// <summary>
/// <c>Option Compare</c> can appear at the top of a module to implicitly specify how every string comparison should operate in that module.
/// </summary>
/// <remarks>
/// Some statements ignore this directive.
/// </remarks>
public enum VBOptionCompare
{
    /// <summary>
    /// <c>Option Compare Binary</c> (implicit default) makes string comparisons case-sensitive by comparing their byte values.
    /// </summary>
    Binary,
    /// <summary>
    /// <c>Option Compare Text</c> makes string comparisons case-insensitive and compares their text value accordingly with local regional settings.
    /// </summary>
    Text,
    /// <summary>
    /// <c>Option Compare Database</c> is defined in <em>Microsoft Access</em> and makes string comparisons <c>Binary</c> or <c>Text</c> dependent on host-defined settings.
    /// </summary>
    Database
}
