using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;

namespace RDCore.Parsing.Model.Types.Intrinsic;

internal record class VBSingleType() : VBIntrinsicType<float>(Tokens.Single), IFloatingPointNumericType
{
    /// <summary>
    /// The number of significant digits retained in a String representation of a value of this type.
    /// </summary>
    public const int SignificantIntegerDigits = 7;

    private static readonly Lazy<VBSingleType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBSingleType TypeInfo => _instance.Value;

    private static readonly Lazy<VBSingleValue> _defaultValue = new(() => VBSingleValue.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override string? DefToken => Tokens.DefSng;
}
