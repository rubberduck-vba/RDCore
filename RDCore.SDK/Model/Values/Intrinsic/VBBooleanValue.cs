using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Interop;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// A <see cref="VBTypedValue"/> representing a runtime value of the <see cref="VBBooleanType"/> data type.
/// </summary>
/// <param name="Symbol">The <see cref="Symbol"/> associated with this value.</param>
public sealed record class VBBooleanValue(Symbol Symbol) 
    : VBTypedValue(VBBooleanType.TypeInfo, Symbol), IVBTypedValue<VBBooleanValue, bool>
{
    private static readonly Lazy<VBBooleanValue> _falseValue = new(() => new VBBooleanValue(GlobalSymbols.StaticSymbols.False) { ManagedValue = new(ManagedInteropValue.BooleanFalse) });
    public static VBBooleanValue False { get; } = _falseValue.Value;

    private static readonly Lazy<VBBooleanValue> _trueValue = new(() => new VBBooleanValue(GlobalSymbols.StaticSymbols.True) { ManagedValue = new(ManagedInteropValue.BooleanTrue) });
    public static VBBooleanValue True { get; } = _trueValue.Value;

    public bool Value => ManagedValue.InteropValue!.Value.Boolean;
    public override int Size { get; } = 16;

    public override string ToString() => Value ? Tokens.True : Tokens.False;

    public bool Equals(IVBTypedValue<VBBooleanValue, bool>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();


    // TOOD move to let-coercion semantics:
    // bool -> numeric
    //public VBDoubleValue? AsCoercedDouble(ref int depth) => new VBDoubleValue(Symbol).WithValue(-1 * Convert.ToDouble(Value));
    // bool -> string
    //public VBStringValue? AsCoercedString(ref int depth) => new VBStringValue(Symbol).WithValue(ToString());
    // bool -> string*length
    //public VBFixedStringValue? AsCoercedFixedLengthString(int length, ref int depth) =>
    //    AsCoercedString(ref depth) is VBStringValue value ? new VBFixedStringValue(length).WithFixedValue(value.Value) : null;
}
