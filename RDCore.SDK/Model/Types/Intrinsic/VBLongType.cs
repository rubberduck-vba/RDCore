using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types.Intrinsic;

public record class VBLongType() : VBIntrinsicType<int>(Tokens.Long), IIntegralNumericType
{
    private static readonly Lazy<VBLongType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBLongType TypeInfo => _instance.Value;

    public override VBTypedValue DefaultValue { get; } = VBLongValue.Zero;
    public override string? DefToken => Tokens.DefLng;
}
