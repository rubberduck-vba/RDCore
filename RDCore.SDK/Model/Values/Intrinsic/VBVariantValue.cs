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
    /// The declared type of the variable this is the value of, when it was passed by reference to a <c>Variant</c> parameter of the standard library
    /// and is declared as another type - an array, a number - which MS-VBA tells apart from a <c>Variant</c> that holds the same value
    /// (<see cref="Runtime.Abstract.StdLib.StdLibByRefAttribute"/>); <see langword="null"/> otherwise.
    /// </summary>
    public Types.Abstract.VBType? ReferencedType { get; init; }

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

    /// <summary>
    /// Whether the subtype of this <c>Variant</c> is known: <see langword="false"/> for one an analysis knows nothing of but that it is a <c>Variant</c>.
    /// </summary>
    /// <remarks>
    /// A <c>Variant</c> whose value is not known still has a subtype that is, when what it holds was given a type - <c>v = x * 1.5</c> holds a
    /// <c>Double</c>, whatever <c>x</c> is - and the effective type of an operation of it is that subtype's. One whose subtype is not known holds an
    /// <c>Empty</c> that is not known either (<see cref="OfUnknownSubtype"/>): an <c>Empty</c> has one value, so an <c>Empty</c> that is not known can only be
    /// a subtype that is not. Nothing is planned on the <c>Empty</c> it assumes: an operation of it has no effective type that is known.
    /// </remarks>
    public bool IsSubtypeKnown => TypedValue switch
    {
        VBVariantValue inner => inner.IsSubtypeKnown,
        VBEmptyValue { IsIndeterminate: true } => false,
        _ => true,
    };

    /// <summary>
    /// A <c>Variant</c> that is not known, of a subtype that is not known either: what an analysis knows of a <c>Variant</c> nothing has said anything of.
    /// </summary>
    public static VBVariantValue OfUnknownSubtype() => (VBVariantValue)new VBVariantValue(VBEmptyValue.Empty.AsIndeterminate()).AsIndeterminate();

    /// <inheritdoc/>
    public override VBTypedValue AsAssumed() => TypedValue is VBEmptyValue ? OfUnknownSubtype() : AsIndeterminate();

    public bool Equals(IVBTypedValue<VBVariantValue, VBRuntimeVariantValue>? other) => Value == other?.Value;
}
