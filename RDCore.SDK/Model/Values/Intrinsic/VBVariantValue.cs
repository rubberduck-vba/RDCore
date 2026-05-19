using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

public record class VBVariantValue : VBTypedValue, IVBTypedValue<VBVariantValue, object?>, INumericCoercion, IStringCoercion
{
    public VBVariantValue(VBTypedValue typedValue, Symbol? symbol = null)
        : base(VBVariantType.TypeInfo with { Subtype = typedValue.TypeInfo }, symbol) { }

    public VBTypedValue? TypedValue { get; init; } = default;
    public object? Value { get; init; } = default;
    public override int Size => nint.Size;

    public VBDoubleValue? AsCoercedDouble(ref int depth) =>
        ((VBVariantType)TypeInfo).Subtype is INumericCoercion coercibleNumeric ? coercibleNumeric.AsCoercedDouble(ref depth) : null;

    public VBStringValue? AsCoercedString(ref int depth) =>
        ((VBVariantType)TypeInfo).Subtype is IStringCoercion coercibleString ? coercibleString.AsCoercedString(ref depth) : null;

    public VBFixedStringValue? AsCoercedFixedLengthString(int length, ref int depth) =>
        ((VBVariantType)TypeInfo).Subtype is IStringCoercion coercibleString ? coercibleString.AsCoercedFixedLengthString(length, ref depth) : null;

    public VBVariantValue WithValue(VBTypedValue value) =>
        this with
        {
            TypedValue = value,
            Value = value,
            TypeInfo = VBVariantType.TypeInfo with { Subtype = value.TypeInfo }
        };

    public bool Equals(IVBTypedValue<VBVariantValue, object?>? other) => Value == other?.Value;
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;
}

public record class VBDeferredMemberValue(Symbol? Symbol = null) : VBTypedValue(VBVariantType.TypeInfo, Symbol)
{
    public override int Size => sizeof(int);

    public string Name { get; init; } = string.Empty;
    public VBDeferredMemberValue WithName(string name) => this with { Name = name };

    /// <summary>
    /// Represents the context of the deferred member value - could be a class or std module value.
    /// </summary>
    public VBTypedValue? Context { get; init; }
    public VBDeferredMemberValue WithContext(VBTypedValue context) => this with { Context = context };
}