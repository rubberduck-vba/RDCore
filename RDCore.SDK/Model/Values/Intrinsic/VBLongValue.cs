using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Long</c> value.
/// </summary>
/// <param name="Symbol">The symbol associated with this value.</param>
public sealed record class VBLongValue(Symbol Symbol) : VBNumericTypedValue(VBLongType.TypeInfo, Symbol),
    IVBTypedValue<VBLongValue, int>,
    INumericValue<VBLongValue>
{
    public int Value => ManagedValue.InteropValue!.Value.Int32;
    public override int Size => sizeof(int);

    public bool Equals(IVBTypedValue<VBLongValue, int>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
