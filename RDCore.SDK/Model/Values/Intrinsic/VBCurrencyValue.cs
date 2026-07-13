using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Interop;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// A <see cref="VBNumericTypedValue"/> representing a runtime value of the <see cref="VBCurrencyType"/> data type.
/// </summary>
/// <param name="Symbol">The <see cref="Symbol"/> associated with this value.</param>
public sealed record class VBCurrencyValue(Symbol Symbol) 
    : VBNumericTypedValue(VBCurrencyType.TypeInfo, Symbol), IVBTypedValue<VBCurrencyValue, ManagedCurrencyInteropValue>, INumericValue<VBCurrencyValue>
{
    public ManagedCurrencyInteropValue Value => ManagedValue.InteropValue!.Value.Currency!.Value;
    public override int Size => sizeof(long);

    public bool Equals(IVBTypedValue<VBCurrencyValue, ManagedCurrencyInteropValue>? other) => Value.StoredValue == other?.Value.StoredValue;
    public override int GetHashCode() => Value.GetHashCode();
}
