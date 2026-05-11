using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBErrorValue : VBTypedValue,
    IVBTypedValue<VBErrorValue, int>
{
    public VBErrorValue(Symbol? symbol = null, int value = 0) : base(VBErrorType.TypeInfo, symbol)
    {
        Value = value;
    }

    public static VBErrorValue None => new() { Value = 0 };
    public static VBErrorValue MinValue => new() { Value = ushort.MinValue };
    public static VBErrorValue MaxValue => new() { Value = ushort.MaxValue };

    public int Value { get; init; }
    public override int Size => sizeof(int);

    public VBErrorValue WithValue(int value) => this with { Value = value };
    public override string ToString() => $"Error {Value}";

    public bool Equals(IVBTypedValue<VBErrorValue, int>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}