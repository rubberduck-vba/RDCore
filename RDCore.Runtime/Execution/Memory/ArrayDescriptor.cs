using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// The <c>SAFEARRAY</c> a dynamic array, and a <c>Variant</c> holding an array, is a pointer to in the session's memory, as <strong>MS-VBA</strong> lays one
/// out (recorded in Excel, oracle\s17 grid-p4): how many dimensions, its features, how wide an element is, how many locks it has, the address of its
/// elements, then each dimension's count and lower bound - the last dimension first.
/// </summary>
/// <remarks>
/// <para>
/// A descriptor is made for what holds the array - a field of a record, an element, a <c>Variant</c> - the first time its address is asked for. A dynamic
/// array's is the same for as long as it holds an array, <c>ReDim</c> or not (A06), and is freed when it holds none, after an <c>Erase</c>; a
/// <c>Variant</c>'s is the array's, and another array assigned to it has another (B11).
/// </para>
/// <para>
/// Its bytes are what the array is as they are read: the address of the elements is the block they are laid out in (<see cref="ArrayBlock"/>), its locks
/// the <c>For Each</c> statements and the calls that lock it (A07).
/// </para>
/// </remarks>
internal sealed class ArrayDescriptor(MemoryAddress start, MemoryAddress holder, int rank, VBArrayValue array)
{
    /// <summary>Where the descriptor begins: what the pointer a dynamic array is, is.</summary>
    public MemoryAddress Start { get; } = start;

    /// <summary>
    /// What holds the array: each address whose bytes are the pointer to the descriptor - the one it was made for, and any other given it (B12).
    /// </summary>
    public HashSet<MemoryAddress> Holders { get; } = [holder];

    /// <summary>How many dimensions the descriptor has room for.</summary>
    public int Rank { get; } = rank;

    /// <summary>The array the descriptor was made for, which a <c>Variant</c>'s is the descriptor of for as long as it holds it.</summary>
    public VBArrayValue Array { get; set; } = array;

    /// <summary>How many bytes a descriptor of <paramref name="rank"/> dimensions occupies.</summary>
    public static int SizeFor(int rank, int pointerWidth) => BoundsOffset(pointerWidth) + rank * 2 * sizeof(int);

    /// <summary>Where the dimensions' counts and lower bounds begin.</summary>
    public static int BoundsOffset(int pointerWidth) => pointerWidth == sizeof(long) ? 24 : 16;

    /// <summary>Where the address of the elements is.</summary>
    public static int DataOffset(int pointerWidth) => pointerWidth == sizeof(long) ? 16 : 12;

    /// <summary>
    /// The descriptor's bytes for <paramref name="array"/>.
    /// </summary>
    /// <param name="array">The array.</param>
    /// <param name="elements">The address of its elements.</param>
    /// <param name="locks">How many times it is locked.</param>
    /// <param name="pointerWidth">The session's pointer width.</param>
    public byte[] Image(VBArrayValue array, long elements, int locks, int pointerWidth)
    {
        var image = new byte[SizeFor(Rank, pointerWidth)];
        BitConverter.TryWriteBytes(image.AsSpan(0, 2), (short)array.Rank);
        BitConverter.TryWriteBytes(image.AsSpan(2, 2), Features(array.ItemType));
        BitConverter.TryWriteBytes(image.AsSpan(4, 4), ElementWidth(array, pointerWidth));
        BitConverter.TryWriteBytes(image.AsSpan(8, 4), locks);
        StringHolders.PointerBytes(elements, pointerWidth).CopyTo(image, DataOffset(pointerWidth));
        for (var dimension = 0; dimension < Math.Min(Rank, array.Rank); dimension++)
        {
            // the last dimension first (A02).
            var bound = array.Dimensions[array.Rank - 1 - dimension];
            var at = BoundsOffset(pointerWidth) + dimension * 2 * sizeof(int);
            BitConverter.TryWriteBytes(image.AsSpan(at, 4), bound.Length);
            BitConverter.TryWriteBytes(image.AsSpan(at + 4, 4), bound.LowerBound);
        }

        return image;
    }

    /// <summary>
    /// The dimensions <paramref name="image"/> says <paramref name="array"/> has, when they are the only bytes of it that a <c>POKE</c> changed and they do
    /// not make the array more elements than it has (A11, A12): a lower bound moves the elements to other subscripts, a count makes the array shorter.
    /// </summary>
    /// <returns><see langword="null"/> when the change is to anything else, or makes the array more elements than it has, which would be memory it is not.</returns>
    public System.Collections.Immutable.ImmutableArray<VBArrayValue.VBArrayDimension>? Reshaped(VBArrayValue array, byte[] image, byte[] was, int pointerWidth)
    {
        var bounds = BoundsOffset(pointerWidth);
        if (!image.AsSpan(0, bounds).SequenceEqual(was.AsSpan(0, bounds)) || array.Rank != Rank)
        {
            return null;
        }

        var dimensions = new VBArrayValue.VBArrayDimension[Rank];
        var count = 1L;
        for (var dimension = 0; dimension < Rank; dimension++)
        {
            var at = bounds + dimension * 2 * sizeof(int);
            var length = BitConverter.ToInt32(image, at);
            var lower = BitConverter.ToInt32(image, at + 4);
            if (length < 1)
            {
                return null;
            }

            count *= length;
            dimensions[Rank - 1 - dimension] = new VBArrayValue.VBArrayDimension(lower, lower + length - 1);
        }

        return count <= array.Length ? [.. dimensions] : null;
    }

    // what MS-VBA says of the elements (FADF_*, B01-B10): a String's are BSTRs, a Variant's VARIANTs, an object's interfaces - IUnknown's or IDispatch's -
    // and every one but a record's says its VarType.
    private static short Features(VBType itemType) => itemType switch
    {
        VBUserDefinedType => 0,
        // the order matters: a fixed-length String is a VBStringType, and is its characters.
        VBFixedStringType => 0x80,
        VBStringType => 0x180,
        VBVariantType => 0x880,
        VBClassType { Symbol.Name: "IUnknown" } => 0x240,
        VBObjectType or VBClassType => 0x440,
        _ => 0x80,
    };

    // how wide an element is: a record's as the elements' own layout says (ArrayBlock).
    private static int ElementWidth(VBArrayValue array, int pointerWidth)
        => array.ItemType is VBUserDefinedType && array.ElementAt(0) is VBUserDefinedTypeValue record
            ? VBUserDefinedTypeLayout.Of(record, pointerWidth).Size
            : Math.Max(1, VBUserDefinedTypeLayout.WidthOf(array.ItemType, pointerWidth));
}
