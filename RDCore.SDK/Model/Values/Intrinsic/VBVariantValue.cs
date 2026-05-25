using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Variant</c> value.
/// </summary>
/// <param name="TypedValue">The wrapped typed value (may be another <c>Variant</c>).</param>
/// <param name="Symbol">The symbol associated with this value.</param>
public record class VBVariantValue(VBTypedValue TypedValue, Symbol Symbol) 
    : VBTypedValue(TypedValue.TypeInfo, Symbol), IVBTypedValue<VBVariantValue, object?>
{
    public object? Value { get; init; } = default;

    public override int Size => nint.Size; // lies

    // TODO make this speak VT_VARIANT
    public VBVariantValue WithValue(VBTypedValue value) =>
        this with
        {
            TypedValue = value,
            Value = value, // NOTE: we're putting a VBTypedValue into a managed slot here
            TypeInfo = VBVariantType.TypeInfo with { SubType = value.TypeInfo }
        };

    public bool Equals(IVBTypedValue<VBVariantValue, object?>? other) => Value == other?.Value;
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;
}
