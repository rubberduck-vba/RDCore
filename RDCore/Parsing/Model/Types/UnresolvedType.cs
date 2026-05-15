using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class UnresolvedType() : VBType(typeof(object), nameof(UnresolvedType))
{
    private static readonly Lazy<UnresolvedType> _instance = new Lazy<UnresolvedType>(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBType TypeInfo => _instance.Value;

    private readonly Lazy<VBTypedValue> _defaultValue = new(() => VBVoidValue.Void, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}
