using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types.Intrinsic;

public sealed record class VBByteType() : VBIntrinsicType<byte>(Tokens.Byte), IIntegralNumericType
{
    private static readonly Lazy<VBByteType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBByteType TypeInfo => _instance.Value;

    private static readonly Lazy<VBByteValue> _defaultValue = new(() => new(GlobalSymbols.VBByteZeroValue), LazyThreadSafetyMode.PublicationOnly);
    public override VBByteValue DefaultValue => _defaultValue.Value;
    public override string? DefToken => Tokens.DefByte;

    public VBNumericTypedValue WithValue(double value, Symbol? symbol = default) => symbol is null
        ? DefaultValue.WithValue(value)
        : DefaultValue with { Symbol = symbol, ManagedValue = value };
}
