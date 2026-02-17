using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBCurrencyType : VBIntrinsicType<decimal>, INumericType
{
    private static readonly VBCurrencyType _type = new();

    private VBCurrencyType() : base(Tokens.Currency) { }

    public static VBCurrencyType TypeInfo => _type;

    public override VBTypedValue DefaultValue { get; } = VBCurrencyValue.Zero;

    public override string? DefToken => Tokens.DefCur;
}
