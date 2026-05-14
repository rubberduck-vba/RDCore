using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBBooleanValue : VBTypedValue, IVBTypedValue<VBBooleanValue, bool>, 
    INumericCoercion, IStringCoercion
{
    public VBBooleanValue(Symbol? declarationSymbol = null)
        : base(VBBooleanType.TypeInfo, declarationSymbol) { }

    public static VBBooleanValue False { get; } = new VBBooleanValue { Value = false };
    public static VBBooleanValue True { get; } = new VBBooleanValue { Value = true };

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
