using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBArrayType : VBIntrinsicType<object[]>, IEnumerableType
{
    private static readonly Lazy<VBArrayType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBArrayType TypeInfo => _instance.Value;

    public VBArrayType(VBArrayValue? declaredValue = null) : base("Array")
    {
        DeclaredValue = declaredValue ?? (VBResizableArrayValue)DefaultValue;
    }

    public VBArrayValue DeclaredValue { get; init; }

    public bool IsArray { get; } = true;

    public override VBTypedValue DefaultValue => DeclaredValue;

    public override bool CanPassByValue { get; } = false;

    public override VBType[] ConvertsSafelyToTypes => [VBVariantType.TypeInfo];
}
