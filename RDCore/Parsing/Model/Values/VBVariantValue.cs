using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBVariantValue : VBTypedValue, IVBTypedValue<VBVariantValue, object?>, INumericCoercion, IStringCoercion
{
    public VBVariantValue(VBTypedValue typedValue, Symbol? symbol = null)
        : base(VBVariantType.TypeInfo with { Subtype = typedValue.TypeInfo }, symbol) { }

    public VBTypedValue? TypedValue { get; init; } = default;
    public object? Value { get; init; } = default;
    public override int Size => nint.Size;

    public VBDoubleValue? AsCoercedNumeric(int depth = 0) =>
        ((VBVariantType)TypeInfo).Subtype is INumericCoercion coercibleNumeric ? coercibleNumeric.AsCoercedNumeric(depth) : null!;

    public VBStringValue? AsCoercedString(int depth = 0) =>
        ((VBVariantType)TypeInfo).Subtype is IStringCoercion coercibleString ? coercibleString.AsCoercedString(depth) : null!;

    public VBVariantValue WithValue(VBTypedValue value) =>
        this with
        {
            TypedValue = value,
            Value = value,
            TypeInfo = VBVariantType.TypeInfo with { Subtype = value.TypeInfo }
        };
}
