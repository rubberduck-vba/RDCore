using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Variant</c> value.
/// </summary>
/// <remarks>
/// 👉 The <em>managed type</em> of this value is a <see cref="VBRuntimeVariantValue"/>
/// </remarks>
/// <param name="TypedValue">The wrapped typed value (may be another <c>Variant</c>).</param>
public record class VBVariantValue(VBTypedValue TypedValue)
    : VBTypedValue(TypedValue.TypeInfo), IVBTypedValue<VBVariantValue, VBRuntimeVariantValue>
{
    /// <summary>
    /// Creates a Variant wrapping <paramref name="typedValue"/> and bound to <paramref name="handle"/>.
    /// The <see cref="Value"/> / subtype plumbing is unchanged (see the complex-value follow-up).
    /// </summary>
    public VBVariantValue(IBindingHandle handle, VBTypedValue typedValue) : this(typedValue)
    {
        Handle = handle;
    }

    public VBRuntimeVariantValue Value { get; init; } = new(VBVariantValueType.Empty, new ValueBindingHandle(VBRuntimeValue<int>.Int32ZeroValue));

    public override int Size => sizeof(long); // the size of VBVariantInteropValue.ValuePtr... probably not what MS-VBA would report

    public VBVariantValue WithValue(VBTypedValue value)
    {
        return this with
        {
            TypedValue = value,
            Value = new VBRuntimeVariantValue(VBVariantValueType.Dispatch, Value.Handle),
            TypeInfo = VBVariantType.TypeInfo with { SubType = value.TypeInfo }
        };
    }

    public bool Equals(IVBTypedValue<VBVariantValue, VBRuntimeVariantValue>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
