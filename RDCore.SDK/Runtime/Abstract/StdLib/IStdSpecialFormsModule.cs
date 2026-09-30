using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 3.3.5.2 Reserved Identifiers</strong> — the <c>special-form</c> reserved identifiers that are
/// used in an expression as if they were a procedure name.
/// </summary>
/// <remarks>
/// MS-VBAL reserves these names but gives them no semantics: a <c>special-form</c> is "used in an expression as if
/// it was a program defined procedure name but which has special syntactic rules for its argument", and is not a
/// member of any module the specification's standard library (section 6.1) defines. They are declared together
/// here, rather than among the members of a module they do not belong to, so that the members MS-VBAL does define
/// stay exactly as it defines them. What each one does is therefore the behavior MS-VBA documents for it.
/// <para>
/// 🚧 TODO <c>Array</c>, <c>Circle</c>, <c>Input</c>, <c>InputB</c> and <c>Scale</c>, the other special forms.
/// </para>
/// </remarks>
[StdLibModule]
public interface IStdSpecialFormsModule
{
    /// <summary>
    /// <strong>LBound</strong> Gets the lowest subscript available in one dimension of an array.
    /// </summary>
    /// <remarks>
    /// The argument is an array, of any element type and rank, whose bounds are read and which is left as it is.
    /// <para>
    /// 💥 Raises run-time error 9 <c>Subscript out of range</c> when the array has no dimensions yet — a dynamic
    /// array that has not been sized by <c>ReDim</c> — or when <paramref name="dimension"/> is not one of the
    /// array's dimensions, and run-time error 13 <c>Type mismatch</c> when <paramref name="arrayName"/> is not an
    /// array.
    /// </para>
    /// </remarks>
    /// <param name="arrayName">The array whose lower bound to get.</param>
    /// <param name="dimension">The dimension, counted from 1, whose lower bound to get. 1 when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongValue> LBound(VBVariantValue arrayName, VBVariantValue? dimension = default);

    /// <summary>
    /// <strong>UBound</strong> Gets the highest subscript available in one dimension of an array.
    /// </summary>
    /// <remarks>
    /// The argument is an array, of any element type and rank, whose bounds are read and which is left as it is.
    /// <para>
    /// 💥 Raises run-time error 9 <c>Subscript out of range</c> when the array has no dimensions yet — a dynamic
    /// array that has not been sized by <c>ReDim</c> — or when <paramref name="dimension"/> is not one of the
    /// array's dimensions, and run-time error 13 <c>Type mismatch</c> when <paramref name="arrayName"/> is not an
    /// array.
    /// </para>
    /// </remarks>
    /// <param name="arrayName">The array whose upper bound to get.</param>
    /// <param name="dimension">The dimension, counted from 1, whose upper bound to get. 1 when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongValue> UBound(VBVariantValue arrayName, VBVariantValue? dimension = default);
}
