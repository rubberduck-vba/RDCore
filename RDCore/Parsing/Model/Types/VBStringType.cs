using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBStringType : VBIntrinsicType<string?>
{
    private static readonly VBStringType _type = new();

    protected VBStringType() : base(Tokens.String) { }
    public static VBStringType TypeInfo => _type;

    public override VBTypedValue DefaultValue => VBStringValue.VBNullString;
    public sealed override VBType[] ConvertsSafelyToTypes { get; } = [VBVariantType.TypeInfo];
    public override string? DefToken => Tokens.DefStr;
}

internal sealed record class VBFixedStringType : VBStringType
{
    public int Length { get; init; }
    public override string? DefToken => default;
    public override VBTypedValue DefaultValue => new VBStringValue { Value = new string(' ', Length) };
}