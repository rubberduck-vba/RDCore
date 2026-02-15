using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBLongPtrType : VBIntrinsicType<int>, INumericType
{
    private static readonly VBLongPtrType _type = new();

    public VBLongPtrType() : base(Tokens.LongPtr) { }
    public static VBLongPtrType TypeInfo => _type;

    public override VBTypedValue DefaultValue { get; } = VBLongPtrValue.Zero;
    public override VBType[] ConvertsSafelyToTypes { get; } = [VBLongType.TypeInfo, VBLongLongType.TypeInfo, VBVariantType.TypeInfo];
}
