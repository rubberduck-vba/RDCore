using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBAnyType() : VBIntrinsicType<object?>(Tokens.Any)
{
    private static readonly Lazy<VBAnyType> _instance = new Lazy<VBAnyType>(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBAnyType TypeInfo => _instance.Value;

    private readonly Lazy<VBTypedValue> _defaultValue = new(() => VBVariantType.TypeInfo.DefaultValue, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override VBType[] ConvertsSafelyToTypes { get; } = [];
    public override bool IsDeclarable => false;
    public override bool RuntimeBinding { get; } = true;
}
