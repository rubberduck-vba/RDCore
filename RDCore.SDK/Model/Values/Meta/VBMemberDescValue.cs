using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Meta;

/// <summary>
/// A meta-value that represents a <c>VBTypeMemberSymbol</c> that is used in a member access expression.
/// </summary>
public record class VBMemberDescValue(Symbol Symbol, VBTypeMemberSymbol Member) : VBTypedValue(Member.ResolvedType, Symbol)
{
    public override int Size => sizeof(int);
}