using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Meta;

/// <summary>
/// A meta-value that represents a <see cref="VBTypeMemberSymbol"/>.
/// </summary>
public record class VBMemberDescValue(Symbol Symbol, VBTypeMemberSymbol Member, params VBParameterDescValue[] Parameters)
    : VBTypedValue(Member.ResolvedType, Symbol)
{
    public override int Size => sizeof(int);
}
