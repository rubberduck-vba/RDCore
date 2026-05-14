using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBIntegerType : VBIntrinsicType<short>, IIntegralNumericType
{
    private static readonly VBIntegerType _type = new();

    private VBIntegerType() : base(Tokens.Integer) { }

    public static VBIntegerType TypeInfo => _type;

    public override VBTypedValue DefaultValue { get; } = VBIntegerValue.Zero;
    public override string? DefToken => Tokens.DefInt;
}
