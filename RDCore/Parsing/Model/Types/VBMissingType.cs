using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

/// <summary>
/// Represents the <c>Variant</c> subtype given to an optional <c>Variant</c> parameter that was not supplied.
/// </summary>
internal record class VBMissingType : VBIntrinsicType<IntPtr>
{
    private static readonly VBMissingType _type = new();

    public VBMissingType() : base("<missing>") { }

    public static VBMissingType TypeInfo => _type;
    public override bool IsDeclarable => false;

    public override VBMissingValue DefaultValue { get; } = new();
    public override VBType[] ConvertsSafelyToTypes { get; } = [VBVariantType.TypeInfo];
}