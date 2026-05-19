using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types.Intrinsic;

public record class VBVariantType : VBIntrinsicType<object?>
{
    private static readonly Lazy<VBVariantType> _instance = new Lazy<VBVariantType>(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBVariantType TypeInfo => _instance.Value;

    private static readonly Lazy<VBVariantValue> _defaultValue = new(() => new(VBEmptyValue.Empty), LazyThreadSafetyMode.PublicationOnly);
    public override VBVariantValue DefaultValue => _defaultValue.Value;

    protected VBVariantType(VBType? subtype = null) : base(Tokens.Variant)
    {
        Subtype = subtype ?? VBEmptyType.TypeInfo;
    }

    public VBType Subtype { get; init; }
    public bool IsEmpty => Subtype is VBEmptyType;

    public override VBType[] ConvertsSafelyToTypes { get; } = [];
    public override string? DefToken => Tokens.DefVar;

    public override bool RuntimeBinding { get; } = true;
}
