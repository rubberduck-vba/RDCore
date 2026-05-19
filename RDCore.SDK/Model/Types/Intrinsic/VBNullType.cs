using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
namespace RDCore.SDK.Model.Types.Intrinsic;

public record class VBNullType() : VBIntrinsicType<object?>(Tokens.Null)
{
    private static readonly Lazy<VBNullType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBNullType TypeInfo => _instance.Value;

    private static readonly Lazy<VBNullValue> _defaultValue = new(() => VBNullValue.Null, LazyThreadSafetyMode.PublicationOnly);
    public override VBNullValue DefaultValue => _defaultValue.Value;

    public override VBType[] ConvertsSafelyToTypes { get; } = [VBVariantType.TypeInfo];
    public override bool IsDeclarable => false;
}
