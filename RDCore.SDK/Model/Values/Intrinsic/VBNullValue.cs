using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Null</c> (<c>VBNullType</c>) literal value.
/// </summary>
/// <remarks>
/// 👉 Under the semantic value, the runtime one: a <see cref="VBRuntimeNullValue"/> (<c>VT_NULL</c>).
/// </remarks>
public sealed record class VBNullValue
    : VBTypedValue, IVBTypedValue<VBNullValue, int>
{
    public VBNullValue() : base(VBNullType.TypeInfo)
    {
        Handle = new ValueBindingHandle(new VBRuntimeNullValue());
    }

    public VBNullValue(IBindingHandle handle) : this() { Handle = handle; }

    private static readonly Lazy<VBNullValue> _instance = new(() => new());
    public static VBNullValue Null => _instance.Value;

    public int Value { get; } = 0;
    public override int Size => 0;

    public bool Equals(IVBTypedValue<VBNullValue, int>? other) => Value == other?.Value;
}
