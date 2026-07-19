using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Interop;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Single</c> numeric value.
/// </summary>
/// <param name="Symbol">The symbol associated with this value.</param>
public sealed record class VBSingleValue(Symbol Symbol) : VBNumericTypedValue(VBSingleType.TypeInfo, Symbol),
    IVBTypedValue<VBSingleValue, float>,
    INumericValue<VBSingleValue>
{
    public float Value => ((ManagedInteropValue<float>)ManagedValue.InteropValue!).Value;
    public override int Size => sizeof(float);
    public bool Equals(IVBTypedValue<VBSingleValue, float>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
