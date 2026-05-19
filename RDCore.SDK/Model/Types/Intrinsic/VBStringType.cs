using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types.Intrinsic;

public record class VBStringType() : VBIntrinsicType<string?>(Tokens.String)
{
    private static readonly Lazy<VBStringType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBStringType TypeInfo => _instance.Value;

    private static readonly Lazy<VBStringValue> _defaultValue = new(() => VBStringValue.VBNullString, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
    
    public sealed override VBType[] ConvertsSafelyToTypes { get; } = [VBVariantType.TypeInfo];
    public override string? DefToken => Tokens.DefStr;
}

public sealed record class VBFixedStringType : VBStringType
{
    public int Length { get; init; }
    public override string? DefToken => default;

    private static readonly Lazy<VBStringValue> _defaultValue = new(() => new VBStringValue(GlobalSymbols.VBNullString) { Value = string.Empty }, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}