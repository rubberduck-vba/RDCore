using RDCore.Parsing.Model.Types.Abstract;

namespace RDCore.Parsing.Model.Types;

internal record class UnresolvedType : VBVariantType
{
    public static VBType VBType { get; } = new UnresolvedType();

    public UnresolvedType() : base(UnresolvedType.VBType) { }
}
