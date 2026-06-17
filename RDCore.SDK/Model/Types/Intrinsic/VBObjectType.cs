#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBIntrinsicType{int}"/> representing the <c>Object</c> data type.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>int</c>.<br/>
/// 👉 This data type represents a polymorphic object reference.
/// </remarks>
public record class VBObjectType() : VBIntrinsicType<int>(VBTypeNames.VBObject)
{
    private static readonly Lazy<VBObjectType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Object</c> data type.
    /// </summary>
    public static VBObjectType TypeInfo => _instance.Value;

    private static readonly Lazy<VBObjectValue> _defaultValue = new(() => VBObjectValue.Nothing, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int);
}
