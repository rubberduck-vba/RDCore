using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents an <c>Empty</c> value.
/// </summary>
/// <remarks>
/// 👉 Under the semantic value, the runtime one: a <see cref="VBRuntimeEmptyValue"/> (<c>VT_EMPTY</c>).
/// </remarks>
public sealed record class VBEmptyValue : VBTypedValue,
    IVBTypedValue<VBEmptyValue, int>
{
    public VBEmptyValue() : base(VBEmptyType.TypeInfo)
    {
        Handle = new ValueBindingHandle(new VBRuntimeEmptyValue());
    }

    public VBEmptyValue(IBindingHandle handle) : this() { Handle = handle; }

    private static readonly Lazy<VBEmptyValue> _emptyValue = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBEmptyValue Empty { get; } = _emptyValue.Value;

    public int Value => 0;
    public override int Size => sizeof(int);

    public bool Equals(IVBTypedValue<VBEmptyValue, int>? other) => Value == other?.Value;
}
