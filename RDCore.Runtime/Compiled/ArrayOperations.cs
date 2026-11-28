using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Expressions;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// What compiled code does of an array parameter or variable: it finds an element in the array held in the cell its frame holds the
/// variable in (<see cref="CompiledActivation.ArrayCellOf"/>), or in the array an array field of a record is (<see cref="RecordOperations.ArrayField"/>),
/// as the interpreter's evaluation on payloads finds it (<c>ElementOf</c>), and reads the bounds of a dimension as <c>LBound</c> and <c>UBound</c> do
/// (<c>ArrayBoundRuntimeSemantics</c>). What it does not find (a subscript outside its dimension, an array of another rank, one with no dimensions, one
/// that is not known, an element not held as bytes of its kind) is the interpreter's: each operation returns <see langword="null"/> or
/// <see langword="false"/>, and the interpreter runs the statement, which raises the error (9) where there is one.
/// </summary>
/// <remarks>
/// A <c>ReDim</c> stores another array in the same cell, and an element is found in the array the variable holds now. Compiled code keeps the array a
/// variable holds, with its shape (<see cref="KeptArray"/>), only while nothing it runs can give the variable another array, and finds an element in it
/// by the operations whose names start with Kept. After anything else (the interpreter running an instruction, a call, a statement that stores an
/// array), it finds the array in the cell again.
/// </remarks>
public static class ArrayOperations
{
    /// <summary>
    /// The array held in <paramref name="holder"/>, found as the other operations of this class find it. What compiled code keeps, with its shape, while the
    /// variable cannot be given another array (<see cref="KeptArray"/>).
    /// </summary>
    /// <param name="holder">The cell of the array variable, the array field, or the cell of the <c>Variant</c> that holds the array.</param>
    /// <returns>
    /// <see langword="null"/> when the holder holds no array that is known: each operation given it declines.
    /// </returns>
    public static VBArrayValue? KnownArray(object? holder) => KnownArrayIn(holder);

    /// <summary>
    /// The array held in <paramref name="holder"/>, when its elements are of <paramref name="kind"/> and held as bytes
    /// (<see cref="VBArrayValue.ElementEncoding"/>), and in <paramref name="index"/> the flat index of the element at <paramref name="first"/>,
    /// <paramref name="second"/> and <paramref name="third"/> (the first <paramref name="rank"/> of them, each a <c>Long</c>): what
    /// <see cref="ReadIntegral"/> and the others read and write.
    /// </summary>
    public static object? ScalarElement(object? holder, ScalarKind kind, int rank, long first, long second, long third, out int index)
    {
        index = -1;
        if (KnownArrayIn(holder) is not { } array || array.Rank != rank || !HoldsBytesOf(array, kind))
        {
            return null;
        }

        Span<int> subscripts = [(int)first, (int)second, (int)third];
        index = array.IndexOf(subscripts[..rank]);
        return index < 0 ? null : array;
    }

    /// <summary>
    /// The payload of the element at <paramref name="index"/> of <paramref name="array"/>, of an integral <paramref name="kind"/>, as
    /// <see cref="ScalarElement"/> found it: none of an element that is not known.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ReadIntegral(object array, int index, ScalarKind kind, out long value)
        => Unsafe.As<VBArrayValue>(array).TryReadElementAt(index, ScalarKinds.EncodingOf(kind), out value, out _);

    /// <summary>
    /// The payload of the element at <paramref name="index"/> of <paramref name="array"/>, of a floating-point <paramref name="kind"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ReadFloating(object array, int index, ScalarKind kind, out double value)
        => Unsafe.As<VBArrayValue>(array).TryReadElementAt(index, ScalarKinds.EncodingOf(kind), out _, out value);

    /// <summary>
    /// Let-assigns a payload of the element's integral <paramref name="kind"/> to the element at <paramref name="index"/> of <paramref name="array"/>
    /// (MS-VBAL §5.4.3.8), where the array holds it: the cell a <c>ByRef</c> parameter given the element is bound to reads it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool WriteIntegral(object array, int index, ScalarKind kind, long value)
        => Unsafe.As<VBArrayValue>(array).TryWriteElementAt(index, ScalarKinds.EncodingOf(kind), value, 0);

    /// <summary>
    /// Let-assigns a payload of the element's floating-point <paramref name="kind"/> to the element at <paramref name="index"/> of <paramref name="array"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool WriteFloating(object array, int index, ScalarKind kind, double value)
        => Unsafe.As<VBArrayValue>(array).TryWriteElementAt(index, ScalarKinds.EncodingOf(kind), 0, value);

    // whether the elements of an array are of a kind, held as bytes of its encoding: an element of an enumeration is a Long.
    internal static bool HoldsBytesOf(VBArrayValue array, ScalarKind kind)
        => array.ElementEncoding != RecordFieldEncoding.None && array.ElementEncoding == ScalarKinds.EncodingOf(kind)
            && ScalarKinds.Of(array.ItemType) == kind;

    /// <summary>
    /// The cell of an element of an array of <c>Variant</c>s (<see cref="ScalarElement"/>), when it is a Variant's cell, or refers to a <c>Variant</c>
    /// variable's: what <see cref="VariantOperations.Read"/> and the others read and write.
    /// </summary>
    public static object? VariantElement(object? holder, int rank, long first, long second, long third)
        => VariantCellOf(ElementOf(holder, rank, first, second, third));

    /// <summary>
    /// The cell of an element of an array of objects (<see cref="ScalarElement"/>), whose reference <see cref="ObjectOperations"/> reads: <c>Nothing</c>, of an
    /// element no object was ever <c>Set</c> to. One that holds no reference it reads declines there.
    /// </summary>
    public static object? ObjectElement(object? holder, int rank, long first, long second, long third)
        => ElementOf(holder, rank, first, second, third);

    /// <summary>
    /// The array compiled code keeps (<see cref="KeptArray"/>) and the flat index of an element of it, as <see cref="ScalarElement"/> finds them in the
    /// array held in <paramref name="holder"/>: the array is found in the holder the first time, and kept with its shape until compiled code lets go of it.
    /// </summary>
    /// <param name="kept">The array compiled code keeps, with its shape; none until an element is found in it.</param>
    /// <param name="holder">What holds the array, as <see cref="ScalarElement"/> is given it.</param>
    /// <param name="kind">The kind of the payload the element holds.</param>
    /// <param name="rank">How many of the subscripts there are.</param>
    /// <param name="first">The first subscript.</param>
    /// <param name="second">The second subscript, when there is one.</param>
    /// <param name="third">The third subscript, when there is one.</param>
    /// <param name="index">The element's flat index; -1 when there is none.</param>
    public static object? KeptScalarElement(ref KeptArray kept, object? holder, ScalarKind kind, int rank, long first, long second, long third, out int index)
        => kept.BytesAt(holder, kind, rank, first, second, third, out index);

    /// <summary>
    /// The cell of an element of an array of <c>Variant</c>s compiled code keeps, as <see cref="VariantElement"/> finds it (<see cref="KeptScalarElement"/>).
    /// </summary>
    /// <inheritdoc cref="KeptScalarElement" path="/param"/>
    public static object? KeptVariantElement(ref KeptArray kept, object? holder, int rank, long first, long second, long third)
        => VariantCellOf(kept.CellAt(holder, rank, first, second, third));

    /// <summary>
    /// The cell of an element of an array of objects compiled code keeps, as <see cref="ObjectElement"/> finds it (<see cref="KeptScalarElement"/>).
    /// </summary>
    /// <inheritdoc cref="KeptScalarElement" path="/param"/>
    public static object? KeptObjectElement(ref KeptArray kept, object? holder, int rank, long first, long second, long third)
        => kept.CellAt(holder, rank, first, second, third);

    /// <summary>
    /// <c>LBound</c> or <c>UBound</c> of a dimension of an array compiled code keeps, as <see cref="Bound"/> reads it (<see cref="KeptScalarElement"/>).
    /// </summary>
    /// <param name="kept">The array compiled code keeps, with its shape.</param>
    /// <param name="holder">What holds the array.</param>
    /// <param name="upper">Whether it is the upper bound.</param>
    /// <param name="dimension">The dimension, counted from 1.</param>
    /// <param name="bound">The bound.</param>
    /// <returns><see langword="false"/> when it is the interpreter's, as <see cref="Bound"/> says.</returns>
    public static bool KeptBound(ref KeptArray kept, object? holder, bool upper, long dimension, out long bound)
        => kept.TryBound(holder, upper, dimension, out bound);

    // an element of an array of Variants, in the cell compiled code reads a Variant in: its own, or the variable's a param array element refers to, which is
    // read and written as the variable is.
    private static object? VariantCellOf(IBindingHandle? element) => element switch
    {
        VariantCell cell => cell,
        VariantReferenceBindingHandle { DeclaredType: VBVariantType, Variable: VariantCell variable } => variable,
        _ => null,
    };

    /// <summary>
    /// <c>LBound</c> (<paramref name="upper"/> <see langword="false"/>) or <c>UBound</c> of the dimension <paramref name="dimension"/>, counted from 1, of the
    /// array held in <paramref name="holder"/> (MS-VBAL §3.3.5.2), as <c>ArrayBoundRuntimeSemantics</c> reads it.
    /// </summary>
    /// <returns><see langword="false"/> when it is the interpreter's: the array is not known, or has no such dimension (error 9).</returns>
    public static bool Bound(object? holder, bool upper, long dimension, out long bound)
    {
        bound = 0;
        if (KnownArrayIn(holder) is not { } array
            || dimension is < int.MinValue or > int.MaxValue
            || !ArrayBoundRuntimeSemantics.TryBoundOf(array, upper ? ArrayBoundKind.Upper : ArrayBoundKind.Lower, (int)dimension, out var found))
        {
            return false;
        }

        bound = found;
        return true;
    }

    // the array a holder holds, known: the cell of an array variable, the array an array field of a record is (RecordOperations.ArrayField), or the array
    // the cell of a Variant holds.
    private static VBArrayValue? KnownArrayIn(object? holder)
        => RuntimeExpressionEvaluator.KnownArrayOf(holder as IBindingHandle)
            ?? (holder is VBArrayValue { IsIndeterminate: false } field ? field
                : holder is VariantCell { IsKnown: true, Value: VBRuntimeVariantValue { WrappedValue: VBArrayValue { IsIndeterminate: false } held } } ? held : null);

    // the cell at the subscripts (MS-VBAL §5.6.13), in the order VBArrayValue.IndexOf numbers them: none outside a dimension, or of an array of another rank.
    private static IBindingHandle? ElementOf(object? holder, int rank, long first, long second, long third)
    {
        if (KnownArrayIn(holder) is not { } array || array.Rank != rank)
        {
            return null;
        }

        // a subscript is a Long, which an int holds.
        Span<int> subscripts = [(int)first, (int)second, (int)third];
        return array.CellAt(array.IndexOf(subscripts[..rank]));
    }
}
