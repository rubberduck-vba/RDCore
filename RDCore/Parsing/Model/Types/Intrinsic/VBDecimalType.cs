using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;

namespace RDCore.Parsing.Model.Types.Intrinsic;

internal sealed record class VBDecimalType() : VBIntrinsicType<decimal>(Tokens.Decimal), IFixedPointNumericType
{
    private static readonly Lazy<VBDecimalType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBDecimalType TypeInfo => _instance.Value;

    private static readonly Lazy<VBDecimalValue> _defaultValue = new(() => VBDecimalValue.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override bool IsDeclarable { get; } = false; // "As Decimal" is explicitly specified as illegal.
}
