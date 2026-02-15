using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBErrorValue : VBTypedValue,
    IVBTypedValue<VBErrorValue, int>
{
    public VBErrorValue(Symbol? symbol = null, int value = 0) : base(VBErrorType.TypeInfo, symbol)
    {
        Value = value;
    }

    public static VBErrorValue None => (VBErrorValue)VBErrorType.TypeInfo.DefaultValue;
    public static VBErrorValue MinValue => None;
    public static VBErrorValue MaxValue => new() { Value = ushort.MaxValue };

    public int Value { get; init; }
    public override int Size => sizeof(int);

    public VBErrorValue WithValue(int value) => this with { Value = value };
    public override string ToString() => $"Error {Value}";
}