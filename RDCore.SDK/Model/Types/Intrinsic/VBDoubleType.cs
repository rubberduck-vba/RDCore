using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types.Intrinsic;

public sealed record class VBDoubleType() : VBIntrinsicType<double>(Tokens.Double), IFloatingPointNumericType
{
    /// <summary>
    /// The number of significant digits retained in a String representation of a value of this type.
    /// </summary>
    public const int SignificantIntegerDigits = 15;

    private static readonly Lazy<VBDoubleType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBDoubleType TypeInfo => _instance.Value;

    private static readonly Lazy<VBDoubleValue> _defaultValue = new(() => VBDoubleValue.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
    public override string? DefToken => Tokens.DefDbl;
}
