using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types.Intrinsic;

public record class VBLongLongType() : VBIntrinsicType<long>(Tokens.LongLong), IIntegralNumericType
{
    private static readonly Lazy<VBLongLongType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBLongLongType TypeInfo => _instance.Value;

    private static readonly Lazy<VBLongLongValue> _defaultValue = new(() => VBLongLongValue.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override string? DefToken => Tokens.DefLngLng; // yup, they did this.
}
