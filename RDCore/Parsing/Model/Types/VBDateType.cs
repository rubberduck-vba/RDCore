using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBDateType : VBIntrinsicType<DateTime>
{
    private static readonly VBDateType _type = new();

    private VBDateType() : base(Tokens.Date) { }
    public static VBDateType TypeInfo => _type;

    public override VBTypedValue DefaultValue { get; } = VBDateValue.Zero;
    public override VBType[] ConvertsSafelyToTypes { get; } = [VBStringType.TypeInfo, VBVariantType.TypeInfo];
    public override string? DefToken => Tokens.DefDate;
}
