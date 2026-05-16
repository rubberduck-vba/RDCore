using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;
using RDCore.Parsing.Model.Values.Abstract;

namespace RDCore.Parsing.Model.Types;

internal sealed record class UnresolvedVBType() : VBType(typeof(object), nameof(UnresolvedVBType))
{
    private static readonly Lazy<UnresolvedVBType> _instance = new Lazy<UnresolvedVBType>(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBType TypeInfo => _instance.Value;

    private readonly Lazy<VBTypedValue> _defaultValue = new(() => VBVoidValue.Void, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}
