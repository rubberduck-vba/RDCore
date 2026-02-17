using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBObjectType : VBIntrinsicType<Guid>
{
    private static readonly VBObjectType _type = new();

    private VBObjectType() : base(Tokens.Object) { }
    public static VBObjectType TypeInfo => _type;

    public override bool RuntimeBinding { get; } = true;
    public override VBTypedValue DefaultValue { get; } = VBObjectValue.Nothing;
    public override VBType[] ConvertsSafelyToTypes { get; } = [VBVariantType.TypeInfo];
    public override string? DefToken => Tokens.DefObj;
}
