using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
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
        foreach (var (at, block) in _arrayBlocks.Where(block => !block.Value.TryGetArray(out _)).ToList())
        {
            FreeStringBlocksIn(at, block.Size);
            _arrayBlocks.Remove(at);
            allocator.TryDeallocate(at, out _);
            Generation++;
        }
    }

    // the BSTRs of the variable-length Strings held in the session's memory (StringBlock), by the address each starts at and by where its String is held; and
    // the addresses of the variables that are variable-length Strings, whose bytes are the address of their characters.
    private readonly Dictionary<MemoryAddress, StringBlock> _stringBlocks = [];
    private readonly Dictionary<MemoryAddress, StringBlock> _stringOfHolder = [];
    private readonly HashSet<MemoryAddress> _stringVariables = [];

    /// <summary>
    /// Says that the variable at <paramref name="address"/> is a variable-length <c>String</c>: its bytes are a pointer to its characters, as
    /// <strong>MS-VBA</strong>'s are (recorded in Excel, oracle\s17 S04), which nothing else here can tell from a fixed-length one's, which are its
    /// characters. What gives the address of a variable away (<c>VarPtr</c>) says so of the ones it gives.
    /// </summary>
    public void HoldsString(MemoryAddress address)
    {
        if (_handleByAddress.ContainsKey(address))
        {
            _stringVariables.Add(address);
        }
    }

    /// <summary>
    /// The address of the characters of the variable-length <c>String</c> held at <paramref name="holder"/> - a variable, an element of an array, a field of a
    /// record, a <c>Variant</c> holding one - in the <c>BSTR</c> it is in (<see cref="StringBlock"/>), made the first time it is asked for; <c>0</c> for
    /// <c>vbNullString</c>. It is what <c>StrPtr</c> is, and the same for as long as the holder holds that <c>String</c>.
    /// </summary>
    /// <returns><see langword="false"/> when nothing at the address holds a <c>String</c>, or there is no room for its block.</returns>
    public bool TryGetStringPointer(MemoryAddress holder, out long pointer)
    {
        pointer = 0;
        if (!TryFindStringAt(holder, out var text))
        {
            return false;
        }

        if (_handleByAddress.TryGetValue(holder, out var variable) && variable.Value is not VBRuntimeVariantValue)
        {
            _stringVariables.Add(holder);
        }

        if (_stringOfHolder.TryGetValue(holder, out var known) && ReferenceEquals(known.Value, text))
        {
            pointer = known.Characters.Value;
            return true;
        }

        // the String it was is not held any more, and MS-VBA frees its BSTR when another is assigned - once the other is made, so the two are never at one
        // address (oracle\s17 S02).
        var made = (StringBlock?)null;
        if (text.BoxedValue is not null)
        {
            if (!allocator.TryAllocate(StringBlock.SizeFor(VBRuntimeByteStringValue.BytesOf(text).Length), out var start))
            {
                return false;
            }

            made = new StringBlock(start, holder, text);
        }

        if (known is not null)
        {
            FreeStringBlock(known);
        }

        if (made is null)
        {
            return true;
        }

        _stringBlocks[made.Start] = made;
        _stringOfHolder[holder] = made;
        Generation++;
        pointer = made.Characters.Value;
        return true;
    }

    /// <summary>
    /// The <c>BSTR</c> <paramref name="text"/>, the very <c>String</c>, is in, if it is in one: what the <c>Mid</c> statement writes the characters of in place.
    /// </summary>
    public StringBlock? BlockOf(IRuntimeValue text)
    {
        foreach (var block in _stringBlocks.Values)
        {
            if (ReferenceEquals(block.Value, text))
            {
                return block;
            }
        }

        return null;
    }

    /// <summary>
    /// Says that the <c>String</c> the holder of <paramref name="block"/> holds now is in it, at the same address, when it is as long as the one it was: the
    /// <c>Mid</c> statement writes the characters of its target in place (<strong>MS-VBA</strong>, recorded in Excel, oracle\s17 S07).
    /// </summary>
    public void KeepInPlace(StringBlock block)
    {
        if (_stringBlocks.ContainsKey(block.Start) && TryFindStringAt(block.Holder, out var text)
            && VBRuntimeByteStringValue.BytesOf(text).Length == VBRuntimeByteStringValue.BytesOf(block.Value).Length)
        {
            block.Value = text;
            Generation++;
        }
    }

    // the address of the characters of the String at an address, which is what its bytes are; 0 when there is none, as for vbNullString.
    private long StringPointerAt(MemoryAddress holder) => TryGetStringPointer(holder, out var pointer) ? pointer : 0;

    private bool TryFindStringAt(MemoryAddress holder, out IRuntimeValue text)
    {
        text = null!;
        if (_handleByAddress.TryGetValue(holder, out var handle))
        {
            return CanRead(handle) && StringHolders.TryStringOf(handle.Value, out text);
        }

        if (!allocator.TryFindBlock(holder, out var found))
        {
            return false;
        }

        var offset = holder.Value - found.Address.Value;
        if (_arrayBlocks.TryGetValue(found.Address, out var elements))
        {
            return elements.TryFindStringAt(offset, out text);
        }

        return _handleByAddress.TryGetValue(found.Address, out var outer) && CanRead(outer) && RecordOf(outer) is { } record
            && StringHolders.TryFindField(record, offset, pointerSize, out var field)
            && StringHolders.TryStringOf(field, out text);
    }

    // makes what holds the String at an address hold another: where a POKE into its characters leaves them.
    private bool TryWriteStringAt(MemoryAddress holder, IRuntimeValue text)
    {
        if (_handleByAddress.TryGetValue(holder, out var handle))
        {
            if (handle is not ValueBindingHandle cell)
            {
                return TryRebind(holder, new ValueBindingHandle(StringHolders.Replacing(handle.Value, text)));
            }

            cell.Store(StringHolders.Replacing(cell.Value, text), isKnown: true);
            Generation++;
            return true;
        }

        if (!allocator.TryFindBlock(holder, out var found))
        {
            return false;
        }

        var offset = holder.Value - found.Address.Value;
        if (_arrayBlocks.TryGetValue(found.Address, out var elements))
        {
            Generation++;
            return elements.TryWriteStringAt(offset, text);
        }

        Generation++;
        return _handleByAddress.TryGetValue(found.Address, out var outer) && RecordOf(outer) is { } record
            && StringHolders.TryFindField(record, offset, pointerSize, out var field)
            && StringHolders.TryWrite(field, text);
    }

    private static SDK.Model.Values.Intrinsic.VBUserDefinedTypeValue? RecordOf(IBindingHandle handle)
        => handle is ValueBindingHandle { Value: SDK.Model.Values.Runtime.VBRuntimeValue<SDK.Model.Values.Runtime.VBRuntimeUserDefinedTypeValue> { StoredValue.UserDefinedType: var record } }
            ? record
            : null;

    private void FreeStringBlock(StringBlock block)
    {
        _stringBlocks.Remove(block.Start);
        _stringOfHolder.Remove(block.Holder);
        allocator.TryDeallocate(block.Start, out _);
        Generation++;
    }

    // the BSTRs of the Strings held in what is freed, a variable, a record or an array's block, are freed with it.
    private void FreeStringBlocksIn(MemoryAddress start, int size)
    {
        if (_stringOfHolder.Count == 0)
        {
            return;
        }

        foreach (var block in _stringOfHolder.Values.Where(block => block.Holder.Value >= start.Value && block.Holder.Value < start.Value + size).ToList())
        {
            FreeStringBlock(block);
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
        if (allocator.TryFindBlock(address, out var freed) && freed.Address == address)
        {
            FreeStringBlocksIn(address, freed.Size);
        }

        _stringVariables.Remove(address);
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

        // the address a String variable holds: what it would point at is no String the session has (🚧 *Ptr phase B, a pointer copied into it).
        if (_stringVariables.Contains(block))
        {
            return false;
        }

        image[offset] = value;

        // the characters of a String: the String it is held as is what they spell now, at the same address (oracle\s17 S12).
        if (_stringBlocks.TryGetValue(block, out var characters))
        {
            if (StringBlock.Spelled(image, offset) is not { } spelled || !TryFindStringAt(characters.Holder, out var held)
                || !ReferenceEquals(held, characters.Value) || !TryWriteStringAt(characters.Holder, spelled))
            {
                return false;
            }

            if (TryFindStringAt(characters.Holder, out var written))
            {
                characters.Value = written;
            }

            Generation++;
            return true;
        }

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
            var start = block;
            image = elements.Image(at => StringPointerAt(new MemoryAddress(start.Value + at)));
            return true;
        }

        if (_stringBlocks.TryGetValue(block, out var characters))
        {
            image = characters.Image();
            return true;
        }

        if (!_handleByAddress.TryGetValue(block, out var handle) || !CanRead(handle))
        {
            return false;
        }

        // a String variable is the address of its characters; a record its fields, a String field the address of its characters too.
        if (_stringVariables.Contains(block))
        {
            image = StringHolders.PointerBytes(StringPointerAt(block), pointerSize);
            return true;
        }

        if (RecordOf(handle) is { } record)
        {
            var start = block;
            image = SDK.Model.Types.VBUserDefinedTypeImage.Of(record, pointerSize, at => StringPointerAt(new MemoryAddress(start.Value + at)));
            return true;
        }

        if (RuntimeValueBytes.Of(handle.Value, pointerSize) is { } valueImage)
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
