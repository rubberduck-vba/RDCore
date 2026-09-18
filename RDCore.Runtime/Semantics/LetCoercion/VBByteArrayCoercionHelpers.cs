using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// Shared mechanics for MS-VBAL 5.5.1.2.6 (Let-coercion to and from a resizable Byte array), used by
/// both directions of the Byte()/String coercion (<see cref="VBResizableByteArrayLetCoercionRuntimeSemantics"/>
/// and <see cref="VBStringLetCoercionRuntimeSemantics"/>).
/// </summary>
internal static class VBByteArrayCoercionHelpers
{
    /// <summary>
    /// Walks every subscript tuple of a (possibly multi-dimensional) array's bounds in column-major
    /// order (first subscript varies fastest, matching <see cref="VBArrayValue"/>'s own documented
    /// cell layout).
    /// </summary>
    public static IEnumerable<int[]> EnumerateSubscripts(ImmutableArray<VBArrayValue.VBArrayDimension> dimensions)
    {
        if (dimensions.Length == 0)
        {
            yield break;
        }

        var subscripts = dimensions.Select(d => d.LowerBound).ToArray();
        var total = dimensions.Aggregate(1, (count, d) => count * d.Length);
        for (var i = 0; i < total; i++)
        {
            yield return [.. subscripts];
            for (var d = 0; d < dimensions.Length; d++)
            {
                if (++subscripts[d] <= dimensions[d].UpperBound)
                {
                    break;
                }
                subscripts[d] = dimensions[d].LowerBound;
            }
        }
    }

    /// <summary>
    /// Reads every cell of <paramref name="byteArraySource"/> (in the same column-major order as
    /// <see cref="EnumerateSubscripts"/>) into a flat <c>byte[]</c>.
    /// </summary>
    public static byte[] Flatten(VBArrayValue byteArraySource)
    {
        var bytes = new byte[byteArraySource.Length];
        var i = 0;
        foreach (var subscripts in EnumerateSubscripts(byteArraySource.Dimensions))
        {
            bytes[i++] = ((VBByteValue)byteArraySource[subscripts]!).Value;
        }
        return bytes;
    }
}
