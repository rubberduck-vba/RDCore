using RDCore.Runtime.LanguageCore.StdLib.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Frozen;
using System.Reflection;

namespace RDCore.Runtime.LanguageCore.StdLib;

// the functions of the standard library evaluated inline (StdLibInlineAttribute), by the target each is declared as.
public static class StdLibInline
{
    private static readonly Lazy<FrozenSet<string>> Targets = new(static () => StdLibSymbolReader.Declarations
        .SelectMany(static type => type.GetMethods().Where(static method => method.GetCustomAttribute<StdLibInlineAttribute>() is not null)
            .Select(method => StdLibSymbolReader.ExternalTargetOf(type, method)))
        .ToFrozenSet(StringComparer.Ordinal));

    public static bool IsInline(Symbol procedure)
        => procedure.GetProperty(SymbolProperties.ExternalTarget) is { } target && Targets.Value.Contains(target);
}
