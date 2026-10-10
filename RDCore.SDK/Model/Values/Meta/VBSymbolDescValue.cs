using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Meta;

/// <summary>
/// A meta-value that represents a resolved <see cref="Symbol"/> reference — unlike
/// <see cref="VBMemberDescValue"/> and <see cref="VBParameterDescValue"/>, not narrowed to a
/// particular symbol hierarchy, for an operation (assignment resolving its <c>lExpression</c> target,
/// chiefly) that needs to carry any kind of resolved symbol through the same
/// <see cref="VBTypedValue"/>-based operand pipeline an ordinary value flows through.
/// </summary>
/// <param name="Symbol">The described symbol.</param>
/// <param name="DeclaredType">
/// The type the symbol is assigned as, when it is not its own declared type: a <c>Variant</c> reference parameter bound to a variable of another type is
/// assigned as the variable is (<strong>MS-VBAL §5.3.1.11</strong>). <see langword="null"/> for the symbol's own.
/// </param>
public record class VBSymbolDescValue(Symbol Symbol, VBType? DeclaredType = null)
    : VBTypedValue(DeclaredType ?? (Symbol is ITypedSymbol typed ? typed.ResolvedType : VBUnknownType.TypeInfo))
{
    public override int Size => sizeof(int);
}
