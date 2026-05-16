using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;

namespace RDCore.Parsing.Model.Types.Intrinsic;

internal sealed record class VBByteType() : VBIntrinsicType<byte>(Tokens.Byte), IIntegralNumericType
{
    private static readonly Lazy<VBByteType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBByteType TypeInfo => _instance.Value;

    private static readonly Lazy<VBByteValue> _defaultValue = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public override VBByteValue DefaultValue => _defaultValue.Value;
    public override string? DefToken => Tokens.DefByte;

    public VBNumericTypedValue WithValue(double value, Symbol? symbol = default) => symbol is null
        ? DefaultValue.WithValue(value)
        : DefaultValue with { Symbol = symbol, NumericValue = value };
}
