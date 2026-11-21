using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// The places inside a record that a reference parameter is bound to at their address (<strong>MS-VBAL §5.3.1.11</strong>): a field, a field of a record
/// field, an element of a fixed-size array field, each where MS-VBA lays it out in the record (<see cref="VBUserDefinedTypeLayout"/>).
/// </summary>
/// <remarks>
/// Several places can start at one address: a record and its first field, an array field and its first element. A record, or an array field, is the place
/// when it is of the type asked for; any other place is the innermost one that starts there, whatever type is asked for (an object field read as
/// <c>Object</c>).
/// </remarks>
internal static class RecordPlaces
{
    /// <summary>
    /// The place inside <paramref name="record"/> that starts at <paramref name="offset"/> and holds a value of <paramref name="type"/>.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="offset">Where the place starts, from the start of the record.</param>
    /// <param name="type">The type of the value the place holds.</param>
    /// <param name="pointerWidth">The session's pointer width, which the layout depends on.</param>
    /// <param name="fieldOfRecord">
    /// The binding of a field that is a record or a <c>Variant</c>, which has no cell of its own (<see cref="RecordFieldBinding"/>).
    /// </param>
    /// <param name="place">The place's binding.</param>
    /// <returns><see langword="false"/> when no place starts at the offset.</returns>
    public static bool TryFind(VBUserDefinedTypeValue record, int offset, VBType type, int pointerWidth,
        Func<VBUserDefinedTypeValue, int, VBType, IBindingHandle>? fieldOfRecord, [NotNullWhen(true)] out IBindingHandle? place)
    {
        place = null;
        var fields = VBUserDefinedTypeLayout.Of(record, pointerWidth).Fields;
        for (var at = 0; at < fields.Count; at++)
        {
            var field = fields[at];
            if (offset < field.Offset || offset >= field.Offset + field.Width)
            {
                continue;
            }

            var within = offset - field.Offset;
            var held = record.StoredFieldAt(at);
            if (held is VBUserDefinedTypeValue inner && !(within == 0 && IsRecordOf(type, inner)))
            {
                return TryFind(inner, within, type, pointerWidth, fieldOfRecord, out place);
            }

            // the elements of a fixed-size array field are in the record, side by side.
            if (field.Symbol.ResolvedType is VBFixedSizeArrayType && held is VBArrayValue array && type is not VBArrayType)
            {
                var width = Math.Max(1, VBUserDefinedTypeLayout.WidthOf(array.ItemType, pointerWidth));
                return TryFindElement(array, width, within, type, pointerWidth, fieldOfRecord, out place);
            }

            if (within != 0 || held is null || field.Symbol.ResolvedType is not { } declared)
            {
                return false;
            }

            place = declared is VBUserDefinedType or VBVariantType ? fieldOfRecord?.Invoke(record, at, declared) : held.Handle;
            return place is not null;
        }

        return false;
    }

    /// <summary>
    /// The place inside <paramref name="array"/>, whose elements are side by side, that starts at <paramref name="offset"/> and holds a value of
    /// <paramref name="type"/>: an element, or a place inside a record element.
    /// </summary>
    /// <param name="array">The array.</param>
    /// <param name="width">How many bytes an element occupies.</param>
    /// <param name="offset">Where the place starts, from the start of the first element.</param>
    /// <param name="type">The type of the value the place holds.</param>
    /// <param name="pointerWidth">The session's pointer width, which a record element's layout depends on.</param>
    /// <param name="fieldOfRecord">
    /// The binding of a field that is a record or a <c>Variant</c>, which has no cell of its own (<see cref="RecordFieldBinding"/>).
    /// </param>
    /// <param name="place">The place's binding.</param>
    /// <returns><see langword="false"/> when no place starts at the offset.</returns>
    public static bool TryFindElement(VBArrayValue array, int width, int offset, VBType type, int pointerWidth,
        Func<VBUserDefinedTypeValue, int, VBType, IBindingHandle>? fieldOfRecord, [NotNullWhen(true)] out IBindingHandle? place)
    {
        place = null;
        var index = offset / width;
        var within = offset % width;
        if (index >= array.Length)
        {
            return false;
        }

        if (array.ItemType is VBUserDefinedType && array.ElementAt(index) is VBUserDefinedTypeValue record && !(within == 0 && IsRecordOf(type, record)))
        {
            return TryFind(record, within, type, pointerWidth, fieldOfRecord, out place);
        }

        place = within == 0 ? array.CellAt(index) : null;
        return place is not null;
    }

    /// <summary>
    /// Whether <paramref name="record"/> is of <paramref name="type"/>.
    /// </summary>
    public static bool IsRecordOf(VBType type, VBUserDefinedTypeValue record)
        => type is VBUserDefinedType wanted && record.TypeInfo is VBUserDefinedType own && wanted.Equals(own);
}
