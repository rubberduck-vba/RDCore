using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents an <c>Empty</c> value.
/// </summary>
public sealed record class VBEmptyValue() : VBTypedValue(VBEmptyType.TypeInfo),
    IVBTypedValue<VBEmptyValue, int>
{
    // for construction uniformity; Empty carries no runtime value, so the handle is inert.
    public VBEmptyValue(IBindingHandle handle) : this() { Handle = handle; }

    private static readonly Lazy<VBEmptyValue> _emptyValue = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBEmptyValue Empty { get; } = _emptyValue.Value;

    public int Value => 0;
    public override int Size => sizeof(int);

    public bool Equals(IVBTypedValue<VBEmptyValue, int>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
