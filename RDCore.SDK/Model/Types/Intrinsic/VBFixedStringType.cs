using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents a <c>String*</c><em>length</em> fixed-length <c>String</c> value.
/// </summary>
/// <param name="Length">The length of the string value.</param>
/// <remarks>
/// Fixed-length strings are padded (with spaces) or truncated as needed to meet the specified length.
/// </remarks>
public sealed record class VBFixedStringType(int Length) : VBStringType
{
    private static readonly Lazy<VBStringValue> _defaultValue = new(() => new VBStringValue(GlobalSymbols.StaticSymbols.VBNullString) { Value = string.Empty }, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}