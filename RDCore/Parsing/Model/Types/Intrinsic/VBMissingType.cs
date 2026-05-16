using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;

namespace RDCore.Parsing.Model.Types.Intrinsic;

/// <summary>
/// Represents the <c>Variant</c> subtype given to an optional <c>Variant</c> parameter that was not supplied.
/// </summary>
internal record class VBMissingType() : VBIntrinsicType<IntPtr>("<missing>")
{
    private static readonly Lazy<VBMissingType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBMissingType TypeInfo => _instance.Value;

    private static readonly Lazy<VBMissingValue> _defaultValue = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public override VBMissingValue DefaultValue => _defaultValue.Value;

    public override VBType[] ConvertsSafelyToTypes { get; } = [VBVariantType.TypeInfo];
    public override bool IsDeclarable => false;
}