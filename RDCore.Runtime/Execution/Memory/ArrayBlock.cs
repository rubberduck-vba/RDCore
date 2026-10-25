using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// The block of the session's memory the elements of an array are laid out in, side by side, as <strong>MS-VBA</strong> lays them out (recorded in Excel,
/// oracle\s17 V02-V12): each element <see cref="ElementWidth"/> bytes wide, in the order <see cref="VBArrayValue.IndexOf"/> numbers them - the first subscript
/// varying fastest.
/// </summary>
/// <remarks>
/// <para>
/// The elements stay in the array: the block is where they are, and its bytes are what they hold, read and written through them. An element that is a pointer
/// in MS-VBA - a variable-length <c>String</c>, an object, a <c>Variant</c>, an array - has no bytes of its own here yet (*Ptr plan P3, P4), and reads as zeros.
/// </para>
/// <para>
/// The block belongs to the array object, which it does not keep alive: an array that is <c>ReDim</c>'d, or assigned another, is another array object, and the
/// one it was is let go of with its block (<see cref="SessionStorage"/> frees the blocks of the arrays that are gone). Every <c>ReDim</c> moves the data in
/// MS-VBA as well, <c>Preserve</c> or not (V11).
/// </para>
/// </remarks>
internal sealed class ArrayBlock
{
    private readonly WeakReference<VBArrayValue> _array;
    private readonly int _pointerWidth;

    /// <param name="array">The array.</param>
    /// <param name="pointerWidth">The session's pointer width, which an element's width, and a record element's layout, depend on.</param>
    public ArrayBlock(VBArrayValue array, int pointerWidth)
    {
        _array = new WeakReference<VBArrayValue>(array);
        _pointerWidth = pointerWidth;
        ElementWidth = WidthOf(array, pointerWidth);
        Size = Math.Max(1, array.Length * ElementWidth);
    }

    /// <summary>How many bytes an element occupies.</summary>
    public int ElementWidth { get; }

    /// <summary>How many bytes the block is: every element's.</summary>
    public int Size { get; }

    /// <summary>The array whose elements the block holds, while it is not gone.</summary>
    public bool TryGetArray([NotNullWhen(true)] out VBArrayValue? array) => _array.TryGetTarget(out array);

    /// <summary>
    /// The block's bytes: each element's image at its offset.
    /// </summary>
    public byte[] Image()
    {
        var image = new byte[Size];
        if (!TryGetArray(out var array))
        {
            return image;
        }

        for (var index = 0; index < array.Length; index++)
        {
            if (array.ElementAt(index) is { } element
                && VBUserDefinedTypeImage.Encode(array.ItemType, element, ElementWidth, _pointerWidth) is { } bytes)
            {
                bytes.AsSpan(0, Math.Min(bytes.Length, ElementWidth)).CopyTo(image.AsSpan(index * ElementWidth));
            }
        }

        return image;
    }

    /// <summary>
    /// Writes back the element that the byte at <paramref name="offset"/> of a mutated <paramref name="image"/> is part of: what a <c>POKE</c> into the block
    /// changes. An element that has no bytes of its own keeps its value.
    /// </summary>
    /// <param name="image">The block's bytes, one of them changed.</param>
    /// <param name="offset">Where the change is.</param>
    /// <returns><see langword="false"/> when nothing could be written there.</returns>
    public bool Write(byte[] image, int offset)
    {
        var index = offset / ElementWidth;
        if (!TryGetArray(out var array) || index >= array.Length || !VBUserDefinedTypeImage.HasBytesOfItsOwn(array.ItemType))
        {
            return false;
        }

        var bytes = image.AsSpan(index * ElementWidth, ElementWidth);
        if (array.ItemType is VBUserDefinedType && array.ElementAt(index) is VBUserDefinedTypeValue record)
        {
            VBUserDefinedTypeImage.WriteInto(record, bytes, _pointerWidth);
            return true;
        }

        return array.TrySetElementAt(index, new ValueBindingHandle(VBUserDefinedTypeImage.Decode(array.ItemType, bytes).RuntimeValue));
    }

    /// <summary>
    /// The cell of the element whose first byte is at <paramref name="offset"/>, if there is one.
    /// </summary>
    public IBindingHandle? CellAt(int offset)
        => offset % ElementWidth == 0 && TryGetArray(out var array) ? array.CellAt(offset / ElementWidth) : null;

    // how many bytes an element occupies: as its type says, and a record as the elements' own layout says, a fixed-size array field being as wide as the
    // record's own.
    private static int WidthOf(VBArrayValue array, int pointerWidth)
        => array.ItemType is VBUserDefinedType && array.ElementAt(0) is VBUserDefinedTypeValue record
            ? VBUserDefinedTypeLayout.Of(record, pointerWidth).Size
            : Math.Max(1, VBUserDefinedTypeLayout.WidthOf(array.ItemType, pointerWidth));
}
