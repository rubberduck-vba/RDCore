using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>RD-VBAL 6.1.2.13 Hidden Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library's <c>_HiddenModule</c>: the members of the <c>VBA</c> library
/// that its type library marks hidden, and that an object browser lists under that name. It is not one of the modules
/// <strong>MS-VBAL §6.1.2</strong> specifies, but the language needs it all the same - <c>Array</c> is documented
/// (<see href="https://learn.microsoft.com/en-us/office/vba/language/reference/user-interface-help/array-function"/>) and
/// <c>Input$</c> and <c>Width</c> are as old as the language - so the platform declares it as it does the modules the
/// specification does name. What the hidden members share is that a hidden member is left out of a completion list and
/// resolves anyway, being a member of a standard module like any other.
/// <para>
/// The pointer members (<c>ObjPtr</c>, <c>StrPtr</c>, <c>VarPtr</c>) are not documented by Microsoft even so; what they
/// are is what the language has always done with them.
/// </para>
/// </remarks>
[StdLibModule("_HiddenModule", IsHidden = true)]
public interface IStdHiddenModule
{
    /// <summary>
    /// <strong>Array</strong> Gets a <c>Variant</c> containing an array of the arguments.
    /// </summary>
    /// <remarks>
    /// The array is a resizable array of <c>Variant</c>, with one element for each argument, in order. With no arguments it is
    /// zero-length: its upper bound is one below its lower.
    /// <para>
    /// Its lower bound is always <c>0</c>, whatever <c>Option Base</c> says. That is the one a call qualified with the name of
    /// the library reaches - <c>VBA.Array(1, 2)</c> - and the classic gotcha of the language: written without a qualifier,
    /// <c>Array(1, 2)</c> is not a call of this member but the <c>Array</c> keyword, which begins at the <c>Option Base</c>
    /// of its module (<see cref="Model.AST.Expressions.ArrayExpressionNode"/>).
    /// </para>
    /// </remarks>
    /// <param name="arglist">The values the elements of the array are given.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Array(params VBVariantValue[] arglist);

    /// <summary>
    /// <strong>Input</strong> Gets the characters read from a file opened in <c>Input</c> or <c>Binary</c> mode.
    /// </summary>
    /// <remarks>
    /// Error 62 when the file ends before <paramref name="number"/> characters could be read, 52 when the file number is not open,
    /// and 54 when the file was opened in a mode that cannot be read this way. The characters read are the string; unlike
    /// <c>Input #</c>, nothing is parsed, and a quotation mark or a comma is a character like any other.
    /// </remarks>
    /// <param name="number">The number of characters to read.</param>
    /// <param name="fileNumber">The file number of an open file.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Input(VBLongValue number, VBIntegerValue fileNumber);

    /// <inheritdoc cref="Input"/>
    [StdLibMember("Input$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> InputString(VBLongValue number, VBIntegerValue fileNumber);

    /// <summary>
    /// <strong>InputB</strong> Gets the <em>bytes</em> read from a file, as <see cref="Input"/> gets characters.
    /// </summary>
    /// <remarks>
    /// 🚧 Not implemented: a file channel reads characters, and nothing in it reads a byte.
    /// </remarks>
    /// <param name="number">The number of bytes to read.</param>
    /// <param name="fileNumber">The file number of an open file.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> InputB(VBLongValue number, VBIntegerValue fileNumber);

    /// <inheritdoc cref="InputB"/>
    [StdLibMember("InputB$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> InputBString(VBLongValue number, VBIntegerValue fileNumber);

    /// <summary>
    /// <strong>ObjPtr</strong> Gets a number that identifies an object: the same one for as long as it lives, and a different one
    /// for every other object.
    /// </summary>
    /// <remarks>
    /// <c>0</c> for <c>Nothing</c>. The number is the identity the session gives the object, not an address in memory
    /// the program could read through: nothing in the runtime lays objects out in an address space.
    /// </remarks>
    /// <param name="object">The object.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(IsHidden = true)]
    RuntimeSemanticsEvaluationResult<VBLongPtrValue> ObjPtr(VBObjectValue @object);

    /// <summary>
    /// <strong>StrPtr</strong> Gets the address of the characters of a string.
    /// </summary>
    /// <remarks>
    /// 🚧 Not implemented: the characters of a string live in the value that holds them, not at an address of the session's memory.
    /// </remarks>
    /// <param name="string">The string.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(IsHidden = true)]
    RuntimeSemanticsEvaluationResult<VBLongPtrValue> StrPtr(VBStringValue @string);

    /// <summary>
    /// <strong>VarPtr</strong> Gets the address of a variable.
    /// </summary>
    /// <remarks>
    /// 🚧 Not implemented: it takes its argument by reference, which a standard-library member cannot yet declare.
    /// </remarks>
    /// <param name="variable">The variable.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(IsHidden = true)]
    RuntimeSemanticsEvaluationResult<VBLongPtrValue> VarPtr(VBVariantValue variable);

    /// <summary>
    /// <strong>Width</strong> Sets the line width of a file opened for output: the number of characters a line holds before
    /// <c>Print #</c> and <c>Write #</c> start another (<strong>MS-VBAL §5.4.5.7</strong>).
    /// </summary>
    /// <remarks>
    /// The <c>Width</c> statement is this member called without parentheses: <c>Width #1, 80</c>.
    /// </remarks>
    /// <param name="fileNumber">The file number of an open file.</param>
    /// <param name="width">The width, from 0 (no limit) to 255.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult Width(VBIntegerValue fileNumber, VBIntegerValue width);
}
