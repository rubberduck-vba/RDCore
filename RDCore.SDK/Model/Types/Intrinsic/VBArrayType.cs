using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents any type of array.
/// </summary>
public abstract record class VBArrayType() : VBIntrinsicType<object[]>("Array"), IEnumerableType
{
    private static readonly Lazy<VBArrayType> _instance = new(() => new VBResizableArrayType(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets an instance of the <c>VBResizableArrayType</c>.
    /// </summary>
    public static VBArrayType TypeInfo => _instance.Value;

    private static readonly Lazy<VBArrayValue> _defaultValue = new(() => VBResizableArrayValue.Empty, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets an empty (uninitialized) <c>VBResizableArrayValue</c>.
    /// </summary>
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    /// <summary>
    /// The size of an array pointer.
    /// </summary>
    /// <remarks>
    /// You may be looking for <c>VBArrayValue.Size</c>.
    /// </remarks>
    public override int Size => sizeof(int);
}
