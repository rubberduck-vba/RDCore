using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBVoidType() : VBType(typeof(void), "(void)", isHidden: true)
{
    private static readonly Lazy<VBVoidType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBType TypeInfo => _instance.Value;

    private readonly Lazy<VBVoidValue> _defaultValue = new(() => VBVoidValue.Void, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}