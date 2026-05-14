using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBLongLongType : VBIntrinsicType<long>, IIntegralNumericType
{
    private static readonly VBLongLongType _type = new();

    private VBLongLongType() : base(Tokens.LongLong) { }

    public static VBLongLongType TypeInfo => _type;

    public override VBTypedValue DefaultValue { get; } = VBLongLongValue.Zero;
    public override string? DefToken => Tokens.DefLngLng; // yup, they did this.
}
