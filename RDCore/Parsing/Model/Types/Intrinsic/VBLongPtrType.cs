using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;

namespace RDCore.Parsing.Model.Types.Intrinsic;

internal record class VBLongPtrType() : VBIntrinsicType<int>(Tokens.LongPtr)
{
    public static VBLongPtrType TypeInfo { get; } = new();

    private static readonly Lazy<VBLongPtrValue> _defaultValue = new(() => VBLongPtrValue.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override VBType[] ConvertsSafelyToTypes { get; } = [VBLongType.TypeInfo, VBLongLongType.TypeInfo, VBVariantType.TypeInfo];
}
