using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBDecimalType : VBIntrinsicType<decimal>, IFixedPointNumericType
{
    private static readonly VBDecimalType _type = new();

    private VBDecimalType() : base(Tokens.Decimal) { }

    public static VBDecimalType TypeInfo => _type;

    public override VBTypedValue DefaultValue { get; } = VBDecimalValue.Zero;

    public override bool IsDeclarable { get; } = false; // "As Decimal" is explicitly specified as illegal.
}
