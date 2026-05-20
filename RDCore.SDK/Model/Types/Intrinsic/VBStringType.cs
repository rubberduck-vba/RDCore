using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>String</c> intrinsic data type.
/// </summary>
public record class VBStringType() : VBIntrinsicType<string?>(Tokens.String)
{
    private static readonly Lazy<VBStringType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBStringType TypeInfo => _instance.Value;

    private static readonly Lazy<VBStringValue> _defaultValue = new(() => VBStringValue.VBNullString, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    /// <summary>
    /// Gets the size of a string pointer.
    /// </summary>
    public override int Size => sizeof(int);
}
