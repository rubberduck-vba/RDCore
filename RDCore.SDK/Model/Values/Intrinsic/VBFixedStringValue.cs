using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Interop;

namespace RDCore.SDK.Model.Values.Intrinsic;

public sealed record class VBFixedStringValue : VBStringValue
{
    public VBFixedStringValue(int length, Symbol symbol)
        : base(symbol)
    {
        Length = length;
    }

    public VBFixedStringValue(VBStringValue value)
        : base(value.ResolvedSymbol)
    {
        Length = value.Length;
        ManagedValue = new(new ManagedInteropReference(typeof(string), value.ResolvedSymbol.ScopeKind, FixLength(value.Value, Length)));
    }

    public override int Length { get; }

    public VBFixedStringValue WithFixedValue(string value) => new(WithValue(value));

    public override VBStringValue WithValue(string? value)
    {
        var fixedValue = FixLength(value, Length);
        return this with { ManagedValue = new(new ManagedInteropReference(typeof(string), ResolvedSymbol.ScopeKind, fixedValue)) };
    }

    private static string FixLength(string? value, int length)
    {
        // MS-VBAL 5.5.1.2.5 let-coercion to String*length (fixed-length strings)
        value ??= string.Empty;
        if (value.Length > length)
        {
            value = value[..length];
        }
        else if (value.Length < length)
        {
            value = value.PadRight(length, ' ');
        }
        return value;
    }
}