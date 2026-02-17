using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBEmptyType : VBIntrinsicType<int?>
{
    private static readonly VBEmptyType _type = new();

    private VBEmptyType() : base(Tokens.vbEmpty) { }
    public static VBEmptyType TypeInfo => _type;

    public override VBEmptyValue DefaultValue { get; } = VBEmptyValue.Empty;
    public override VBType[] ConvertsSafelyToTypes => [VBVariantType.TypeInfo];
}
