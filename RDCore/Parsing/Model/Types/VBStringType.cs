using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBStringType : VBIntrinsicType<string?>
{
    private static readonly VBStringType _type = new();

    protected VBStringType() : base(Tokens.String) { }
    public static VBStringType TypeInfo => _type;

    public override VBTypedValue DefaultValue => VBStringValue.VBNullString;
    public override VBType[] ConvertsSafelyToTypes { get; } = [VBVariantType.TypeInfo];
}

internal record class VBFixedStringType : VBStringType
{
    public int Length { get; init; }
}