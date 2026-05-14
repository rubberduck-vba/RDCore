using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBByteType : VBIntrinsicType<byte>, IIntegralNumericType
{
    private static readonly VBByteType _type = new();

    private VBByteType() : base(Tokens.Byte) { }

    public static VBByteType TypeInfo => _type;
    public override VBByteValue DefaultValue { get; } = new();
    public override string? DefToken => Tokens.DefByte;

    public VBNumericTypedValue WithValue(double value, Symbol? symbol = default) => symbol is null
        ? DefaultValue.WithValue(value)
        : DefaultValue with { Symbol = symbol, NumericValue = value };
}
