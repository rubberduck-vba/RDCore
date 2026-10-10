using RDCore.External.Automation;
using RDCore.External.Protocol;
using RDCore.Runtime.Execution.External.Automation;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Runtime.Execution.External.Native;

/// <summary>
/// A user-defined type as MS-VBA passes it to a native function: the address of a copy of it, laid out as the type is in memory - every field on its natural
/// boundary, no stricter than eight bytes, and the whole padded to its strictest field (<see cref="VBUserDefinedTypeLayout"/>'s rule) - with its strings in the
/// ANSI code page, and taken back into the fields when the function returns.
/// </summary>
/// <remarks>
/// <para>
/// A <c>String</c> field is a pointer to a <c>BSTR</c> of ANSI characters, and a fixed-length <c>String * n</c> field is <em>n</em> ANSI bytes, inline - which
/// is why <c>Len</c> of a type that holds one is the size a function declared for its <c>A</c> flavour expects (<c>OSVERSIONINFO</c>, 148 bytes with its 128
/// characters). Everything else is as it is in memory: a pointer as wide as the environment's, a <c>Variant</c> as wide as a <c>VARIANT</c> is there, a nested
/// type and a fixed-size array inline.
/// </para>
/// <para>
/// 🚧 TODO a field that is an object other than <c>Nothing</c>, or a resizable array, is not passed yet: a record that holds one cannot be described.
/// </para>
/// </remarks>
internal static class NativeRecords
{
    // one field of the copy, and what puts back into the type what the function left in it.
    private sealed record class Leaf(NativeField Field, Action<object?> TakeBack);

    /// <summary>
    /// Describes the copy of <paramref name="value"/>, and what takes back into it what the function left in the copy.
    /// </summary>
    /// <param name="value">The value of the type - the variable the argument names, whose fields are written to.</param>
    /// <param name="pointerWidth">The width of a pointer in the environment the function runs in.</param>
    /// <param name="record">The copy, as it is passed.</param>
    /// <param name="takeBack">Takes back what the function left in each field, in the order of the copy's fields.</param>
    /// <returns><see langword="false"/> when the type holds a field that cannot be passed yet.</returns>
    public static bool TryDescribe(VBUserDefinedTypeValue value, int pointerWidth, out NativeRecord record, out Action<ExternalValue> takeBack)
    {
        var leaves = new List<Leaf>();
        if (!TryLay(value, 0, pointerWidth, leaves, out var size, out _))
        {
            record = new NativeRecord();
            takeBack = _ => { };
            return false;
        }

        record = new NativeRecord { Size = size, Fields = [.. leaves.Select(leaf => leaf.Field)] };
        takeBack = written =>
        {
            var elements = written.Elements ?? [];
            for (var index = 0; index < leaves.Count && index < elements.Length; index++)
            {
                leaves[index].TakeBack(ExternalValues.FromWire(elements[index], NoObject));
            }
        };
        return true;
    }

    // lays a type out from the offset it starts at: its size, and the boundary it is aligned to, are what a field of it takes.
    private static bool TryLay(VBUserDefinedTypeValue value, int start, int pointerWidth, List<Leaf> leaves, out int size, out int alignment)
    {
        var offset = 0;
        alignment = 1;
        for (var index = 0; index < value.Fields.Length; index++)
        {
            var field = index;
            var type = value.Fields[index].ResolvedType;
            if (type is null || !TryLayField(value.FieldAt(index), type, start, offset, pointerWidth, leaves,
                written => value.TrySetFieldAt(field, NativeValues.Typed(written, type)), out var next, out var fieldAlignment))
            {
                size = 0;
                return false;
            }

            offset = next;
            alignment = Math.Max(alignment, fieldAlignment);
        }

        size = AlignedTo(offset, alignment);
        return true;
    }

    // lays one field out after `offset`: where it ends is where the next one may start.
    private static bool TryLayField(
        VBTypedValue? value, VBType type, int start, int offset, int pointerWidth, List<Leaf> leaves, Action<object?> takeBack, out int end, out int alignment)
    {
        end = offset;
        alignment = 1;

        // a nested type is laid out on its own first, for the boundary it asks for, then placed on that boundary.
        if (type is VBUserDefinedType && value is VBUserDefinedTypeValue nested)
        {
            var inner = new List<Leaf>();
            if (!TryLay(nested, 0, pointerWidth, inner, out var size, out alignment))
            {
                return false;
            }

            var at = AlignedTo(offset, alignment);
            leaves.AddRange(inner.Select(leaf => leaf with { Field = leaf.Field with { Offset = start + at + leaf.Field.Offset } }));
            end = at + size;
            return true;
        }

        // a fixed-size array is its elements, inline, each where the one before it ends.
        if (type is VBFixedSizeArrayType array && value is VBArrayValue elements)
        {
            var at = offset;
            for (var index = 0; index < elements.Length; index++)
            {
                var subscripts = SubscriptsOf(elements, index);
                if (!TryLayField(elements.ElementAt(index), array.ItemType, start, at, pointerWidth, leaves,
                    written => elements.TrySetElement(new ValueBindingHandle(NativeValues.Typed(written, array.ItemType).RuntimeValue), subscripts),
                    out at, out var elementAlignment))
                {
                    return false;
                }

                alignment = Math.Max(alignment, elementAlignment);
            }

            end = at;
            return true;
        }

        if (MeasureOf(type, value, pointerWidth) is not var (slot, width, boundary, length, wire))
        {
            return false;
        }

        alignment = boundary;
        var placed = AlignedTo(offset, boundary);
        leaves.Add(new Leaf(new NativeField { Offset = start + placed, Slot = slot, Length = length, Value = wire }, takeBack));
        end = placed + width;
        return true;
    }

    // what one field of a type that is neither nested nor an array is in the copy: its slot, its width and boundary, its length if it is a run of bytes, and its value.
    private static (NativeSlot Slot, int Width, int Alignment, int Length, ExternalValue Value)? MeasureOf(VBType type, VBTypedValue? value, int pointerWidth)
    {
        switch (type)
        {
            case VBFixedStringType fixedString:
                return (NativeSlot.AnsiFixed, fixedString.Length, 1, fixedString.Length, Wire((value as VBStringValue)?.Value ?? string.Empty));
            case VBStringType:
                return (NativeSlot.AnsiBstr, pointerWidth, pointerWidth, 0, Wire((value as VBStringValue)?.Value ?? string.Empty));
            case VBVariantType:
                try
                {
                    return (NativeSlot.Variant, pointerWidth == 8 ? 24 : 16, 8, 0, Wire(AutomationMarshaller.ToAutomation(value ?? VBEmptyValue.Empty, _ => null)));
                }
                catch (AutomationException)
                {
                    return null;
                }
        }

        if (value is null || NativeValues.Scalar(value) is not var (slot, number))
        {
            return null;
        }

        var width = slot switch
        {
            NativeSlot.Byte => 1,
            NativeSlot.Int16 => 2,
            NativeSlot.Int32 or NativeSlot.Single => 4,
            NativeSlot.Pointer => pointerWidth,
            _ => 8,
        };
        return (slot, width, width, 0, Wire(number));
    }

    // the subscripts of the element at a flat index: the first dimension varies fastest, as the array's own store does.
    private static int[] SubscriptsOf(VBArrayValue array, int flatIndex)
    {
        var subscripts = new int[array.Rank];
        for (var dimension = 0; dimension < array.Rank; dimension++)
        {
            var length = array.Dimensions[dimension].Length;
            subscripts[dimension] = array.Dimensions[dimension].LowerBound + (flatIndex % length);
            flatIndex /= length;
        }

        return subscripts;
    }

    private static int AlignedTo(int offset, int alignment)
    {
        var boundary = Math.Min(Math.Max(alignment, 1), VBUserDefinedTypeLayout.MaxAlignment);
        var overshoot = offset % boundary;
        return overshoot == 0 ? offset : offset + boundary - overshoot;
    }

    private static ExternalValue Wire(object? value) => ExternalValues.ToWire(value, NoHandle);

    // a record passes values, never objects: there is no handle for one, either way.
    private static long NoHandle(object value) => throw new AutomationException(unchecked((int)0x80020005), SDK.Exceptions.VBDeclare_ObjectNotPassed_Verbose);

    private static object NoObject(long handle) => throw new AutomationException(unchecked((int)0x80020005), SDK.Exceptions.VBDeclare_ObjectNotPassed_Verbose);
}
