using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

public record class VBIntegerValue(Symbol Symbol) : VBNumericTypedValue(VBIntegerType.TypeInfo, Symbol),
    IVBTypedValue<VBIntegerValue, short>,
    INumericValue<VBIntegerValue>
{
    public short Value => ManagedValue.InteropValue!.Value.Int16;
    public override int Size { get; } = sizeof(short);

    public bool Equals(IVBTypedValue<VBIntegerValue, short>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
