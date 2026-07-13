using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <see cref="VBObjectType"/> value, i.e. an object reference.
/// </summary>
/// <remarks>
/// The default value of a <c>VBObjectValue</c> is <see cref="VBNothingValue"/>.
/// </remarks>
public record class VBObjectValue : VBTypedValue,
    IVBTypedValue<VBObjectValue, int> // FIXME
{
    private static readonly Lazy<VBObjectValue> _nothing = new(() 
        => new VBNothingValue(GlobalSymbols.StaticSymbols.Nothing), LazyThreadSafetyMode.PublicationOnly);
    public static VBObjectValue Nothing => _nothing.Value;

    public VBObjectValue(Symbol symbol) : base(VBObjectType.TypeInfo, symbol) { }

    public int Value { get; init; }
    public override int Size => sizeof(int);

    public bool IsNothing() => Value == Nothing.Value;

    public bool Equals(IVBTypedValue<VBObjectValue, int>? other) => Value.Equals(other?.Value);
    public override int GetHashCode() => Value.GetHashCode();
}
