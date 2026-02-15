using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBBooleanValue : VBTypedValue, IVBTypedValue<VBBooleanValue, bool>
{
    public VBBooleanValue(Symbol? declarationSymbol = null)
        : base(VBBooleanType.TypeInfo, declarationSymbol) { }

    public static VBBooleanValue False { get; } = new VBBooleanValue { Value = false };
    public static VBBooleanValue True { get; } = new VBBooleanValue { Value = true };

    public bool Value { get; init; } = default;
    public override int Size { get; } = 16;

    public VBDoubleValue AsCoercedNumeric() => new VBDoubleValue(Symbol).WithValue(-1 * Convert.ToDouble(Value));
    public VBStringValue AsCoercedString() => new VBStringValue(Symbol).WithValue(ToString());

    public VBBooleanValue WithValue(bool value) => this with { Value = value };
    public VBBooleanValue WithValue(int value) => this with { Value = value != 0 };

    public override string ToString() => Value ? Tokens.True : Tokens.False;
}
