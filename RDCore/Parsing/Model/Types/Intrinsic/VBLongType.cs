using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;

namespace RDCore.Parsing.Model.Types.Intrinsic;

internal record class VBLongType() : VBIntrinsicType<int>(Tokens.Long), IIntegralNumericType
{
    private static readonly Lazy<VBLongType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBLongType TypeInfo => _instance.Value;

    public override VBTypedValue DefaultValue { get; } = VBLongValue.Zero;
    public override string? DefToken => Tokens.DefLng;
}
