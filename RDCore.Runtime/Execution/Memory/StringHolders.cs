using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// What holds a variable-length <c>String</c> in the session's memory, and so has a <see cref="StringBlock"/>: a <c>String</c> or a <c>Variant</c> holding
/// one, as a variable, an element of an array or a field of a record.
/// </summary>
internal static class StringHolders
{
    /// <summary>
    /// The <c>String</c> <paramref name="held"/> is, or holds as a <c>Variant</c>: <c>vbNullString</c> included, whose data is no address.
    /// </summary>
    public static bool TryStringOf(IRuntimeValue held, out IRuntimeValue text)
    {
        text = held is VBRuntimeVariantValue { WrappedValue: VBStringValue wrapped and not VBFixedStringValue } ? wrapped.RuntimeValue : held;
        return text is IRuntimeValue<string>;
    }

    /// <summary>
    /// What <paramref name="held"/> becomes, holding <paramref name="text"/> where it holds a <c>String</c>: a <c>Variant</c> holds it as a <c>Variant</c>.
    /// </summary>
    public static IRuntimeValue Replacing(IRuntimeValue held, IRuntimeValue text)
        => held is VBRuntimeVariantValue ? new VBRuntimeVariantValue(VBVarType.VBString, new VBStringValue(new ValueBindingHandle(text))) : text;

    /// <summary>
    /// The <c>String</c> a field holds, or holds as a <c>Variant</c>.
    /// </summary>
    public static bool TryStringOf(VBTypedValue? field, out IRuntimeValue text)
    {
        text = null!;
        return field switch
        {
            VBVariantValue { TypedValue: VBStringValue wrapped and not VBFixedStringValue } => TryStringOf(wrapped.RuntimeValue, out text),
            VBStringValue held and not VBFixedStringValue => TryStringOf(held.RuntimeValue, out text),
            _ => false,
        };
    }

    /// <summary>
    /// What a field becomes, holding <paramref name="text"/>: a <c>Variant</c> field holds it as a <c>Variant</c>.
    /// </summary>
    public static VBTypedValue Replacing(VBTypedValue field, IRuntimeValue text)
        => field is VBVariantValue ? new VBVariantValue(new VBStringValue(new ValueBindingHandle(text))) : new VBStringValue(new ValueBindingHandle(text));

    /// <summary>
    /// The field of <paramref name="record"/> that begins at <paramref name="offset"/>, in its layout for <paramref name="pointerWidth"/>, and is what the
    /// session lays out (<see cref="VBUserDefinedTypeImage.IsLaidOutByTheHost"/>) - a variable-length <c>String</c>, an object, a <c>Variant</c>: in a record
    /// that is a field of it too, and an element of a fixed-size array field, which is in the record (oracle\s24 P04).
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="offset">Where the field begins, from the start of the record.</param>
    /// <param name="pointerWidth">The session's pointer width.</param>
    /// <param name="location">Where the field is.</param>
    public static bool TryFindField(VBUserDefinedTypeValue record, int offset, int pointerWidth, out FieldLocation location)
    {
        location = default;
        var fields = VBUserDefinedTypeLayout.Of(record, pointerWidth).Fields;
        for (var at = 0; at < fields.Count; at++)
        {
            var field = fields[at];
            if (offset < field.Offset || offset >= field.Offset + field.Width)
            {
                continue;
            }

            if (field.Symbol.ResolvedType is VBUserDefinedType && record.FieldAt(at) is VBUserDefinedTypeValue nested)
            {
                return TryFindField(nested, offset - field.Offset, pointerWidth, out location);
            }

            if (record.FieldAt(at) is VBFixedSizeArrayValue { ItemType: var item } array && VBUserDefinedTypeImage.IsLaidOutByTheHost(item))
            {
                var width = VBUserDefinedTypeLayout.WidthOf(item, pointerWidth);
                var element = (offset - field.Offset) / width;
                location = new FieldLocation(record, at, element, item);
                return (offset - field.Offset) % width == 0 && element < array.Length;
            }

            location = new FieldLocation(record, at, Element: -1, field.Symbol.ResolvedType!);
            return offset == field.Offset && field.Symbol.ResolvedType is { } type && VBUserDefinedTypeImage.IsLaidOutByTheHost(type);
        }

        return false;
    }

    /// <summary>
    /// Where, in <paramref name="record"/>, the field the session lays out (<see cref="VBUserDefinedTypeImage.IsLaidOutByTheHost"/>) begins that
    /// <paramref name="offset"/> falls in - in a nested record, and an element of a fixed-size array field, too - and its declared type; <c>-1</c> when the
    /// offset falls in no such field.
    /// </summary>
    public static int SlotAt(VBUserDefinedTypeValue record, int offset, int pointerWidth, out VBType type)
    {
        type = VBUnknownType.TypeInfo;
        var fields = VBUserDefinedTypeLayout.Of(record, pointerWidth).Fields;
        for (var at = 0; at < fields.Count; at++)
        {
            var field = fields[at];
            if (offset < field.Offset || offset >= field.Offset + field.Width)
            {
                continue;
            }

            if (field.Symbol.ResolvedType is VBUserDefinedType && record.FieldAt(at) is VBUserDefinedTypeValue nested)
            {
                return Within(field.Offset, SlotAt(nested, offset - field.Offset, pointerWidth, out type));
            }

            if (record.FieldAt(at) is VBFixedSizeArrayValue { ItemType: var item } array)
            {
                var width = Math.Max(1, VBUserDefinedTypeLayout.WidthOf(item, pointerWidth));
                var element = (offset - field.Offset) / width;
                if (element >= array.Length)
                {
                    return -1;
                }

                var start = field.Offset + element * width;
                if (VBUserDefinedTypeImage.IsLaidOutByTheHost(item))
                {
                    type = item;
                    return start;
                }

                return item is VBUserDefinedType && array.ElementAt(element) is VBUserDefinedTypeValue inner
                    ? Within(start, SlotAt(inner, offset - start, pointerWidth, out type))
                    : -1;
            }

            if (field.Symbol.ResolvedType is { } declared && VBUserDefinedTypeImage.IsLaidOutByTheHost(declared))
            {
                type = declared;
                return field.Offset;
            }

            return -1;
        }

        return -1;
    }

    // a slot found inside a part that begins at `start`, from the start of the whole.
    private static int Within(int start, int slot) => slot < 0 ? -1 : start + slot;

    /// <summary>
    /// What the field at <paramref name="location"/> holds, as a variable's cell holds it: a <c>Variant</c> as a <see cref="VBRuntimeVariantValue"/>.
    /// </summary>
    public static bool TryRawOf(FieldLocation location, out IRuntimeValue raw)
    {
        raw = null!;
        if (location.Element >= 0)
        {
            if (location.Owner.FieldAt(location.Index) is not VBArrayValue array || array.CellAt(location.Element) is not { } cell
                || !cell.BindingCapabilities.HasFlag(BindingCapabilities.GetValue))
            {
                return false;
            }

            raw = cell.Value;
            return true;
        }

        if (location.Owner.FieldAt(location.Index) is not { } field)
        {
            return false;
        }

        raw = field is VBVariantValue variant ? new VBRuntimeVariantValue(variant.TypedValue.TypeInfo.VarType(), variant.TypedValue) : field.RuntimeValue;
        return true;
    }

    /// <summary>
    /// Makes the field at <paramref name="location"/> hold <paramref name="raw"/>, as a variable's cell holds it (<see cref="TryRawOf"/>).
    /// </summary>
    public static bool TryWriteRaw(FieldLocation location, IRuntimeValue raw)
    {
        if (location.Element >= 0)
        {
            return location.Owner.FieldAt(location.Index) is VBArrayValue array && array.TrySetElementAt(location.Element, new ValueBindingHandle(raw));
        }

        return location.Owner.TrySetFieldAt(location.Index, Typed(raw, location.Type));
    }

    /// <summary>
    /// The value <paramref name="raw"/>, held by a variable's cell, is of <paramref name="declared"/>: a <c>Variant</c> the value it holds.
    /// </summary>
    public static VBTypedValue Typed(IRuntimeValue raw, VBType declared)
        => raw is VBRuntimeVariantValue { WrappedValue: var held } ? new VBVariantValue(held) : declared.CreateValue(new ValueBindingHandle(raw));

    /// <summary>
    /// The <c>String</c> the field at <paramref name="location"/> holds, or holds as a <c>Variant</c>.
    /// </summary>
    public static bool TryStringOf(FieldLocation location, out IRuntimeValue text)
    {
        text = null!;
        if (location.Element < 0)
        {
            return TryStringOf(location.Owner.FieldAt(location.Index), out text);
        }

        return location.Owner.FieldAt(location.Index) is VBArrayValue array && array.CellAt(location.Element) is { } cell
            && cell.BindingCapabilities.HasFlag(BindingCapabilities.GetValue) && TryStringOf(cell.Value, out text);
    }

    /// <summary>
    /// Makes the field at <paramref name="location"/> hold <paramref name="text"/>.
    /// </summary>
    public static bool TryWrite(FieldLocation location, IRuntimeValue text)
    {
        if (location.Element < 0)
        {
            return location.Owner.FieldAt(location.Index) is { } held && location.Owner.TrySetFieldAt(location.Index, Replacing(held, text));
        }

        return location.Owner.FieldAt(location.Index) is VBArrayValue array && array.CellAt(location.Element) is { } cell
            && array.TrySetElementAt(location.Element, new ValueBindingHandle(Replacing(cell.Value, text)));
    }

    /// <summary>
    /// The bytes of a pointer: what a variable-length <c>String</c> is in memory, the address of its characters.
    /// </summary>
    public static byte[] PointerBytes(long pointer, int pointerWidth)
        => pointerWidth == sizeof(int) ? BitConverter.GetBytes(unchecked((int)pointer)) : BitConverter.GetBytes(pointer);
}

/// <summary>
/// Where a field of a record that the session lays out is - a <c>String</c>, an object, a <c>Variant</c>: a field of <paramref name="Owner"/>, and an element
/// of it when it is a fixed-size array.
/// </summary>
/// <param name="Owner">The record the field is a field of.</param>
/// <param name="Index">Which field of <paramref name="Owner"/> it is.</param>
/// <param name="Element">Which element of the field it is, in the order the array numbers them; <c>-1</c> for a field that is no array.</param>
/// <param name="Type">The declared type of the field, or of the elements of the array it is an element of.</param>
internal readonly record struct FieldLocation(VBUserDefinedTypeValue Owner, int Index, int Element, VBType Type);
