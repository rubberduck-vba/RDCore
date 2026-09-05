using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Null</c> (<c>VBNullType</c>) literal value.
/// </summary>
public sealed record class VBNullValue()
    : VBTypedValue(VBNullType.TypeInfo), IVBTypedValue<VBNullValue, int>
{
    // for construction uniformity; Null carries no runtime value, so the handle is inert.
    public VBNullValue(IBindingHandle handle) : this() { Handle = handle; }

    private static readonly Lazy<VBNullValue> _instance = new(() => new());
    public static VBNullValue Null => _instance.Value;

    public int Value { get; } = 0;
    public override int Size => 0;

    public bool Equals(IVBTypedValue<VBNullValue, int>? other) => Value == other?.Value;
}
