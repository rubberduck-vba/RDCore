using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Types;

public record class VBVoidType() : VBType(typeof(void), "(void)", isHidden: true)
{
    private static readonly Lazy<VBVoidType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBType TypeInfo => _instance.Value;

    private readonly Lazy<VBVoidValue> _defaultValue = new(() => VBVoidValue.Void, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}