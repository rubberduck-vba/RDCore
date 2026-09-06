using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// A project-level conditional-compilation constant (<strong>MS-VBAL §3.4.1</strong>) — supplied by
/// the <c>.rdproj</c> or a <c>--define</c> argument, and shadowed by a module's own <c>#Const</c>.
/// </summary>
/// <remarks>
/// The declared type of the binding is <c>Variant</c>; <see cref="Value"/> is the concrete data value
/// of the constant expression.
/// </remarks>
/// <param name="Name">The constant's name.</param>
/// <param name="Value">The constant's data value.</param>
public sealed record class PrecompilerConstantSymbol(string Name, VBTypedValue Value)
    : UnboundTypedSymbol(StaticSymbol.GlobalUri, StaticSymbol.GlobalUri, Name, ScopeKind.Unallocated, SymbolKindExt.Constant, VBVariantType.TypeInfo);
