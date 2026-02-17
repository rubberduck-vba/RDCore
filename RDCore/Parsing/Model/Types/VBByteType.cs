using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBByteType : VBIntrinsicType<byte>, INumericType
{
    private static readonly VBByteType _type = new();

    private VBByteType() : base(Tokens.Byte) { }

    public static VBByteType TypeInfo => _type;
    public override VBByteValue DefaultValue { get; } = new();
    public override string? DefToken => Tokens.DefByte;
}
