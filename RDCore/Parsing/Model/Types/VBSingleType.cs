using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBSingleType : VBIntrinsicType<float>, INumericType
{
    private static readonly VBSingleType _type = new();

    private VBSingleType() : base(Tokens.Single) { }
    public static VBSingleType TypeInfo => _type;

    public override VBTypedValue DefaultValue { get; } = VBSingleValue.Zero;
    public override string? DefToken => Tokens.DefSng;
}
