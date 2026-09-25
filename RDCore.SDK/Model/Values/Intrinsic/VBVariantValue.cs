using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Variant</c> value.
/// </summary>
/// <remarks>
/// 👉 The <em>managed type</em> of this value is a <see cref="VBRuntimeVariantValue"/>, which boxes
/// <see cref="TypedValue"/> itself so a fresh Variant round-trips through <c>ISessionStorage</c> intact:
/// reading one back (<see cref="VBVariantType.CreateValue"/>) unboxes the very same wrapped value,
/// never a fresh, unrelated <c>Empty</c>.
/// </remarks>
public record class VBVariantValue : VBTypedValue, IVBTypedValue<VBVariantValue, VBRuntimeVariantValue>
{
    /// <summary>
    /// The wrapped typed value (may be another <c>Variant</c>).
    /// </summary>
    public VBTypedValue TypedValue { get; init; }

    /// <summary>
    /// Creates a Variant wrapping <paramref name="typedValue"/>, self-consistently bound: its own
    /// <see cref="VBTypedValue.Handle"/> boxes <paramref name="typedValue"/> directly, so reading this
    /// value straight back through <see cref="VBTypedValue.RuntimeValue"/> never throws.
    /// </summary>
    public VBVariantValue(VBTypedValue typedValue) : base(typedValue.TypeInfo)
    {
        TypedValue = typedValue;
        Handle = new ValueBindingHandle(new VBRuntimeVariantValue(typedValue.TypeInfo.VarType(), typedValue));
    }

    /// <summary>
    /// Creates a Variant wrapping <paramref name="typedValue"/> and bound to <paramref name="handle"/>
    /// directly — used when unboxing one already read back from storage.
    /// </summary>
    public VBVariantValue(IBindingHandle handle, VBTypedValue typedValue) : this(typedValue)
    {
        Handle = handle;
    }

    // computed, not independently settable: Handle is this value's own single source of truth (see the
    // class remarks) - a separate, directly-settable Value property could silently drift out of sync
    // with it, which is exactly the "poor handle handling" this whole type was rebuilt to eliminate.
    public VBRuntimeVariantValue Value => (VBRuntimeVariantValue)RuntimeValue;

    public override int Size => sizeof(long); // the size of VBVariantInteropValue.ValuePtr... probably not what MS-VBA would report

    public VBVariantValue WithValue(VBTypedValue value) => new(value) { TypeInfo = VBVariantType.TypeInfo with { SubType = value.TypeInfo } };

    public bool Equals(IVBTypedValue<VBVariantValue, VBRuntimeVariantValue>? other) => Value == other?.Value;
}
