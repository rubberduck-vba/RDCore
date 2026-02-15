using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBIntegerType : VBIntrinsicType<short>, INumericType
{
    private static readonly VBIntegerType _type = new();

    private VBIntegerType() : base(Tokens.Integer) { }

    public static VBIntegerType TypeInfo => _type;

    public override VBTypedValue DefaultValue { get; } = VBIntegerValue.Zero;
}
