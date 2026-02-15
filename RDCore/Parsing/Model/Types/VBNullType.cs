using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBNullType : VBIntrinsicType<object?>
{
    private static readonly VBNullType _type = new();

    public VBNullType() : base(Tokens.Null) { }

    public static VBNullType TypeInfo => _type;
    public override bool IsDeclarable => false;

    public override VBNullValue DefaultValue { get; } = VBNullValue.Null;
    public override VBType[] ConvertsSafelyToTypes { get; } = [VBVariantType.TypeInfo];
}
