using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBLongPtrType() : VBIntrinsicType<int>(Tokens.LongPtr)
{
    public static VBLongPtrType TypeInfo { get; } = new();

    private static readonly Lazy<VBLongPtrValue> _defaultValue = new(() => VBLongPtrValue.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override VBType[] ConvertsSafelyToTypes { get; } = [VBLongType.TypeInfo, VBLongLongType.TypeInfo, VBVariantType.TypeInfo];
}
