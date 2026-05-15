using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBObjectType() : VBIntrinsicType<Guid>(Tokens.Object)
{
    private static readonly Lazy<VBObjectType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBObjectType TypeInfo => _instance.Value;

    private static readonly Lazy<VBObjectValue> _defaultValue = new(() => VBObjectValue.Nothing, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
    
    public override VBType[] ConvertsSafelyToTypes { get; } = [VBVariantType.TypeInfo];
    public override string? DefToken => Tokens.DefObj;

    public override bool RuntimeBinding { get; } = true;
}
