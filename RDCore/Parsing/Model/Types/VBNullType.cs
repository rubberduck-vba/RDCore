using RDCore.Parsing.Model.Values;
namespace RDCore.Parsing.Model.Types;

internal record class VBNullType() : VBIntrinsicType<object?>(Tokens.Null)
{
    private static readonly Lazy<VBNullType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBNullType TypeInfo => _instance.Value;

    private static readonly Lazy<VBNullValue> _defaultValue = new(() => VBNullValue.Null, LazyThreadSafetyMode.PublicationOnly);
    public override VBNullValue DefaultValue => _defaultValue.Value;

    public override VBType[] ConvertsSafelyToTypes { get; } = [VBVariantType.TypeInfo];
    public override bool IsDeclarable => false;
}
