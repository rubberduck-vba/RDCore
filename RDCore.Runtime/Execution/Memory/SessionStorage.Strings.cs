using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.Memory;

// the variable-length Strings of the session's memory: each a pointer to the characters of a BSTR (StringBlock), as MS-VBA lays them out (recorded in Excel,
// oracle\s17 S rows and grid-p3).
internal sealed partial class SessionStorage
{
    // the BSTRs, by the address each starts at and by where a String in it is held; the addresses of the variables that are variable-length Strings, whose
    // bytes are the address of their characters; and the bytes poked into a String's pointer that point at no BSTR yet.
    private readonly Dictionary<MemoryAddress, StringBlock> _stringBlocks = [];
    private readonly Dictionary<MemoryAddress, StringBlock> _stringOfHolder = [];
    private readonly HashSet<MemoryAddress> _stringVariables = [];
    private readonly Dictionary<MemoryAddress, byte[]> _pendingPointers = [];

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
            Detach(holder, known, free: true);
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
    /// Says that the <c>String</c> a holder of <paramref name="block"/> holds now is in it, at the same address, when it is as long as the one it was: the
    /// <c>Mid</c> statement writes the characters of its target in place (<strong>MS-VBA</strong>, recorded in Excel, oracle\s17 S07) - which every other
    /// holder of the block then holds too (grid-p3 Q01).
    /// </summary>
    public void KeepInPlace(StringBlock block)
    {
        if (!_stringBlocks.ContainsKey(block.Start))
        {
            return;
        }

        var previous = block.Value;
        foreach (var holder in block.Holders)
        {
            if (TryFindStringAt(holder, out var text) && !ReferenceEquals(text, previous) && block.TryRewrite(text))
            {
                Spread(block, previous);
                Generation++;
                return;
            }
        }
    }

    // what a block holds now is what every holder of it holds that held what it held; one that holds another String was assigned it, and is the block's no
    // more - which is freed when it is no one's (MS-VBA frees a String's BSTR when another is assigned to it).
    private void Spread(StringBlock block, IRuntimeValue previous)
    {
        foreach (var holder in block.Holders.ToList())
        {
            if (!TryFindStringAt(holder, out var held) || ReferenceEquals(held, block.Value))
            {
                continue;
            }

            if (!ReferenceEquals(held, previous) || !TryWriteStringAt(holder, block.Value))
            {
                Detach(holder, block, free: true);
            }
        }
    }

    // a holder lets go of its block; one that is no one's is freed unless a pointer let go of it, which MS-VBA leaves where it is (grid-p3 Q03).
    private void Detach(MemoryAddress holder, StringBlock block, bool free)
    {
        block.Holders.Remove(holder);
        if (_stringOfHolder.TryGetValue(holder, out var held) && ReferenceEquals(held, block))
        {
            _stringOfHolder.Remove(holder);
        }

        if (free && block.Holders.Count == 0 && _stringBlocks.Remove(block.Start))
        {
            allocator.TryDeallocate(block.Start, out _);
        }

        Generation++;
    }

    // a POKE into a BSTR: the String is what its length says of its characters now, at the same address, for every holder of it (oracle\s17 S12, grid-p3
    // L01). A length that goes on past the block is refused, as reading memory it is not.
    private bool TryPokeCharacters(StringBlock block, byte[] image)
    {
        var previous = block.Value;
        if (!block.TryWrite(image, out var changed))
        {
            return false;
        }

        if (changed is not null)
        {
            Spread(block, previous);
        }

        Generation++;
        return true;
    }

    // a POKE into the pointer a String is (grid-p3 Q01, Q03-Q05): 0 makes it vbNullString, and the address of the characters of a BSTR makes it the String of
    // that BSTR, which the two then hold - a change to the characters through either is the other's. What it pointed at is let go of, not freed. A pointer is
    // written a byte at a time, so one that points at no BSTR is kept as it is written, the String what it was until it does.
    private bool TryPokePointer(MemoryAddress holder, int within, byte value)
    {
        var bytes = StringHolders.PointerBytes(PointerAt(holder), pointerSize);
        bytes[within] = value;
        var pointer = pointerSize == sizeof(int) ? BitConverter.ToInt32(bytes) : BitConverter.ToInt64(bytes);
        var target = pointer == 0 ? null : BlockAt(pointer);
        if (pointer != 0 && target is null)
        {
            _pendingPointers[holder] = bytes;
            Generation++;
            return true;
        }

        _pendingPointers.Remove(holder);
        var was = _stringOfHolder.GetValueOrDefault(holder);
        if (was is not null && ReferenceEquals(was, target))
        {
            Generation++;
            return true;
        }

        if (was is not null)
        {
            Detach(holder, was, free: false);
        }

        if (!TryWriteStringAt(holder, target?.Value ?? new VBRuntimeValue<string>(null!)))
        {
            return false;
        }

        if (target is not null)
        {
            target.Holders.Add(holder);
            _stringOfHolder[holder] = target;
        }

        Generation++;
        return true;
    }

    private StringBlock? BlockAt(long characters)
    {
        foreach (var block in _stringBlocks.Values)
        {
            if (block.Characters.Value == characters)
            {
                return block;
            }
        }

        return null;
    }

    // what the bytes of the pointer a String is are: what was poked into it while it points at no BSTR, the address of its characters otherwise, 0 for
    // vbNullString.
    private long PointerAt(MemoryAddress holder)
        => _pendingPointers.TryGetValue(holder, out var pending)
            ? pointerSize == sizeof(int) ? BitConverter.ToInt32(pending) : BitConverter.ToInt64(pending)
            : TryGetStringPointer(holder, out var pointer) ? pointer : 0;

    // the pointer a variable-length String is that an address falls in: a variable's, an element's, a field's - and where in it.
    private bool TryFindStringSlot(MemoryAddress address, out MemoryAddress holder, out int within)
    {
        holder = default;
        within = 0;
        if ((_stringVariables.Count == 0 && _stringOfHolder.Count == 0 && _pendingPointers.Count == 0) || !allocator.TryFindBlock(address, out var found))
        {
            return false;
        }

        var offset = address.Value - found.Address.Value;
        var slot = -1;
        if (_stringVariables.Contains(found.Address))
        {
            slot = offset < pointerSize ? 0 : -1;
        }
        else if (_arrayBlocks.TryGetValue(found.Address, out var elements))
        {
            slot = elements.SlotOfStringAt(offset);
        }
        else if (_handleByAddress.TryGetValue(found.Address, out var handle) && RecordOf(handle) is { } record)
        {
            slot = StringHolders.SlotOfStringAt(record, offset, pointerSize);
        }

        if (slot < 0)
        {
            return false;
        }

        holder = new MemoryAddress(found.Address.Value + slot);
        within = offset - slot;
        return true;
    }

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

    // makes what holds the String at an address hold another.
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

    // the Strings held in what is freed, a variable, a record or an array's block, let go of their BSTRs, which are freed when they are no one's.
    private void FreeStringBlocksIn(MemoryAddress start, int size)
    {
        foreach (var holder in _pendingPointers.Keys.Where(holder => holder.Value >= start.Value && holder.Value < start.Value + size).ToList())
        {
            _pendingPointers.Remove(holder);
        }

        foreach (var (holder, block) in _stringOfHolder.Where(held => held.Key.Value >= start.Value && held.Key.Value < start.Value + size).ToList())
        {
            Detach(holder, block, free: true);
        }
    }
}
