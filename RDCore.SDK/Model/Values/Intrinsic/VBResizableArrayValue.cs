using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// A value representing a resizable (dynamically-sized) array containing <c>VBByteValue</c> elements.
/// </summary>
/// <remarks>
/// Specifications attach let-coercion semantics (to and from <see cref="VBStringType"/>) to this specific array type.
/// </remarks>
public sealed record class VBResizableByteArrayValue : VBResizableArrayValue
{
    private static readonly Lazy<VBResizableByteArrayValue> _defaultValue = new(() => new([]));
    /// <summary>
    /// Gets an empty <c>VBResizableByteArrayValue</c>.
    /// </summary>
    public static new VBResizableByteArrayValue Empty => _defaultValue.Value;

    /// <summary>
    /// Creates a new resizable array containing <c>VBByteValue</c> elements.
    /// </summary>
    /// <param name="dimensions">The lower and upper bound of each dimension.</param>
    public VBResizableByteArrayValue((int lBound, int uBound)[] dimensions)
        : base(dimensions, VBByteType.TypeInfo) { }

    /// <summary>Creates a resizable Byte() array bound to <paramref name="handle"/> (currently inert).</summary>
    public VBResizableByteArrayValue(IBindingHandle handle, (int lBound, int uBound)[] dimensions)
        : base(handle, dimensions, VBByteType.TypeInfo) { }
}

/// <summary>
/// A value representing a resizable (dynamically-sized) array.
/// </summary>
public record class VBResizableArrayValue : VBArrayValue
{
    private static readonly Lazy<VBResizableArrayValue> _defaultValue = new(() => new([]));
    /// <summary>
    /// Gets an empty (uninitialized) <c>VBResizableArrayValue</c>.
    /// </summary>
    public static VBResizableArrayValue Empty => _defaultValue.Value;

    /// <summary>
    /// Creates a new resizable array containing <c>VBVariantValue</c> elements unless specified otherwise.
    /// </summary>
    /// <param name="dimensions">The lower and upper bound of each dimension.</param>
    /// <param name="itemType">The element type; <see cref="VBVariantType"/> when unspecified.</param>
    public VBResizableArrayValue((int lBound, int uBound)[] dimensions, VBType? itemType = null)
        : base(dimensions, itemType ?? VBVariantType.TypeInfo) { }

    /// <summary>Creates a resizable array bound to <paramref name="handle"/> (currently inert).</summary>
    public VBResizableArrayValue(IBindingHandle handle, (int lBound, int uBound)[] dimensions, VBType? itemType = null)
        : base(handle, dimensions, itemType ?? VBVariantType.TypeInfo) { }
}
