using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// The <see cref="ISessionStorage"/>: binds a value's <see cref="IBindingHandle"/> to the address
/// reserved for it through an <see cref="ISessionMemoryAllocator"/>.
/// </summary>
/// <param name="allocator">The session's memory allocator; reserves address space only, never values.</param>
/// <param name="pointerSize">The size of an address in the session's memory, in bytes: what a variable that holds nothing is given.</param>
internal sealed class SessionStorage(ISessionMemoryAllocator allocator, int pointerSize = sizeof(long)) : ISessionStorage
{
    private readonly Dictionary<MemoryAddress, IBindingHandle> _handleByAddress = [];
    private readonly Dictionary<MemoryAddress, byte[]> _bytesByAddress = [];

    // the blocks the elements of arrays are laid out in (ArrayBlock), by the address each starts at, and by the array: an array that is gone is not kept
    // alive by its block, which is freed the next time a block is made.
    private readonly Dictionary<MemoryAddress, ArrayBlock> _arrayBlocks = [];
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<SDK.Model.Values.Intrinsic.VBArrayValue, System.Runtime.CompilerServices.StrongBox<MemoryAddress>> _blockOfArray = new();

    /// <summary>
    /// The address of the element at <paramref name="flatIndex"/> of <paramref name="array"/>, in the order <see cref="SDK.Model.Values.Intrinsic.VBArrayValue.IndexOf"/>
    /// numbers them: in the block the array's elements are laid out in side by side (<see cref="ArrayBlock"/>), made the first time the address of one of
    /// them is asked for. Every element of the array is at the start of the block plus its index times the width of an element (<c>VarPtr</c>).
    /// </summary>
    /// <returns><see langword="false"/> when there is no element at the index, or no room for the block.</returns>
    public bool TryAddressOfElement(SDK.Model.Values.Intrinsic.VBArrayValue array, int flatIndex, out MemoryAddress address)
    {
        address = default;
        if (flatIndex < 0 || flatIndex >= array.Length)
        {
            return false;
        }

        if (!_blockOfArray.TryGetValue(array, out var start))
        {
            var made = new ArrayBlock(array, pointerSize);
            if (!allocator.TryAllocate(made.Size, out var at))
            {
                return false;
            }

            // the blocks of the arrays that are gone are freed once this one is made, so that it is never where one of them was: an array ReDim'd is
            // elsewhere (oracle\s17 V11), whenever the one it was is collected.
            FreeBlocksOfArraysThatAreGone();
            _arrayBlocks[at] = made;
            _blockOfArray.Add(array, start = new System.Runtime.CompilerServices.StrongBox<MemoryAddress>(at));
            Generation++;
        }

        address = new MemoryAddress(start.Value.Value + flatIndex * _arrayBlocks[start.Value].ElementWidth);
        return true;
    }

    private void FreeBlocksOfArraysThatAreGone()
    {
        foreach (var (at, _) in _arrayBlocks.Where(block => !block.Value.TryGetArray(out _)).ToList())
        {
            _arrayBlocks.Remove(at);
            allocator.TryDeallocate(at, out _);
            Generation++;
        }
    }

    // the block of an array that an address falls in, and where in it.
    private bool TryFindArrayBlock(MemoryAddress address, [NotNullWhen(true)] out ArrayBlock? block, out int offset)
    {
        block = null;
        offset = 0;
        if (_arrayBlocks.Count == 0 || !allocator.TryFindBlock(address, out var found) || !_arrayBlocks.TryGetValue(found.Address, out block))
        {
            return false;
        }

        offset = address.Value - found.Address.Value;
        return true;
    }

    /// <summary>
    /// How many times what is bound at an address has changed: an allocation, a deallocation, a rebinding (a <c>POKE</c>). A binding read through an
    /// address is the binding there for as long as this has not changed (IR lowering S1.5).
    /// </summary>
    public int Generation { get; private set; }

    public bool TryAllocate(int size, IBindingHandle handle, out MemoryAddress address)
    {
        // a value whose Size is 0 (vbNullString, Null, an array with no elements yet, an empty ParamArray) is held by a variable all the same, and a
        // variable has an address a program can take (VarPtr): MS-VBA's String or dynamic array is a pointer that is 0, which is a pointer's size. A record
        // occupies its layout for the session's pointer width, which its own Size is not (VBUserDefinedTypeLayout.DefaultPointerWidth).
        if (handle is ValueBindingHandle { Value: SDK.Model.Values.Runtime.VBRuntimeValue<SDK.Model.Values.Runtime.VBRuntimeUserDefinedTypeValue> { StoredValue.UserDefinedType: var record } })
        {
            size = Math.Max(size, SDK.Model.Types.VBUserDefinedTypeLayout.Of(record, pointerSize).Size);
        }

        if (!allocator.TryAllocate(size <= 0 ? pointerSize : size, out address))
        {
            return false;
        }

        _handleByAddress[address] = handle;
        Bind(handle, address);
        Generation++;
        return true;
    }

    // a cell knows the address it is bound at, so that a second name for its variable - a ByRef parameter given the cell (Frames.CellReference) - finds the
    // address the variable has, whichever of them was asked for it first.
    private static void Bind(IBindingHandle handle, MemoryAddress address)
    {
        if (handle is Frames.ScalarCell cell)
        {
            cell.Address = address;
        }
    }

    private void Unbind(MemoryAddress address)
    {
        if (_handleByAddress.TryGetValue(address, out var handle) && handle is Frames.ScalarCell { Address: { } bound } cell && bound == address)
        {
            cell.Address = null;
        }
    }

    public bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? handle)
    {
        if (_handleByAddress.TryGetValue(address, out handle))
        {
            return true;
        }

        // an element of an array, at its place in the array's block.
        handle = TryFindArrayBlock(address, out var block, out var offset) ? block.CellAt(offset) : null;
        return handle is not null;
    }

    public bool TryDeallocate(MemoryAddress address)
    {
        Generation++;
        Unbind(address);
        return (_handleByAddress.Remove(address) || _bytesByAddress.Remove(address) || _arrayBlocks.Remove(address)) && allocator.TryDeallocate(address, out _);
    }

    public bool TryRebind(MemoryAddress address, IBindingHandle handle)
    {
        if (!_handleByAddress.ContainsKey(address))
        {
            return false;
        }

        Unbind(address);
        _handleByAddress[address] = handle;
        Bind(handle, address);
        Generation++;
        return true;
    }

    public bool TryAllocateBytes(int size, out MemoryAddress address)
    {
        if (!allocator.TryAllocate(size, out address))
        {
            return false;
        }

        _bytesByAddress[address] = new byte[size];
        return true;
    }

    public bool TryReadBytes(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out byte[]? bytes)
    {
        if (!_bytesByAddress.TryGetValue(address, out var stored))
        {
            bytes = null;
            return false;
        }

        bytes = (byte[])stored.Clone();
        return true;
    }

    public bool TryWriteBytes(MemoryAddress address, ReadOnlySpan<byte> bytes)
    {
        if (!_bytesByAddress.ContainsKey(address))
        {
            return false;
        }

        _bytesByAddress[address] = bytes.ToArray();
        return true;
    }

    public bool TryPeek(MemoryAddress address, out byte value)
    {
        value = 0;
        if (!TryFindImage(address, out var image, out var offset, out _))
        {
            return false;
        }

        // a block can be bigger than the image of what is bound in it (a declared size that rounds up,
        // a String shorter than the space reserved for it); those bytes read as zero rather than as a
        // failure, which is what reading further into an allocation than its value reaches does.
        value = offset < image.Length ? image[offset] : (byte)0;
        return true;
    }

    public bool TryPoke(MemoryAddress address, byte value)
    {
        if (!TryFindImage(address, out var image, out var offset, out var block) || offset >= image.Length)
        {
            return false;
        }

        image[offset] = value;

        // a raw byte buffer takes the mutated image as it is. A bound value is rebuilt from it, and
        // this is where being unchecked bites: the image is whatever the caller just made it, so the
        // value becomes whatever that spells - which is the entire point of a POKE.
        if (_bytesByAddress.ContainsKey(block))
        {
            _bytesByAddress[block] = image;
            return true;
        }

        // the element of an array the byte is part of, which the array holds.
        if (_arrayBlocks.TryGetValue(block, out var elements))
        {
            Generation++;
            return elements.Write(image, offset);
        }

        return _handleByAddress.TryGetValue(block, out var handle)
            && CanRead(handle)
            && RuntimeValueBytes.From(handle.Value, image, pointerSize) is { } rebuilt
            && TryRebind(block, new ValueBindingHandle(rebuilt));
    }

    /// <summary>
    /// The byte image of whatever is allocated at <paramref name="address"/>, the offset of that
    /// address within it, and the address the allocation starts at.
    /// </summary>
    private bool TryFindImage(MemoryAddress address, out byte[] image, out int offset, out MemoryAddress block)
    {
        image = [];
        offset = 0;
        block = default;

        if (!allocator.TryFindBlock(address, out var found))
        {
            return false;
        }

        block = found.Address;
        offset = address.Value - found.Address.Value;

        if (_bytesByAddress.TryGetValue(block, out var bytes))
        {
            image = bytes;
            return true;
        }

        if (_arrayBlocks.TryGetValue(block, out var elements))
        {
            image = elements.Image();
            return true;
        }

        if (_handleByAddress.TryGetValue(block, out var handle) && CanRead(handle)
            && RuntimeValueBytes.Of(handle.Value, pointerSize) is { } valueImage)
        {
            image = valueImage;
            return true;
        }

        return false;
    }

    // a handle that resolves its value lazily or through a reference has nothing to read without a
    // resolver, and a byte-level read has no business driving one.
    private static bool CanRead(IBindingHandle handle)
        => handle.BindingCapabilities.HasFlag(BindingCapabilities.GetValue);
}
