using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBErrorType : VBIntrinsicType<int>
{
    private static readonly VBErrorType _type = new();
    private VBErrorType() : base(Tokens.Error) { }

    public static VBErrorType TypeInfo => _type;

    public override VBTypedValue DefaultValue => VBErrorValue.None;
    public override VBType[] ConvertsSafelyToTypes => [VBVariantType.TypeInfo];
}
