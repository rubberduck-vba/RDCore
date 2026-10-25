using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.Memory;

// the objects and the Variants of the session's memory, as MS-VBA lays them out (recorded in Excel, oracle\s17 O rows, V16, V22): an object variable is a
// pointer to an interface of the object (ObjectHeader), a Variant a VARIANT.
internal sealed partial class SessionStorage
{
    // the interfaces of the objects, by the address each starts at and by the object and the interface; the tables of the classes' interfaces, which the
    // first pointer of an interface is the address of; and the declared types of the variables that are objects, whose bytes are the address of an interface.
    private readonly Dictionary<MemoryAddress, ObjectHeader> _headers = [];
    private readonly Dictionary<(VBRuntimeObjectId Object, string? Interface), ObjectHeader> _headerOf = [];
    private readonly Dictionary<(string? Class, string? Interface), MemoryAddress> _vtables = [];
    private readonly Dictionary<MemoryAddress, VBType> _objectVariables = [];

    /// <summary>
    /// The class an object is an instance of, as a key that is the same for every object of the class: what tells the objects whose interfaces begin with the
    /// same pointer (oracle\s17 O07, O08). The session composes it; an object it knows no class of shares a table with every other such.
    /// </summary>
    public Func<VBRuntimeObjectId, string?>? ClassOf { get; set; }

    // how many bytes a VARIANT is, and an interface's table: its first three pointers are IUnknown's.
    private int VariantSize => pointerSize == sizeof(long) ? 24 : 16;
    private int VTableSize => 3 * pointerSize;

    /// <summary>
    /// Says that the variable at <paramref name="address"/> is an object, declared as <paramref name="declared"/>: its bytes are the address of the interface
    /// of the object it holds that the type says (<c>ObjPtr</c>; recorded in Excel, oracle\s17 O03). What gives the address of a variable away (<c>VarPtr</c>)
    /// says so of the ones it gives.
    /// </summary>
    public void HoldsObject(MemoryAddress address, VBType declared)
    {
        if (_handleByAddress.ContainsKey(address))
        {
            _objectVariables[address] = declared;
        }
    }

    /// <summary>
    /// The address of the interface of the object held at <paramref name="holder"/> - a variable, an element of an array, a field of a record, a
    /// <c>Variant</c> holding one - that its declared type says (<see cref="ObjectHeader"/>): what <c>ObjPtr</c> is, <c>0</c> for <c>Nothing</c>.
    /// </summary>
    /// <returns><see langword="false"/> when nothing at the address holds an object, or there is no room for its interface.</returns>
    public bool TryGetObjectPointer(MemoryAddress holder, out long pointer)
    {
        pointer = 0;
        if (!TryFindRawAt(holder, out var raw) || ObjectOf(raw) is not { } held)
        {
            return false;
        }

        var declared = raw is VBRuntimeVariantValue ? null : _objectVariables.GetValueOrDefault(holder) ?? DeclaredTypeAt(holder);
        if (held.Equals(VBObjectValue.Nothing.Value))
        {
            return true;
        }

        return TryHeaderOf(held, declared, out pointer);
    }

    /// <summary>
    /// Lets go of the interfaces of <paramref name="destroyed"/>, an object that is destroyed: their addresses are the session's again.
    /// </summary>
    public void ReleaseHeadersOf(VBRuntimeObjectId destroyed)
    {
        foreach (var (key, header) in _headerOf.Where(entry => entry.Key.Object.Equals(destroyed)).ToList())
        {
            _headerOf.Remove(key);
            _headers.Remove(header.Start);
            allocator.TryDeallocate(header.Start, out _);
            Generation++;
        }
    }

    // the object a cell holds, held by an object variable or a Variant: Nothing included.
    private static VBRuntimeObjectId? ObjectOf(IRuntimeValue raw) => raw switch
    {
        VBRuntimeValue<VBRuntimeObjectId> reference => reference.StoredValue,
        VBRuntimeVariantValue { WrappedValue: VBObjectValue reference } => reference.Value,
        _ => null,
    };

    // what the bytes of the pointer an object is are: what was poked into it while it points at no interface, the address of its interface otherwise.
    private long ObjectPointerAt(MemoryAddress holder)
        => _pendingPointers.TryGetValue(holder, out var pending)
            ? pointerSize == sizeof(int) ? BitConverter.ToInt32(pending) : BitConverter.ToInt64(pending)
            : TryGetObjectPointer(holder, out var pointer) ? pointer : 0;

    // the declared type of what an element of an array, or a field of a record, is: what says which interface of an object it holds.
    private VBType? DeclaredTypeAt(MemoryAddress holder) => TryFindSlot(holder, out _, out var within, out var type) && within == 0 ? type : null;

    // the interface of an object a type says: the class's own for the object's class, Object and a Variant (O02, O06); another for each other class, an
    // interface it implements, and for IUnknown.
    private string? InterfaceOf(VBRuntimeObjectId held, VBType? declared)
        => declared is VBClassType named && named.Symbol.Uri.AbsoluteUri is var interfaceKey && interfaceKey != ClassOf?.Invoke(held) ? interfaceKey : null;

    private bool TryHeaderOf(VBRuntimeObjectId held, VBType? declared, out long pointer)
    {
        var key = (held, InterfaceOf(held, declared));
        if (_headerOf.TryGetValue(key, out var known))
        {
            pointer = known.Start.Value;
            return true;
        }

        pointer = 0;
        var table = (ClassOf?.Invoke(held), key.Item2);
        if (!_vtables.TryGetValue(table, out var vtable))
        {
            if (!allocator.TryAllocate(VTableSize, out vtable))
            {
                return false;
            }

            _vtables[table] = vtable;
        }

        if (!allocator.TryAllocate(ObjectHeader.SizeFor(pointerSize), out var start))
        {
            return false;
        }

        var made = new ObjectHeader(start, held, vtable);
        _headers[start] = made;
        _headerOf[key] = made;
        Generation++;
        pointer = start.Value;
        return true;
    }

    // a POKE into the pointer an object is (O04): 0 makes it Nothing, and the address of an interface of an object makes it that object - without a reference
    // of its own, as MS-VBA counts none: the weak reference programs make so. A pointer is written a byte at a time, so one that points at no interface is kept
    // as it is written, the object what it was until it does.
    private bool TryPokeObjectPointer(MemoryAddress holder, int within, byte value)
    {
        var bytes = StringHolders.PointerBytes(ObjectPointerAt(holder), pointerSize);
        bytes[within] = value;
        var pointer = pointerSize == sizeof(int) ? BitConverter.ToInt32(bytes) : BitConverter.ToInt64(bytes);
        var header = pointer == 0 ? null : _headers.GetValueOrDefault(new MemoryAddress(unchecked((int)pointer)));
        if (pointer != 0 && header is null)
        {
            _pendingPointers[holder] = bytes;
            Generation++;
            return true;
        }

        _pendingPointers.Remove(holder);
        if (!TryFindRawAt(holder, out var raw))
        {
            return false;
        }

        var held = header?.Object ?? VBObjectValue.Nothing.Value;
        return TryWriteRawAt(holder, raw is VBRuntimeVariantValue
            ? new VBRuntimeVariantValue(VBVarType.VBObject, new VBObjectValue(held))
            : new VBRuntimeValue<VBRuntimeObjectId>(held));
    }

    // a Variant is a VARIANT (V16, V22): its type, the VarType, in its first two bytes, and its data eight bytes in - a number as its bytes, a String the
    // address of its characters, an object the address of its interface (O06), a Decimal across the whole of it. 🚧 an array's is the address of a SAFEARRAY,
    // which nothing here makes: zeros.
    private byte[]? VariantImageAt(MemoryAddress holder)
    {
        if (!TryFindRawAt(holder, out var raw) || raw is not VBRuntimeVariantValue variant)
        {
            return null;
        }

        var image = new byte[VariantSize];
        var held = variant.WrappedValue;
        BitConverter.TryWriteBytes(image.AsSpan(0, sizeof(short)), unchecked((short)VariantTypeOf(held)));
        var data = held switch
        {
            VBEmptyValue or VBNullValue => null,
            VBArrayValue => StringHolders.PointerBytes(ArrayPointerAt(holder), pointerSize),
            VBStringValue => StringHolders.PointerBytes(PointerAt(holder), pointerSize),
            VBObjectValue => StringHolders.PointerBytes(ObjectPointerAt(holder), pointerSize),
            VBDecimalValue @decimal => DecimalData(image, @decimal.Value),
            _ => VBUserDefinedTypeImage.Encode(held.TypeInfo, held, Math.Max(1, VBUserDefinedTypeLayout.WidthOf(held.TypeInfo, pointerSize)), pointerSize),
        };

        data?.AsSpan(0, Math.Min(data.Length, image.Length - VariantDataOffset)).CopyTo(image.AsSpan(VariantDataOffset));
        return image;
    }

    // where a VARIANT's data begins.
    private const int VariantDataOffset = 8;

    // the type a VARIANT says it holds, as VarType says it (StdInformation): an array's, its elements' and vbArray; an object's, vbObject; a LongPtr's, the
    // integer a pointer is.
    private VBVarType VariantTypeOf(VBTypedValue held) => held switch
    {
        VBArrayValue array => VBVarType.VBArray | ElementTypeOf(array.ItemType),
        VBObjectValue => VBVarType.VBObject,
        VBLongPtrValue => PointerVarType,
        _ => held.TypeInfo.VarType(),
    };

    private VBVarType ElementTypeOf(VBType type) => type is VBLongPtrType_x64 or VBLongPtrType_x86 ? PointerVarType : type.VarType();

    private VBVarType PointerVarType => pointerSize == sizeof(long) ? VBVarType.VBLongLong : VBVarType.VBLong;

    // a Decimal is a DECIMAL over the whole of the VARIANT but its type: its scale at 2, its sign at 3, its high 32 bits at 4 - and the data, its low 64 bits, at 8.
    private static byte[] DecimalData(byte[] image, decimal value)
    {
        var bits = decimal.GetBits(value);
        image[2] = (byte)((bits[3] >> 16) & 0xFF);
        image[3] = (byte)(bits[3] < 0 ? 0x80 : 0);
        BitConverter.TryWriteBytes(image.AsSpan(4, sizeof(int)), bits[2]);
        return BitConverter.GetBytes(((long)bits[1] << 32) | (uint)bits[0]);
    }

    private static decimal DecimalOf(byte[] image)
    {
        var low = BitConverter.ToInt64(image, VariantDataOffset);
        return new decimal(unchecked((int)low), unchecked((int)(low >> 32)), BitConverter.ToInt32(image, 4), (image[3] & 0x80) != 0, image[2]);
    }

    // a POKE into a VARIANT: what its type and its data say now. The data of a String or an object is the pointer it is, as a String's or an object
    // variable's is; a type that is neither a number nor Empty nor Null, or a type changed to or from a String's or an object's, is refused: what it would
    // point at is nothing the session has.
    private bool TryPokeVariant(MemoryAddress holder, int within, byte value)
    {
        if (VariantImageAt(holder) is not { } image || !TryFindRawAt(holder, out var raw) || raw is not VBRuntimeVariantValue variant)
        {
            return false;
        }

        image[within] = value;
        var type = (VBVarType)BitConverter.ToInt16(image, 0);
        var was = VariantTypeOf(variant.WrappedValue);
        var points = was is VBVarType.VBString or VBVarType.VBObject || was.HasFlag(VBVarType.VBArray);
        if (points && type == was && within >= VariantDataOffset && within < VariantDataOffset + pointerSize)
        {
            return was is VBVarType.VBString ? TryPokePointer(holder, within - VariantDataOffset, value)
                : was is VBVarType.VBObject ? TryPokeObjectPointer(holder, within - VariantDataOffset, value)
                : TryPokeArrayPointer(holder, within - VariantDataOffset, value);
        }

        if (points && type == was)
        {
            Generation++;
            return true;
        }

        if (points || Decoded(type, image) is not { } decoded)
        {
            return false;
        }

        return TryWriteRawAt(holder, new VBRuntimeVariantValue(type, decoded));
    }

    // the value a VARIANT of a type holds that is no pointer, from its bytes.
    private VBTypedValue? Decoded(VBVarType type, byte[] image)
    {
        var held = type switch
        {
            VBVarType.VBEmpty => (VBTypedValue)VBEmptyValue.Empty,
            VBVarType.VBNull => VBNullValue.Null,
            VBVarType.VBDecimal => new VBDecimalValue(DecimalOf(image)),
            _ => null,
        };

        if (held is not null)
        {
            return held;
        }

        VBType? scalar = type switch
        {
            VBVarType.VBInteger => VBIntegerType.TypeInfo,
            VBVarType.VBLong => VBLongType.TypeInfo,
            VBVarType.VBSingle => VBSingleType.TypeInfo,
            VBVarType.VBDouble => VBDoubleType.TypeInfo,
            VBVarType.VBCurrency => VBCurrencyType.TypeInfo,
            VBVarType.VBDate => VBDateType.TypeInfo,
            VBVarType.VBBoolean => VBBooleanType.TypeInfo,
            VBVarType.VBByte => VBByteType.TypeInfo,
            VBVarType.VBLongLong => VBLongLongType.TypeInfo,
            _ => null,
        };

        return scalar is null
            ? null
            : VBUserDefinedTypeImage.Decode(scalar, image.AsSpan(VariantDataOffset, Math.Max(1, VBUserDefinedTypeLayout.WidthOf(scalar, pointerSize))));
    }

    // what is held at an address, as a variable's cell holds it: a variable, an element of an array, a field of a record.
    private bool TryFindRawAt(MemoryAddress holder, out IRuntimeValue raw)
    {
        raw = null!;
        if (TryVariableAt(holder, out var handle))
        {
            if (!CanRead(handle))
            {
                return false;
            }

            raw = handle.Value;
            return true;
        }

        if (!allocator.TryFindBlock(holder, out var found))
        {
            return false;
        }

        var offset = holder.Value - found.Address.Value;
        if (_arrayBlocks.TryGetValue(found.Address, out var elements))
        {
            return elements.TryRawAt(offset, out raw);
        }

        return _handleByAddress.TryGetValue(found.Address, out var outer) && CanRead(outer) && RecordOf(outer) is { } record
            && StringHolders.TryFindField(record, offset, pointerSize, out var field)
            && StringHolders.TryRawOf(field, out raw);
    }

    // makes what is held at an address hold another, as a variable's cell holds it.
    private bool TryWriteRawAt(MemoryAddress holder, IRuntimeValue raw)
    {
        Generation++;
        if (TryVariableAt(holder, out var handle))
        {
            if (handle is not SDK.Model.Values.Bindings.ValueBindingHandle cell)
            {
                return TryRebind(holder, new SDK.Model.Values.Bindings.ValueBindingHandle(raw));
            }

            cell.Store(raw, isKnown: true);
            return true;
        }

        if (!allocator.TryFindBlock(holder, out var found))
        {
            return false;
        }

        var offset = holder.Value - found.Address.Value;
        if (_arrayBlocks.TryGetValue(found.Address, out var elements))
        {
            return elements.TryWriteRawAt(offset, raw);
        }

        return _handleByAddress.TryGetValue(found.Address, out var outer) && RecordOf(outer) is { } record
            && StringHolders.TryFindField(record, offset, pointerSize, out var field)
            && StringHolders.TryWriteRaw(field, raw);
    }
}
