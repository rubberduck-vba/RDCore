using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBErrorType : VBIntrinsicType<int>
{
    private static readonly VBErrorType _type;
    static VBErrorType()
    {
        _type = new();
    }

    public VBErrorType() : base(Tokens.Error) { }

    public static VBErrorType TypeInfo => _type;

    public override VBTypedValue DefaultValue => VBErrorValue.None;
    public override VBType[] ConvertsSafelyToTypes => [VBVariantType.TypeInfo];
}
