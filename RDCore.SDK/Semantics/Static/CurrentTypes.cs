using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// The class a declaration is of, as the composition has it now.
/// </summary>
/// <remarks>
/// A declaration bound to a class of the workspace (<c>Dim o As Widget</c>) holds the class by its identity, and what the class declares - its members, its
/// default member (<strong>MS-VBAL §5.5.1.2.13</strong>) - is read from the class as it is when it is asked, not as it was when the declaration was bound: a
/// module listed before the class it uses is bound before the class is composed (HexIDE's findings desk #1). A class of a library is the library's from the
/// start, and so is a type that is more than a class (a <c>Collection</c>).
/// </remarks>
public static class CurrentTypes
{
    /// <summary>
    /// The class of <paramref name="declared"/> as the composition has it now; <paramref name="declared"/> itself when there is nothing to resolve it in, it is
    /// a library's, or the composition no longer has it.
    /// </summary>
    /// <param name="resolver">The resolver names are bound by, as the composition has it now; <see langword="null"/> when there is none.</param>
    /// <param name="declared">The class a declaration was bound to.</param>
    /// <returns>The class to read members and the default member of.</returns>
    public static VBClassType Of(ISymbolResolver? resolver, VBClassType declared)
        => resolver is not null
            && declared.GetType() == typeof(VBClassType)
            && string.IsNullOrEmpty(declared.Symbol.GetProperty(SymbolProperties.Library))
            && VBProjectSymbol.ResolveClass(resolver, declared.Symbol) is { } current
            && !ReferenceEquals(current, declared.Symbol)
                ? VBClassType.Of(current) with { IsHidden = declared.IsHidden }
                : declared;

    /// <summary>
    /// The class of <paramref name="declared"/> as the composition of <paramref name="context"/> has it now: see <see cref="Of(ISymbolResolver?, VBClassType)"/>.
    /// </summary>
    /// <param name="context">The compile-time context the class is resolved in.</param>
    /// <param name="declared">The class a declaration was bound to.</param>
    /// <returns>The class to read members and the default member of.</returns>
    public static VBClassType Of(StaticEvaluationContext context, VBClassType declared)
        => Of(context.Resolver, declared);
}
