using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBAnyType : VBIntrinsicType<object?>
{
    private static readonly VBAnyType _type = new();

    private VBAnyType() : base(Tokens.Any) { }

    public static VBAnyType TypeInfo => _type;

    public override bool RuntimeBinding { get; } = true;
    public override VBTypedValue DefaultValue => VBVariantType.TypeInfo.DefaultValue;
    public override VBType[] ConvertsSafelyToTypes { get; } = [];
}
