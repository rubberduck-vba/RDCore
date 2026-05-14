using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBSingleType : VBIntrinsicType<float>, IFloatingPointNumericType
{
    /// <summary>
    /// The number of significant digits retained in a String representation of a value of this type.
    /// </summary>
    public const int SignificantIntegerDigits = 7;

    private static readonly VBSingleType _type = new();

    private VBSingleType() : base(Tokens.Single) { }
    public static VBSingleType TypeInfo => _type;

    public override VBTypedValue DefaultValue { get; } = VBSingleValue.Zero;
    public override string? DefToken => Tokens.DefSng;
}
