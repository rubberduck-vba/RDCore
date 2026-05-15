using RDCore.Parsing.Model.Values;
using RDCore.Runtime;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBBooleanType() : VBIntrinsicType<bool>(Tokens.Boolean)
{
    private static readonly Lazy<VBBooleanType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBBooleanType TypeInfo => _instance.Value;

    private readonly Lazy<VBTypedValue> _defaultValue = new(() => VBBooleanValue.False, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
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
