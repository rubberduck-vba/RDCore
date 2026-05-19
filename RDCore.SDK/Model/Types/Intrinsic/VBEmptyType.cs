using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types.Intrinsic;

public sealed record class VBEmptyType() : VBIntrinsicType<int?>(Tokens.Empty)
{
    private static readonly Lazy<VBEmptyType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBEmptyType TypeInfo => _instance.Value;

    private static readonly Lazy<VBEmptyValue> _defaultValue = new(() => VBEmptyValue.Empty, LazyThreadSafetyMode.PublicationOnly);
    public override VBEmptyValue DefaultValue => _defaultValue.Value;

    public override VBType[] ConvertsSafelyToTypes => [VBVariantType.TypeInfo];
    public override bool IsDeclarable =>  false;
}
