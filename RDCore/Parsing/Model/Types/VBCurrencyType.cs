using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBCurrencyType() : VBIntrinsicType<decimal>(Tokens.Currency), IFixedPointNumericType
{
    private static readonly Lazy<VBCurrencyType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBCurrencyType TypeInfo => _instance.Value;

    private static readonly Lazy<VBCurrencyValue> _defaultValue = new(() => VBCurrencyValue.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override string? DefToken => Tokens.DefCur;
}
