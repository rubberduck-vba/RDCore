using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBDoubleType : VBIntrinsicType<double>, IFloatingPointNumericType
{
    /// <summary>
    /// The number of significant digits retained in a String representation of a value of this type.
    /// </summary>
    public const int SignificantIntegerDigits = 15;

    private static readonly VBDoubleType _type = new();

    private VBDoubleType() : base(Tokens.Double) { }

    public static VBDoubleType TypeInfo => _type;

    public override VBTypedValue DefaultValue { get; } = VBDoubleValue.Zero;
    public override string? DefToken => Tokens.DefDbl;
}
