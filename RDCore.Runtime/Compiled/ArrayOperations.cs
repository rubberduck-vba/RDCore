using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Expressions;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// What compiled code does of an array parameter or variable: it finds an element in the array held in the cell its frame holds the
/// variable in (<see cref="CompiledActivation.ArrayCellOf"/>), or in the array an array field of a record is (<see cref="RecordOperations.ArrayField"/>),
/// as the interpreter's evaluation on payloads finds it (<c>ElementCellOf</c>), and reads the bounds of a dimension as <c>LBound</c> and <c>UBound</c> do
/// (<c>ArrayBoundRuntimeSemantics</c>). What it does not find (a subscript outside its dimension, an array of another rank, one with no dimensions, one that is not known, an element held in a cell of another kind) is the interpreter's: each operation
/// returns <see langword="null"/> or <see langword="false"/>, and the interpreter runs the statement, which raises the error (9) where there is one.
/// </summary>
/// <remarks>
/// The array is read from the cell each time, never kept: a <c>ReDim</c> stores another array in the same cell, and an element is found in the array the
/// variable holds now.
/// </remarks>
public static class ArrayOperations
{
    /// <summary>
    /// The cell of the element at <paramref name="first"/>, <paramref name="second"/> and <paramref name="third"/> (the first <paramref name="rank"/> of them,
    /// each a <c>Long</c>) of the array held in <paramref name="holder"/>, when it is a cell that holds a payload of <paramref name="kind"/>: what
    /// <see cref="CompiledActivation.ReadIntegral"/> and the others read and write.
    /// </summary>
    public static object? ScalarElement(object? holder, ScalarKind kind, int rank, long first, long second, long third)
        => ElementOf(holder, rank, first, second, third) is ScalarCell cell && ScalarKinds.Of(cell.Type) == kind ? cell : null;

    /// <summary>
    /// The cell of an element of an array of <c>Variant</c>s (<see cref="ScalarElement"/>), when it is a Variant's cell, or refers to a <c>Variant</c>
    /// variable's: what <see cref="VariantOperations.Read"/> and the others read and write.
    /// </summary>
    public static object? VariantElement(object? holder, int rank, long first, long second, long third)
        => ElementOf(holder, rank, first, second, third) switch
        {
            VariantCell cell => cell,

            // an element of a param array given a Variant variable refers to it, and is read and written as the variable is: in its cell.
            VariantReferenceBindingHandle { DeclaredType: VBVariantType, Variable: VariantCell variable } => variable,
            _ => null,
        };

    /// <summary>
    /// The cell of an element of an array of objects (<see cref="ScalarElement"/>), whose reference <see cref="ObjectOperations"/> reads: <c>Nothing</c>, of an
    /// element no object was ever <c>Set</c> to. One that holds no reference it reads declines there.
    /// </summary>
    public static object? ObjectElement(object? holder, int rank, long first, long second, long third)
        => ElementOf(holder, rank, first, second, third);

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
