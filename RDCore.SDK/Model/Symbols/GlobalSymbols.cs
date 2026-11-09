using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// The symbols that belong to no declaration.
/// </summary>
/// <remarks>
/// One member, and it is a sentinel: something a resolver or a caller can name when there is nothing
/// real to name.
/// <para>
/// This was a grab-bag of some 140 static symbols: a table of every operator symbol, named accessors
/// into that table, an <c>ExtensionSymbols</c> nest of per-type <c>Byte.Min</c>/<c>Long.Max</c>
/// constants, a <c>StaticSymbols</c> nest of <c>True</c>/<c>Nothing</c>/<c>vbNullString</c>, and an
/// <c>Initialize</c> that seeded an index with the operators. Exactly one of them was ever read by any
/// code, and only for its declared type (which the type itself already names), and nothing called
/// <c>Initialize</c> at all. Together they looked like a language model; measured, they were a list of
/// names nobody could resolve, because nothing ever put them in a scope.
/// </para>
/// <para>
/// The real thing they gestured at is <c>StdLibSymbolProvider</c>: a
/// symbol provider whose symbols reach the scope tree, so a name like <c>vbNullString</c> actually
/// binds. Anything from the old nests that is genuinely part of the language belongs there (declared
/// once, resolvable) rather than here as a static field.
/// </para>
/// </remarks>
public static class GlobalSymbols
{
    /// <summary>
    /// Represents a symbol that is unbound and unresolved.
    /// </summary>
    /// <remarks>
    /// The data type of this symbol is <see cref="VBUnknownType"/>.
    /// </remarks>
    public static readonly StaticSymbol UnresolvedSymbol =
        new(nameof(UnresolvedSymbol), SymbolKindExt.Ignored, VBUnknownType.TypeInfo);
}
