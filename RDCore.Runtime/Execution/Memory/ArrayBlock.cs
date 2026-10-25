using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// The block of the session's memory the elements of an array are laid out in, side by side, as <strong>MS-VBA</strong> lays them out (recorded in Excel,
/// oracle\s17 V02-V12): each element <see cref="ElementWidth"/> bytes wide, in the order <see cref="VBArrayValue.IndexOf"/> numbers them - the first subscript
/// varying fastest.
/// </summary>
/// <remarks>
/// <para>
/// The elements stay in the array: the block is where they are, and its bytes are what they hold, read and written through them. A variable-length
/// <c>String</c> element is the address of its characters (<see cref="StringBlock"/>), an object the address of its interface (<see cref="ObjectHeader"/>), a
/// <c>Variant</c> its <c>VARIANT</c>. 🚧 An array element of an array of arrays is the address of a <c>SAFEARRAY</c> in MS-VBA, which nothing makes here: zeros.
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
    /// <param name="hostBytes">The bytes the session lays out for what is at an offset of the block, an element or a field of a record element, that is no
    /// bytes of its own (<see cref="VBUserDefinedTypeImage.IsLaidOutByTheHost"/>): a <c>String</c>'s pointer, an object's, a <c>VARIANT</c>.</param>
    public byte[] Image(Func<int, byte[]?> hostBytes)
    {
        var image = new byte[Size];
        if (!TryGetArray(out var array))
        {
            return image;
        }

        for (var index = 0; index < array.Length; index++)
        {
            var at = index * ElementWidth;
            if (VBUserDefinedTypeImage.IsLaidOutByTheHost(array.ItemType))
            {
                if (hostBytes(at) is { } laidOut)
                {
                    laidOut.AsSpan(0, Math.Min(laidOut.Length, ElementWidth)).CopyTo(image.AsSpan(at));
                }

                continue;
            }

            if (array.ItemType is VBUserDefinedType && array.ElementAt(index) is VBUserDefinedTypeValue record)
            {
                var fields = VBUserDefinedTypeImage.Of(record, _pointerWidth, offset => hostBytes(at + offset));
                fields.AsSpan(0, Math.Min(fields.Length, ElementWidth)).CopyTo(image.AsSpan(at));
                continue;
            }

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
    /// The variable-length <c>String</c> held at <paramref name="offset"/>: by an element of a <c>String</c> or a <c>Variant</c> array that begins there, or a
    /// field of a record element.
    /// </summary>
    public bool TryFindStringAt(int offset, out IRuntimeValue text)
    {
        text = null!;
        var index = offset / ElementWidth;
        var within = offset % ElementWidth;
        if (!TryGetArray(out var array) || index >= array.Length)
        {
            return false;
        }

        if (array.ItemType is VBUserDefinedType)
        {
            return array.ElementAt(index) is VBUserDefinedTypeValue record
                && StringHolders.TryFindField(record, within, _pointerWidth, out var field)
                && StringHolders.TryStringOf(field, out text);
        }

        return within == 0 && array.CellAt(index) is { } cell && cell.BindingCapabilities.HasFlag(BindingCapabilities.GetValue)
            && StringHolders.TryStringOf(cell.Value, out text);
    }

    /// <summary>
    /// Where the element the session lays out (<see cref="VBUserDefinedTypeImage.IsLaidOutByTheHost"/>) begins that <paramref name="offset"/> falls in - or a
    /// field of a record element - and its declared type; <c>-1</c> when the offset falls in none.
    /// </summary>
    public int SlotAt(int offset, out VBType type)
    {
        type = VBUnknownType.TypeInfo;
        var index = offset / ElementWidth;
        if (!TryGetArray(out var array) || index >= array.Length)
        {
            return -1;
        }

        var start = index * ElementWidth;
        if (array.ItemType is VBUserDefinedType)
        {
            return array.ElementAt(index) is VBUserDefinedTypeValue record && StringHolders.SlotAt(record, offset - start, _pointerWidth, out type) is >= 0 and var slot
                ? start + slot
                : -1;
        }

        if (!VBUserDefinedTypeImage.IsLaidOutByTheHost(array.ItemType))
        {
            return -1;
        }

        type = array.ItemType;
        return start;
    }

    /// <summary>
    /// What is held at <paramref name="offset"/>, as a variable's cell holds it, by an element that begins there or a field of a record element the session
    /// lays out.
    /// </summary>
    public bool TryRawAt(int offset, out IRuntimeValue raw)
    {
        raw = null!;
        var index = offset / ElementWidth;
        var within = offset % ElementWidth;
        if (!TryGetArray(out var array) || index >= array.Length)
        {
            return false;
        }

        if (array.ItemType is VBUserDefinedType)
        {
            return array.ElementAt(index) is VBUserDefinedTypeValue record
                && StringHolders.TryFindField(record, within, _pointerWidth, out var field)
                && StringHolders.TryRawOf(field, out raw);
        }

        if (within != 0 || array.CellAt(index) is not { } cell || !cell.BindingCapabilities.HasFlag(BindingCapabilities.GetValue))
        {
            return false;
        }

        raw = cell.Value;
        return true;
    }

    /// <summary>
    /// Makes what is held at <paramref name="offset"/> hold <paramref name="raw"/> (<see cref="TryRawAt"/>).
    /// </summary>
    public bool TryWriteRawAt(int offset, IRuntimeValue raw)
    {
        var index = offset / ElementWidth;
        var within = offset % ElementWidth;
        if (!TryGetArray(out var array) || index >= array.Length)
        {
            return false;
        }

        if (array.ItemType is VBUserDefinedType)
        {
            return array.ElementAt(index) is VBUserDefinedTypeValue record
                && StringHolders.TryFindField(record, within, _pointerWidth, out var field)
                && StringHolders.TryWriteRaw(field, raw);
        }

        return within == 0 && array.TrySetElementAt(index, new ValueBindingHandle(raw));
    }

    /// <summary>
    /// Makes what holds the <c>String</c> at <paramref name="offset"/> hold <paramref name="text"/> (<see cref="TryFindStringAt"/>).
    /// </summary>
    public bool TryWriteStringAt(int offset, IRuntimeValue text)
    {
        var index = offset / ElementWidth;
        var within = offset % ElementWidth;
        if (!TryGetArray(out var array) || index >= array.Length)
        {
            return false;
        }

        if (array.ItemType is VBUserDefinedType)
        {
            return array.ElementAt(index) is VBUserDefinedTypeValue record
                && StringHolders.TryFindField(record, within, _pointerWidth, out var field)
                && StringHolders.TryWrite(field, text);
        }

        return within == 0 && array.CellAt(index) is { } cell
            && array.TrySetElementAt(index, new ValueBindingHandle(StringHolders.Replacing(cell.Value, text)));
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
