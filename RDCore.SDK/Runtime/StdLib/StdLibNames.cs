namespace RDCore.SDK.Runtime.StdLib;

/// <summary>
/// The conventional VBA name of a standard-library declaration, so that only the declarations
/// convention does not reach have to state one.
/// </summary>
/// <remarks>
/// Every one of these is overridable per declaration — the <c>$</c>-suffixed function pairs and
/// <c>FormShowConstants</c> are names no convention recovers. The convention exists so that the
/// hundred-odd declarations that are perfectly regular stay free of ceremony, not to be clever about
/// the exceptions.
/// </remarks>
internal static class StdLibNames
{
    private const string InterfacePrefix = "IStd";
    private const string EnumPrefix = "VB";

    /// <summary><c>IStdInformationModule</c> → <c>Information</c>.</summary>
    internal static string ModuleName(string declarationName) => Trim(declarationName, "Module");

    /// <summary><c>IStdCollectionClass</c> → <c>Collection</c>.</summary>
    internal static string ClassName(string declarationName) => Trim(declarationName, "Class");

    /// <summary><c>VBDayOfWeek</c> → <c>VbDayOfWeek</c>.</summary>
    internal static string EnumName(string declarationName)
        => declarationName.StartsWith(EnumPrefix, StringComparison.Ordinal)
            ? string.Concat("Vb", declarationName.AsSpan(EnumPrefix.Length))
            : declarationName;

    /// <summary><c>VBSunday</c> → <c>vbSunday</c>.</summary>
    internal static string ConstantName(string fieldName)
        => fieldName.StartsWith(EnumPrefix, StringComparison.Ordinal)
            ? string.Concat("vb", fieldName.AsSpan(EnumPrefix.Length))
            : fieldName;

    /// <summary>
    /// <c>errorNumber</c> → <c>ErrorNumber</c>, and <c>@string</c> → <c>String</c>: a VBA parameter
    /// name is Pascal-cased, and is what a named argument at a call site has to spell.
    /// </summary>
    internal static string ParameterName(string? parameterName)
    {
        var name = (parameterName ?? string.Empty).TrimStart('@');
        return name.Length == 0 ? name : string.Concat(char.ToUpperInvariant(name[0]).ToString(), name.AsSpan(1));
    }

    private static string Trim(string declarationName, string suffix)
    {
        var name = declarationName.StartsWith(InterfacePrefix, StringComparison.Ordinal)
            ? declarationName[InterfacePrefix.Length..]
            : declarationName;

        return name.EndsWith(suffix, StringComparison.Ordinal) ? name[..^suffix.Length] : name;
    }
}
