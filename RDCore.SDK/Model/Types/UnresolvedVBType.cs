using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Types;

public sealed record class UnresolvedVBType() : VBType(typeof(object), nameof(UnresolvedVBType))
{
    private static readonly Lazy<UnresolvedVBType> _instance = new Lazy<UnresolvedVBType>(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBType TypeInfo => _instance.Value;

    private readonly Lazy<VBTypedValue> _defaultValue = new(() => VBVoidValue.Void, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}
