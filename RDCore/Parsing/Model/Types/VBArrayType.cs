using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBArrayType : VBIntrinsicType<object[]>, IEnumerableType
{
    private static readonly VBArrayType _type = new();

    public VBArrayType(VBArrayValue? declaredValue = null) : base("Array")
    {
        DeclaredValue = declaredValue ?? (VBResizableArrayValue)DefaultValue;
    }

    public static VBArrayType TypeInfo => _type;
    public VBArrayValue DeclaredValue { get; init; }

    public bool IsArray { get; } = true;

    public override VBTypedValue DefaultValue { get; } = new VBResizableArrayValue([]);
    public override bool CanPassByValue { get; } = false;

    public override VBType[] ConvertsSafelyToTypes => [VBVariantType.TypeInfo];
}
