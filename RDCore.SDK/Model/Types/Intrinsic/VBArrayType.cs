using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types.Intrinsic;

/// <summary>
/// Represents any type of array.
/// </summary>
public abstract record class VBArrayType : VBIntrinsicType<object[]>, IEnumerableType
{
    // FIXME: recursive references here
    private static readonly Lazy<VBArrayType> _instance = new(() => new VBResizableArrayType(VBResizableArrayValue.Empty), LazyThreadSafetyMode.PublicationOnly);
    public static VBArrayType TypeInfo => _instance.Value;

    public VBArrayType(VBArrayValue declaredValue) : base("Array")
    {
        DeclaredValue = declaredValue;
    }

    public VBArrayValue DeclaredValue { get; init; }

    public bool IsArray { get; } = true;

    public override VBTypedValue DefaultValue => DeclaredValue;

    public override bool CanPassByValue { get; } = false;

    public override VBType[] ConvertsSafelyToTypes => [VBVariantType.TypeInfo];
}

public sealed record class VBResizableArrayType(VBArrayValue DeclaredValue) : VBArrayType(DeclaredValue) { }
public sealed record class VBResizableByteArrayType() : VBArrayType(VBResizableByteArrayValue.Empty) { }