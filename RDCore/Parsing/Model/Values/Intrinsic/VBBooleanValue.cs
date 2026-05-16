using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Runtime;

namespace RDCore.Parsing.Model.Values.Intrinsic;

internal record class VBBooleanValue(Symbol? Symbol = null) : VBTypedValue(VBBooleanType.TypeInfo, Symbol), 
    IVBTypedValue<VBBooleanValue, bool>, 
    INumericCoercion, IStringCoercion
{
    private static readonly Lazy<VBBooleanValue> _falseValue = new(() => new VBBooleanValue(GlobalSymbols.False) { Value = false });
    public static VBBooleanValue False { get; } = _falseValue.Value;

    private static readonly Lazy<VBBooleanValue> _trueValue = new(() => new VBBooleanValue(GlobalSymbols.True) { Value = true });
    public static VBBooleanValue True { get; } = _trueValue.Value;

    public bool Value { get; init; } = default;
    public override int Size { get; } = 16;

    public VBDoubleValue? AsCoercedDouble(ref int depth) => new VBDoubleValue(Symbol).WithValue(-1 * Convert.ToDouble(Value));
    public VBStringValue? AsCoercedString(ref int depth) => new VBStringValue(Symbol).WithValue(ToString());
    public VBFixedStringValue? AsCoercedFixedLengthString(int length, ref int depth) => 
        AsCoercedString(ref depth) is VBStringValue value ? new VBFixedStringValue(length).WithFixedValue(value.Value) : null;

    public VBBooleanValue WithValue(bool value) => this with { Value = value };
    public VBBooleanValue WithValue(int value) => this with { Value = value != 0 };
    public VBBooleanValue WithValue(double value) => this with { Value = value != 0 };

    public override string ToString() => Value ? Tokens.True : Tokens.False;

    public bool Equals(IVBTypedValue<VBBooleanValue, bool>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
