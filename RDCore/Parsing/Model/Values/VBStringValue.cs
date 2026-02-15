using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBStringValue : VBTypedValue,
    IVBTypedValue<VBStringValue, string>,
    INumericCoercion,
    IStringCoercion
{
    public VBStringValue(Symbol? symbol = null)
        : base(VBStringType.TypeInfo, symbol) { }

    public static VBStringValue VBNullString { get; } = new VBStringValue();
    public static VBStringValue ZeroLengthString { get; } = new VBStringValue { Value = string.Empty };

    public string Value { get; init; } = default!;
    public virtual int Length => Value?.Length ?? 0;
    public override int Size => Value is null ? 0 : 2 * Length + 2;

    public VBStringValue AsCoercedString(int depth = 0) => this;
    public VBDoubleValue AsCoercedNumeric(int depth = 0)
    {
        if (double.TryParse(Value, out var coerced))
        {
            return new VBDoubleValue(Symbol).WithValue(coerced);
        }

        throw VBRuntimeErrorException.TypeMismatch(Symbol!, $"Numeric coercion failed to coerce \"{Value}\" to a numeric value.");
    }

    public virtual VBStringValue WithValue(string? value) => this with { Value = value ?? string.Empty };

    public override string ToString() => Value ?? Tokens.vbNullString;
}
