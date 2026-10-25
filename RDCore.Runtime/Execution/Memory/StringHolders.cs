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
    /// The field of <paramref name="record"/> that begins at <paramref name="offset"/>, in its layout for <paramref name="pointerWidth"/>, and is a
    /// variable-length <c>String</c> or a <c>Variant</c>: in a record that is a field of it too.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="offset">Where the field begins, from the start of the record.</param>
    /// <param name="pointerWidth">The session's pointer width.</param>
    /// <param name="owner">The record the field is a field of.</param>
    /// <param name="index">Which field of <paramref name="owner"/> it is.</param>
    public static bool TryFindField(VBUserDefinedTypeValue record, int offset, int pointerWidth, out VBUserDefinedTypeValue owner, out int index)
    {
        owner = record;
        index = -1;
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
                return TryFindField(nested, offset - field.Offset, pointerWidth, out owner, out index);
            }

            // the order matters: a fixed-length String is a VBStringType, and is its characters.
            if (offset == field.Offset && field.Symbol.ResolvedType is VBStringType and not VBFixedStringType or VBVariantType)
            {
                index = at;
                return true;
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// The bytes of a pointer: what a variable-length <c>String</c> is in memory, the address of its characters.
    /// </summary>
    public static byte[] PointerBytes(long pointer, int pointerWidth)
        => pointerWidth == sizeof(int) ? BitConverter.GetBytes(unchecked((int)pointer)) : BitConverter.GetBytes(pointer);
}
