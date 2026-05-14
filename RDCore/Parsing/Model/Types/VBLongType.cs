using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBLongType : VBIntrinsicType<int>, IIntegralNumericType
{
    private static readonly VBLongType _type = new();

    private VBLongType() : base(Tokens.Long) { }
    public static VBLongType TypeInfo => _type;

    public override VBTypedValue DefaultValue { get; } = VBLongValue.Zero;
    public override string? DefToken => Tokens.DefLng;
}
