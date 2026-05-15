using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class VBDateType() : VBIntrinsicType<DateTime>(Tokens.Date)
{
    private static readonly Lazy<VBDateType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBDateType TypeInfo => _instance.Value;

    private static readonly Lazy<VBDateValue> _defaultValue = new(() => VBDateValue.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override VBType[] ConvertsSafelyToTypes { get; } = [VBStringType.TypeInfo, VBVariantType.TypeInfo];
    public override string? DefToken => Tokens.DefDate;
}
