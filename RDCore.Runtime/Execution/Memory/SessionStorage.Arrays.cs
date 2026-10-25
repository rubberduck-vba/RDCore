using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.Memory;

// the dynamic arrays of the session's memory: each a pointer to a SAFEARRAY (ArrayDescriptor), as MS-VBA lays them out (recorded in Excel, oracle\s17 grid-p4,
// grid-p4b).
internal sealed partial class SessionStorage
{
    // the descriptors, by the address each starts at and by what holds an array described by it.
    private readonly Dictionary<MemoryAddress, ArrayDescriptor> _descriptors = [];
    private readonly Dictionary<MemoryAddress, ArrayDescriptor> _descriptorOf = [];

    /// <summary>
    /// How many times an array is locked - by a <c>For Each</c> over it, a call it is passed to an element of (<c>ICallStackFrame.LocksArray</c>): what its
    /// descriptor's locks are (A07). The session composes it.
    /// </summary>
    public Func<VBArrayValue, int>? LocksOf { get; set; }

    /// <summary>
    /// The address of the <c>SAFEARRAY</c> describing the array held at <paramref name="holder"/> - a dynamic array field of a record, an element, a
    /// <c>Variant</c> holding an array (<see cref="ArrayDescriptor"/>) - made the first time it is asked for; <c>0</c> for an array that has no dimensions.
    /// </summary>
    /// <returns><see langword="false"/> when nothing at the address holds an array, or there is no room for its descriptor.</returns>
    public bool TryGetArrayPointer(MemoryAddress holder, out long pointer)
    {
        pointer = 0;
        if (!TryFindArrayAt(holder, out var array, out var inVariant))
        {
            return false;
        }

        var known = _descriptorOf.GetValueOrDefault(holder);
        if (array is not { IsInitialized: true })
        {
            if (known is not null)
            {
                DetachDescriptor(holder, known, free: true);
            }

            return true;
        }

        // a dynamic array's is the same for as long as it holds an array, ReDim'd or not (A06); a Variant's is the array's, and another is another's (B11).
        if (known is not null && known.Rank == array.Rank && (!inVariant || ReferenceEquals(known.Array, array)))
        {
            known.Array = array;
            pointer = known.Start.Value;
            return true;
        }

        if (!allocator.TryAllocate(ArrayDescriptor.SizeFor(array.Rank, pointerSize), out var start))
        {
            return false;
        }

        if (known is not null)
        {
            DetachDescriptor(holder, known, free: true);
        }

        var made = new ArrayDescriptor(start, holder, array.Rank, array);
        _descriptors[start] = made;
        _descriptorOf[holder] = made;
        Generation++;
        pointer = start.Value;
        return true;
    }

    // what the bytes of the pointer a dynamic array is are: what was poked into it while it points at no descriptor, the descriptor's address otherwise.
    private long ArrayPointerAt(MemoryAddress holder)
        => _pendingPointers.TryGetValue(holder, out var pending)
            ? pointerSize == sizeof(int) ? BitConverter.ToInt32(pending) : BitConverter.ToInt64(pending)
            : TryGetArrayPointer(holder, out var pointer) ? pointer : 0;

    // a holder lets go of a descriptor; one that is no one's is freed unless a pointer let go of it, which MS-VBA leaves where it is.
    private void DetachDescriptor(MemoryAddress holder, ArrayDescriptor descriptor, bool free)
    {
        descriptor.Holders.Remove(holder);
        if (_descriptorOf.TryGetValue(holder, out var held) && ReferenceEquals(held, descriptor))
        {
            _descriptorOf.Remove(holder);
        }

        if (free && descriptor.Holders.Count == 0 && _descriptors.Remove(descriptor.Start))
        {
            allocator.TryDeallocate(descriptor.Start, out _);
        }

        Generation++;
    }

    // the array a descriptor describes now: what its holders hold - ReDim'd since the descriptor was asked for, it is another array object.
    private VBArrayValue CurrentArrayOf(ArrayDescriptor descriptor)
    {
        foreach (var holder in descriptor.Holders)
        {
            if (TryFindArrayAt(holder, out var array, out var inVariant) && array is { IsInitialized: true } && array.Rank == descriptor.Rank
                && (!inVariant || ReferenceEquals(array, descriptor.Array)))
            {
                return descriptor.Array = array;
            }
        }

        return descriptor.Array;
    }

    private byte[] DescriptorImage(ArrayDescriptor descriptor)
    {
        var array = CurrentArrayOf(descriptor);
        var elements = array.Length > 0 && TryAddressOfElement(array, 0, out var first) ? first.Value : 0L;
        return descriptor.Image(array, elements, LocksOf?.Invoke(array) ?? 0, pointerSize);
    }

    // a POKE into a descriptor: a lower bound moves the elements to other subscripts, a count makes the array shorter, its elements where they are (A11, A12);
    // anything else is refused - the address of the elements would be memory that is not the array's, which is what the session has no way to be.
    private bool TryPokeDescriptor(ArrayDescriptor descriptor, byte[] image)
    {
        var array = CurrentArrayOf(descriptor);
        if (descriptor.Reshaped(array, image, DescriptorImage(descriptor), pointerSize) is not { } dimensions)
        {
            return false;
        }

        var reshaped = array with { Dimensions = dimensions };
        RetargetBlock(array, reshaped);
        foreach (var holder in descriptor.Holders.ToList())
        {
            if (TryFindArrayAt(holder, out var held, out _) && ReferenceEquals(held, array))
            {
                TryWriteArrayAt(holder, reshaped);
            }
        }

        descriptor.Array = reshaped;
        Generation++;
        return true;
    }

    // a POKE into the pointer a dynamic array is (B12): 0 makes it an array of no dimensions, and the address of a descriptor makes it the array of that
    // descriptor, which the two then hold - an element written through either is the other's. What it pointed at is let go of, not freed. A pointer is written
    // a byte at a time, so one that points at no descriptor is kept as it is written, the array what it was until it does.
    private bool TryPokeArrayPointer(MemoryAddress holder, int within, byte value)
    {
        var bytes = StringHolders.PointerBytes(ArrayPointerAt(holder), pointerSize);
        bytes[within] = value;
        var pointer = pointerSize == sizeof(int) ? BitConverter.ToInt32(bytes) : BitConverter.ToInt64(bytes);
        var target = pointer == 0 ? null : _descriptors.GetValueOrDefault(new MemoryAddress(unchecked((int)pointer)));
        if (pointer != 0 && target is null)
        {
            _pendingPointers[holder] = bytes;
            Generation++;
            return true;
        }

        _pendingPointers.Remove(holder);
        var was = _descriptorOf.GetValueOrDefault(holder);
        if (was is not null && ReferenceEquals(was, target))
        {
            Generation++;
            return true;
        }

        if (!TryFindArrayAt(holder, out var held, out _))
        {
            return false;
        }

        if (was is not null)
        {
            DetachDescriptor(holder, was, free: false);
        }

        var itemType = held?.ItemType ?? VBVariantType.TypeInfo;
        if (!TryWriteArrayAt(holder, target is null ? new VBResizableArrayValue([], itemType) : CurrentArrayOf(target)))
        {
            return false;
        }

        if (target is not null)
        {
            target.Holders.Add(holder);
            _descriptorOf[holder] = target;
        }

        Generation++;
        return true;
    }

    // the array held at an address - by a variable, an element, a field, or a Variant (inVariant) - or none, for a Variant that holds something else.
    private bool TryFindArrayAt(MemoryAddress holder, out VBArrayValue? array, out bool inVariant)
    {
        array = null;
        inVariant = false;
        if (!allocator.TryFindBlock(holder, out var found))
        {
            return false;
        }

        if (!TryVariableAt(holder, out _) && !_arrayBlocks.ContainsKey(found.Address)
            && _handleByAddress.TryGetValue(found.Address, out var outer) && CanRead(outer) && RecordOf(outer) is { } record)
        {
            if (!StringHolders.TryFindField(record, holder.Value - found.Address.Value, pointerSize, out var field))
            {
                return false;
            }

            if (field.Element >= 0)
            {
                return StringHolders.TryRawOf(field, out var element) && ArrayOf(element, out array, out inVariant);
            }

            (array, inVariant) = field.Owner.FieldAt(field.Index) switch
            {
                VBArrayValue held => (held, false),
                VBVariantValue { TypedValue: var held } => (held as VBArrayValue, true),
                _ => (null, false),
            };

            return array is not null || inVariant;
        }

        return TryFindRawAt(holder, out var raw) && ArrayOf(raw, out array, out inVariant);
    }

    private static bool ArrayOf(IRuntimeValue raw, out VBArrayValue? array, out bool inVariant)
    {
        (array, inVariant) = raw switch
        {
            VBRuntimeValue<VBRuntimeArrayValue> held => (held.StoredValue.Array, false),
            VBRuntimeVariantValue { WrappedValue: var held } => (held as VBArrayValue, true),
            _ => (null, false),
        };

        return array is not null || inVariant;
    }

    // makes what holds an array at an address hold another.
    private bool TryWriteArrayAt(MemoryAddress holder, VBArrayValue array)
    {
        if (!TryFindArrayAt(holder, out _, out var inVariant) || !allocator.TryFindBlock(holder, out var found))
        {
            return false;
        }

        Generation++;
        if (!TryVariableAt(holder, out _) && !_arrayBlocks.ContainsKey(found.Address)
            && _handleByAddress.TryGetValue(found.Address, out var outer) && RecordOf(outer) is { } record
            && StringHolders.TryFindField(record, holder.Value - found.Address.Value, pointerSize, out var field) && field.Element < 0)
        {
            return field.Owner.TrySetFieldAt(field.Index, inVariant ? new VBVariantValue(array) : array);
        }

        return TryWriteRawAt(holder, inVariant
            ? new VBRuntimeVariantValue(VariantTypeOf(array), array)
            : new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array)));
    }

    // the block of an array's elements is the block of another array that is the same elements (a descriptor's bounds poked): the elements are where they were.
    private void RetargetBlock(VBArrayValue from, VBArrayValue to)
    {
        if (_blockOfArray.TryGetValue(from, out var start) && _arrayBlocks.TryGetValue(start.Value, out var block))
        {
            block.Retarget(to);
            _blockOfArray.AddOrUpdate(to, start);
        }
    }

    // the descriptors of the arrays held in what is freed are let go of, and freed when they are no one's.
    private void FreeDescriptorsIn(MemoryAddress start, int size)
    {
        foreach (var (holder, descriptor) in _descriptorOf.Where(held => held.Key.Value >= start.Value && held.Key.Value < start.Value + size).ToList())
        {
            DetachDescriptor(holder, descriptor, free: true);
        }
    }
}
