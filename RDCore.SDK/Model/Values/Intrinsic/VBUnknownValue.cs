using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents the placeholder runtime value of an unresolved symbol.
/// </summary>
public sealed record class VBUnknownValue() : VBTypedValue(VBUnknownType.TypeInfo), IVBTypedValue<VBUnknownValue, object>
{
    // for construction uniformity; an unresolved symbol has no meaningful binding.
    public VBUnknownValue(IBindingHandle handle) : this() { Handle = handle; }

    private static readonly Lazy<VBUnknownValue> _defaultValue = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBUnknownValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int);
    public object Value => UnderlyingValue;

    public bool Equals(IVBTypedValue<VBUnknownValue, object>? other) => false;
}
