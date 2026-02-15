using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBDoubleType : VBIntrinsicType<double>, INumericType
{
    private static readonly VBDoubleType _type = new();

    private VBDoubleType() : base(Tokens.Double) { }

    public static VBDoubleType TypeInfo => _type;

    public override VBTypedValue DefaultValue { get; } = VBDoubleValue.Zero;
}
