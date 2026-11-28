using RDCore.Runtime.Semantics.Expressions;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// An array compiled code keeps (<see cref="ArrayOperations.KnownArray"/>), with its shape: its rank, and the bounds of each of its first three dimensions,
/// read when the array is found in its holder. The shape of an array never changes (a <c>ReDim</c> stores another array in the holder), and compiled code
/// keeps an array only while nothing it runs can give the holder another one, so the element at subscripts is found from the shape: each subscript
/// within the bounds of its dimension, the first varying fastest, as <see cref="VBArrayValue.IndexOf"/> numbers them (MS-VBAL §5.6.13).
/// </summary>
/// <remarks>
/// Compiled code holds one in a local for each array it keeps, gives it by reference to the operations of <see cref="ArrayOperations"/> that find an element
/// in it, and lets go of it by making it the default value again. What it does not find is the interpreter's, as the other operations say.
/// </remarks>
public struct KeptArray
{
    private VBArrayValue? _array;
    private int _rank;
    private int _lower0, _upper0, _length0;
    private int _lower1, _upper1, _length1;
    private int _lower2, _upper2, _length2;

    // the kind the elements are held as bytes of, read when the array is kept; none for elements held in cells.
    private ScalarKind? _bytesKind;

    // the array the holder holds, kept with its shape the first time; false when it holds no array that is known.
    private bool Keep(object? holder)
    {
        if (_array is not null)
        {
            return true;
        }

        if (ArrayOperations.KnownArray(holder) is not { } array)
        {
            return false;
        }

        _array = array;
        _rank = array.Rank;
        var dimensions = array.Dimensions;
        (_lower0, _upper0, _length0) = _rank > 0 ? (dimensions[0].LowerBound, dimensions[0].UpperBound, dimensions[0].Length) : default;
        (_lower1, _upper1, _length1) = _rank > 1 ? (dimensions[1].LowerBound, dimensions[1].UpperBound, dimensions[1].Length) : default;
        (_lower2, _upper2, _length2) = _rank > 2 ? (dimensions[2].LowerBound, dimensions[2].UpperBound, dimensions[2].Length) : default;
        _bytesKind = ScalarKinds.Of(array.ItemType) is { } kind && ArrayOperations.HoldsBytesOf(array, kind) ? kind : null;
        return true;
    }

    // the cell of the element at the subscripts, each a Long (an int holds it): none outside a dimension, of an array of another rank, or of an array with no
    // elements (a dimension of none), as VBArrayValue.IndexOf finds none.
    internal IBindingHandle? CellAt(object? holder, int rank, long first, long second, long third)
        => IndexAt(holder, rank, first, second, third) is var index and >= 0 ? _array!.CellAt(index) : null;

    // the array, when its elements are held as bytes of the kind, and the flat index of the element at the subscripts, as CellAt finds it.
    internal object? BytesAt(object? holder, ScalarKind kind, int rank, long first, long second, long third, out int index)
    {
        index = IndexAt(holder, rank, first, second, third);
        if (index < 0 || _bytesKind != kind)
        {
            index = -1;
            return null;
        }

        return _array;
    }

    private int IndexAt(object? holder, int rank, long first, long second, long third)
    {
        if (!Keep(holder) || _rank != rank)
        {
            return -1;
        }

        var index = (long)(int)first - _lower0;
        if ((ulong)index >= (ulong)_length0)
        {
            return -1;
        }

        if (rank > 1)
        {
            var next = (long)(int)second - _lower1;
            if ((ulong)next >= (ulong)_length1)
            {
                return -1;
            }

            var stride = (long)_length0;
            index += next * stride;
            if (rank > 2)
            {
                var last = (long)(int)third - _lower2;
                if ((ulong)last >= (ulong)_length2)
                {
                    return -1;
                }

                index += last * stride * _length1;
            }
        }

        return (int)index;
    }

    // LBound or UBound of the dimension, counted from 1, as ArrayBoundRuntimeSemantics.TryBoundOf reads it: none of an array with no dimensions, or of a
    // dimension it has not (error 9).
    internal bool TryBound(object? holder, bool upper, long dimension, out long bound)
    {
        bound = 0;
        if (!Keep(holder) || dimension < 1 || dimension > _rank)
        {
            return false;
        }

        switch (dimension)
        {
            case 1:
                bound = upper ? _upper0 : _lower0;
                return true;
            case 2:
                bound = upper ? _upper1 : _lower1;
                return true;
            case 3:
                bound = upper ? _upper2 : _lower2;
                return true;
        }

        if (!ArrayBoundRuntimeSemantics.TryBoundOf(_array!, upper ? ArrayBoundKind.Upper : ArrayBoundKind.Lower, (int)dimension, out var found))
        {
            return false;
        }

        bound = found;
        return true;
    }
}
