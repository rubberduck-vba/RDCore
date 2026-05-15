using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBIntegerType() : VBIntrinsicType<short>(Tokens.Integer), IIntegralNumericType
{
    private static readonly Lazy<VBIntegerType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBIntegerType TypeInfo => _instance.Value;

    private static readonly Lazy<VBIntegerValue> _defaultValue = new(() => VBIntegerValue.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override string? DefToken => Tokens.DefInt;
}
