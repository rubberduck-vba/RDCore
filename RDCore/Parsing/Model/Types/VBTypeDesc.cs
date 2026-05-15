using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBTypeDesc : VBType
{
    private static readonly Lazy<VBTypeDesc> _instance = new(() => new(nameof(VBType)), LazyThreadSafetyMode.PublicationOnly);
    public static VBTypeDesc TypeInfo => _instance.Value;

    private static readonly Lazy<VBTypeDescValue> _defaultValue = new(() => new VBTypeDescValue(VBVariantType.TypeInfo), LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    private VBTypeDesc(string name) 
        : base(typeof(Type), name, isUserDefined: false, isHidden: true)
    {
    }
}
