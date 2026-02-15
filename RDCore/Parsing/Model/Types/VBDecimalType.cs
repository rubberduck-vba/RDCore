using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBDecimalType : VBIntrinsicType<decimal>, INumericType
{
    private static readonly VBDecimalType _type = new();

    private VBDecimalType() : base(Tokens.Decimal) { }

    public static VBDecimalType TypeInfo => _type;

    public override VBTypedValue DefaultValue { get; } = VBDecimalValue.Zero;

    public override bool IsDeclarable { get; } = false; // "As Decimal" is explicitly specified as illegal.
}
