using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types.Intrinsic;

public record class VBLongPtrType() : VBIntrinsicType<int>(Tokens.LongPtr)
{
    public static VBLongPtrType TypeInfo { get; } = new();

    private static readonly Lazy<VBLongPtrValue> _defaultValue = new(() => VBLongPtrValue.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override VBType[] ConvertsSafelyToTypes { get; } = [VBLongType.TypeInfo, VBLongLongType.TypeInfo, VBVariantType.TypeInfo];
}
