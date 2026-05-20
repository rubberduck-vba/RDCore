using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>Variant</c> subtype given to an optional <c>Variant</c> parameter that was not supplied.
/// </summary>
public record class VBMissingType() : VBIntrinsicType<IntPtr>("<missing>")
{
    private static readonly Lazy<VBMissingType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBMissingType TypeInfo => _instance.Value;

    private static readonly Lazy<VBMissingValue> _defaultValue = new(() => new(GlobalSymbols.StaticSymbols.MissingValue), LazyThreadSafetyMode.PublicationOnly);
    public override VBMissingValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int);
}