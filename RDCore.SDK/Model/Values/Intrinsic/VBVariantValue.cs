using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Interop;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Variant</c> value.
/// </summary>
/// <remarks>
/// 👉 The <em>managed type</em> of this value is a <see cref="ManagedInteropVariant"/>
/// </remarks>
/// <param name="TypedValue">The wrapped typed value (may be another <c>Variant</c>).</param>
/// <param name="Symbol">The symbol associated with this value.</param>
public record class VBVariantValue(VBTypedValue TypedValue, Symbol Symbol)
    : VBTypedValue(TypedValue.TypeInfo, Symbol), IVBTypedValue<VBVariantValue, ManagedInteropVariant>
{
    public ManagedInteropVariant Value { get; init; } = new(VBVariantValueType.Empty, ScopeKind.Unallocated, new ValueBindingHandle(ManagedInteropValue<int>.Int32ZeroValue));

    public override int Size => sizeof(long); // the size of VBVariantInteropValue.ValuePtr... probably not what MS-VBA would report

    public VBVariantValue WithValue(VBTypedValue value)
    {
        return this with
        {
            TypedValue = value,
            //Value = new ManagedInteropVariant(VBVariantValueType.Dispatch, value.ResolvedSymbol.ScopeKind, Value.Handle),
            TypeInfo = VBVariantType.TypeInfo with { SubType = value.TypeInfo }
        };
    }

    public bool Equals(IVBTypedValue<VBVariantValue, ManagedInteropVariant>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
