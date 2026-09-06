using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// A value representing a fixed-size array.
/// </summary>
public sealed record class VBFixedSizeArrayValue : VBArrayValue
{
    /// <summary>
    /// Creates a new fixed-size array with the specified declared dimensions.
    /// </summary>
    /// <param name="dimensions">The lower and upper bound of each dimension.</param>
    /// <param name="itemType">The element type; <see cref="VBVariantType"/> when unspecified.</param>
    public VBFixedSizeArrayValue((int lBound, int uBound)[] dimensions, VBType? itemType = null)
        : base(dimensions, itemType ?? VBVariantType.TypeInfo) { }

    /// <summary>Creates a fixed-size array bound to <paramref name="handle"/> (currently inert).</summary>
    public VBFixedSizeArrayValue(IBindingHandle handle, (int lBound, int uBound)[] dimensions, VBType? itemType = null)
        : base(handle, dimensions, itemType ?? VBVariantType.TypeInfo) { }
}
