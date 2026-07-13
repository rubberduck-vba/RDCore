using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>LongLong</c> value.
/// </summary>
/// <param name="Symbol">The symbol associated with this value.</param>
public sealed record class VBLongLongValue(Symbol Symbol) : VBNumericTypedValue(VBLongLongType.TypeInfo, Symbol),
    IVBTypedValue<VBLongLongValue, long>, 
    INumericValue<VBLongLongValue>
{
    public long Value => ManagedValue.InteropValue!.Value.Int64;
    public override int Size => sizeof(long);

    public bool Equals(IVBTypedValue<VBLongLongValue, long>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
