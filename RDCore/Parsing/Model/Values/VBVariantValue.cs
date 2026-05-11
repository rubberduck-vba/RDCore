using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using System.Collections.Immutable;

namespace RDCore.Parsing.Model.Values;

internal record class VBVariantValue : VBTypedValue, IVBTypedValue<VBVariantValue, object?>, INumericCoercion, IStringCoercion
{
    public VBVariantValue(VBTypedValue typedValue, Symbol? symbol = null)
        : base(VBVariantType.TypeInfo with { Subtype = typedValue.TypeInfo }, symbol) { }

    public VBTypedValue? TypedValue { get; init; } = default;
    public object? Value { get; init; } = default;
    public override int Size => nint.Size;

    public VBDoubleValue? AsCoercedNumeric(ref int depth) =>
        ((VBVariantType)TypeInfo).Subtype is INumericCoercion coercibleNumeric ? coercibleNumeric.AsCoercedNumeric(ref depth) : null;

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

internal record class VBDeferredMemberValue : VBTypedValue
{
    public VBDeferredMemberValue(Symbol? symbol)
        : base(VBVariantType.TypeInfo, symbol)
    {
    }

    public override int Size => sizeof(int);

    public string Name { get; init; } = string.Empty;
    public VBDeferredMemberValue WithName(string name) => this with { Name = name };

    public VBTypedValue? Context { get; init; }
    public VBDeferredMemberValue WithContext(VBTypedValue context) => this with { Context = context };

    public ImmutableArray<Diagnostic> Diagnostics { get; init; } = [];
    public VBDeferredMemberValue WithDiagnostic(Diagnostic diagnostic) => this with { Diagnostics = [.. Diagnostics, diagnostic] };
}