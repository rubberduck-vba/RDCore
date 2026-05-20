using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A pseudo data type representing an unknown (unresolved) but presumably valid data type.
/// </summary>
public sealed record class VBUnknownType() : VBType(typeof(object), nameof(VBUnknownType))
{
    private static readonly Lazy<VBUnknownType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBType TypeInfo => _instance.Value;

    private readonly Lazy<VBTypedValue> _defaultValue = new(() => VBEmptyValue.Empty, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}
