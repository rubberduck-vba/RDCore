using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBErrorValue(Symbol? Symbol = null, int Value = 0) : VBTypedValue(VBErrorType.TypeInfo, Symbol),
    IVBTypedValue<VBErrorValue, int>
{
    public static VBErrorValue None => new();
    public static VBErrorValue MinValue => new(default, ushort.MinValue);
    public static VBErrorValue MaxValue => new(default, ushort.MaxValue);

    public override int Size => sizeof(int);

    public VBErrorValue WithValue(int value) => this with { Value = value };
    public override string ToString() => $"Error {Value}";

    public bool Equals(IVBTypedValue<VBErrorValue, int>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}