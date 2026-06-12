using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic
{
    /// <summary>
    /// Represents a <c>Variant</c> value.
    /// </summary>
    /// <remarks>
    /// 👉 The <em>managed type</em> of this value is a <see cref="VBVariantInteropValue"/>
    /// </remarks>
    /// <param name="TypedValue">The wrapped typed value (may be another <c>Variant</c>).</param>
    /// <param name="Symbol">The symbol associated with this value.</param>
    public record class VBVariantValue(VBTypedValue TypedValue, Symbol Symbol)
        : VBTypedValue(TypedValue.TypeInfo, Symbol), IVBTypedValue<VBVariantValue, VBVariantInteropValue>
    {
        public VBVariantInteropValue Value { get; init; } = new(VBVariantValueType.Empty, ScopeKind.Unallocated, 0);

        public override int Size => sizeof(long); // the size of VBVariantInteropValue.ValuePtr... probably not what MS-VBA would report

        public override object BoxedValue => Value;

        public VBVariantValue WithValue(VBTypedValue value)
        {
            return this with
            {
                TypedValue = value,
                Value = new VBVariantInteropValue(VBVariantValueType.Dispatch, value.ResolvedSymbol.ScopeKind, value.RawAddress),
                TypeInfo = VBVariantType.TypeInfo with { SubType = value.TypeInfo }
            };
        }

        public bool Equals(IVBTypedValue<VBVariantValue, VBVariantInteropValue>? other) => Value == other?.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }
}
