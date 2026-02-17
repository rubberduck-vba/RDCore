using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBBooleanType : VBIntrinsicType<bool>
{
    private static readonly VBBooleanType _type = new();

    private VBBooleanType() : base(Tokens.Boolean) { }
    public static VBBooleanType TypeInfo => _type;

    public override VBBooleanValue DefaultValue { get; } = new VBBooleanValue() { Value = false };
    public override VBType[] ConvertsSafelyToTypes =>
        [
            VBByteType.TypeInfo,
            VBIntegerType.TypeInfo,
            VBLongType.TypeInfo,
            VBLongLongType.TypeInfo,
            VBDecimalType.TypeInfo,
            VBCurrencyType.TypeInfo,
            VBSingleType.TypeInfo,
            VBDoubleType.TypeInfo,
            VBStringType.TypeInfo,
            VBVariantType.TypeInfo
        ];

    public override string? DefToken => Tokens.DefBool;
}
