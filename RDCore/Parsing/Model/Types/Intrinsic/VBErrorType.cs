using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;

namespace RDCore.Parsing.Model.Types.Intrinsic;

internal sealed record class VBErrorType() : VBIntrinsicType<int>(Tokens.Error)
{
    private static readonly Lazy<VBErrorType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBErrorType TypeInfo => _instance.Value;

    private static readonly Lazy<VBErrorValue> _defaultValue = new(() => VBErrorValue.None, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override VBType[] ConvertsSafelyToTypes => [VBVariantType.TypeInfo];
}
