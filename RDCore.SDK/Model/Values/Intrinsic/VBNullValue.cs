using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Null</c> (<c>VBNullType</c>) literal value.
/// </summary>
/// <param name="Symbol">The symbol associated with this value.</param>
public sealed record class VBNullValue(Symbol Symbol) 
    : VBTypedValue(VBNullType.TypeInfo, Symbol), IVBTypedValue<VBNullValue, int>
{
    private static readonly Lazy<VBNullValue> _instance = new(() => new(GlobalSymbols.StaticSymbols.Null));
    public static VBNullValue Null => _instance.Value;

    public int Value { get; } = 0;
    public override int Size => 0;

    public bool Equals(IVBTypedValue<VBNullValue, int>? other) => Value == other?.Value;
}
