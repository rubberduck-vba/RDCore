using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Meta;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Meta;
using System.Text;
using System.Text.Json.Serialization;
namespace RDCore.SDK.Model.Values.Abstract;

/// <summary>
/// Represents any run-time typed value that can be represented with a managed (.net) value.
/// </summary>
/// <remarks>
/// Mandates an implementation of <c>IEquatable&lt;T&gt;</c> for the specified <c>VBTypedValue</c>
/// </remarks>
/// <typeparam name="VBTValue">The <c>VBType</c> type of the value.</typeparam>
/// <typeparam name="TValue">The underlying managed type of the value.</typeparam>
public interface IVBTypedValue<VBTValue, TValue> : IEquatable<IVBTypedValue<VBTValue, TValue>>
    where VBTValue : VBTypedValue
{
    /// <summary>
    /// Gets the underlying managed value corresponding to this typed value.
    /// </summary>
    TValue Value { get; }
}

/// <summary>
/// Represents any typed value.
/// </summary>
/// <remarks>
/// This class is at the base of the type hierarchy for all typed values.
/// </remarks>
/// <param name="TypeInfo">The <c>VBType</c> of the value.</param>
[JsonConverter(typeof(VBTypedValueJsonConverter))]
public abstract record class VBTypedValue(VBType TypeInfo)
{
    protected VBTypedValue(VBType typeInfo, VBRuntimeReference reference) : this(typeInfo)
    {
        Handle = new ReferenceBindingHandle(reference);
    }
    protected VBTypedValue(VBType typeInfo, IRuntimeValue value) : this(typeInfo)
    {
        Handle = new ValueBindingHandle(value);
    }


    /// <summary>
    /// Gets the described <c>Target</c> type of this value if the value is a <see cref="VBTypeDescValue"/>; yields the <c>TypeInfo</c> of this value otherwise.
    /// </summary>
    /// <remarks>
    /// 👉 The <c>TypeInfo</c> of a <em>type descriptor value</em> is a <see cref="VBTypeDesc"/>.
    /// </remarks>
    public VBType GetTargetType() => this is VBTypeDescValue desc ? desc.Target : this.TypeInfo;
    /// <summary>
    /// Creates a new <see cref="VBTypeDescValue"/> that describes this value.
    /// </summary>
    public VBTypeDescValue Describe() => new VBTypeDescValue(TypeInfo);

    /// <summary>
    /// The allocated size (in bytes) of this value.
    /// </summary>
    public abstract int Size { get; }

    /// <summary>
    /// The runtime value this typed value is currently bound to.
    /// </summary>
    public IRuntimeValue RuntimeValue => Handle.Value;

    public IBindingHandle Handle { get; init; } = InvalidBindingHandle.Default;

    /// <summary>
    /// Returns a copy of this value bound to <paramref name="runtimeValue"/>.
    /// </summary>
    public VBTypedValue WithRuntimeValue(IRuntimeValue runtimeValue)
        => this with { Handle = new ValueBindingHandle(runtimeValue) };

    /// <summary>
    /// Whether the value is not known: it is bound to an <see cref="IndeterminateBindingHandle"/>, or to a cell that does not know what it holds
    /// (<see cref="IKnowledgeBinding.IsKnown"/>), and the value it yields is assumed.
    /// </summary>
    /// <remarks>
    /// An analysis evaluates code without running it, so a value that is not a constant is not known: it is represented by an indeterminate value of
    /// its declared type (<see cref="VBType.CreateIndeterminateValue"/>). Whatever is derived from an indeterminate value is indeterminate, and
    /// nothing that is raised because of the value it assumes is known to happen.
    /// </remarks>
    public bool IsIndeterminate => Handle is IndeterminateBindingHandle or IKnowledgeBinding { IsKnown: false };

    /// <summary>
    /// Returns a copy of this value whose value is not known, and which assumes the value this one holds.
    /// </summary>
    public VBTypedValue AsIndeterminate()
        => IsIndeterminate ? this : this with { Handle = new IndeterminateBindingHandle(Handle) };

    /// <summary>
    /// Returns what an analysis assumes of this value where it does not trust it - a variable it reads, a value nothing says anything of but its declared
    /// type: a copy whose value is not known (<see cref="AsIndeterminate"/>).
    /// </summary>
    /// <remarks>
    /// 👉 A <c>Variant</c> that holds <c>Empty</c>, the value every <c>Variant</c> starts as, says nothing of the value it will hold: its subtype is not known
    /// either (<see cref="Intrinsic.VBVariantValue.IsSubtypeKnown"/>). One that holds a value of another subtype keeps it.
    /// </remarks>
    public virtual VBTypedValue AsAssumed() => AsIndeterminate();

    /// <summary>
    /// Returns a copy of this value that is known, when it is not known only because it was read as not known (<see cref="AsIndeterminate"/>).
    /// </summary>
    /// <remarks>
    /// For the statements that give a variable a value that no longer depends on what it held: they are what makes it known again.
    /// </remarks>
    public VBTypedValue AsKnown()
        => Handle is IndeterminateBindingHandle indeterminate ? this with { Handle = indeterminate.Assumed } : this;

    /// <summary>
    /// The bound managed value, or <c>null</c> when the binding cannot yield one.
    /// </summary>
    /// <remarks>
    /// 👉 <see cref="IBindingHandle"/> is a <em>storage</em> concern, not <em>identity</em>: two typed
    /// values of the same type holding the same managed value are equal regardless of how (or whether)
    /// each is currently bound. Equality and hashing therefore key on the exact value type and this
    /// managed value only: never on <see cref="Handle"/>, which is mutable. Whether the value is known
    /// at all is not storage: an indeterminate value is never equal to a known one.
    /// </remarks>
    private object? BoundManagedValue
        => Handle.BindingCapabilities.HasFlag(BindingCapabilities.GetValue) ? Handle.Value.BoxedValue : null;

    /// <summary>
    /// Two typed values are equal when they have the exact same value type, either both are known or neither is (<see cref="IsIndeterminate"/>),
    /// and they hold equal managed values.
    /// </summary>
    public virtual bool Equals(VBTypedValue? other)
        => other is not null
        && EqualityContract == other.EqualityContract
        && IsIndeterminate == other.IsIndeterminate
        && Equals(BoundManagedValue, other.BoundManagedValue);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(EqualityContract, IsIndeterminate, BoundManagedValue);

    /// <summary>
    /// Prints this <c>VBTypedValue</c>'s members for <see cref="object.ToString"/>.
    /// </summary>
    /// <remarks>
    /// Prints <see cref="TypeInfo"/> by <see cref="VBType.Name"/> rather than the <c>VBType</c>
    /// instance itself: that type's own <see cref="VBType.DefaultValue"/> is this kind of value, so
    /// letting the compiler-generated <c>ToString</c> print the full <c>TypeInfo</c> object would
    /// recurse between the two <c>PrintMembers</c> implementations forever.
    /// </remarks>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder.Append("TypeInfo = ").Append(TypeInfo.Name);
        builder.Append(", Handle = ").Append(Handle);
        return true;
    }
}