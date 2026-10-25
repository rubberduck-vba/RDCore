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
    /// <strong>ObjPtr</strong> Gets the address of an interface of an object in the session's memory.
    /// </summary>
    /// <remarks>
    /// Declared as the <c>VBA</c> type library declares it, <c>ObjPtr(Ptr As Unknown) As LongPtr</c>, and given where its argument is
    /// (<see cref="StdLibByAddressAttribute"/>): the interface is the one the variable it is passed is declared as. As in <strong>MS-VBA</strong> (recorded
    /// in Excel):
    /// <list type="bullet">
    /// <item><c>Nothing</c> is <c>0</c>, and an object's address is the same for as long as it lives;</item>
    /// <item>a variable of the object's class, of <c>Object</c>, and a <c>Variant</c> give one address; a variable of an interface the class implements gives
    /// another, and one of <c>IUnknown</c> another still;</item>
    /// <item>the bytes of an object variable are this address, and the first pointer at it is the same for every object of a class.</item>
    /// </list>
    /// </remarks>
    /// <param name="ptr">The address of the object variable.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(IsHidden = true)]
    RuntimeSemanticsEvaluationResult<VBLongPtrValue> ObjPtr([StdLibByAddress(typeof(VBObjectValue))] VBLongLongValue ptr);

    /// <summary>
    /// <strong>StrPtr</strong> Gets the address of the characters of a string in the session's memory.
    /// </summary>
    /// <remarks>
    /// Declared as the <c>VBA</c> type library declares it, <c>StrPtr(Ptr As String) As LongPtr</c>, and given where its argument is
    /// (<see cref="StdLibByAddressAttribute"/>): the characters are those of the very <c>String</c> it is passed, in the <c>BSTR</c> it is in - a <c>Long</c>
    /// of its length in bytes just before them, a null character just after. As in <strong>MS-VBA</strong> (recorded in Excel):
    /// <list type="bullet">
    /// <item><c>vbNullString</c>, and a <c>String</c> variable nothing was assigned to, is <c>0</c>; a zero-length <c>String</c> is not;</item>
    /// <item>a <c>String</c> variable, an element of a <c>String</c> array, a field of a record and a <c>Variant</c> holding a <c>String</c> each have their own
    /// characters, the same until another <c>String</c> is assigned to them: <c>t = s</c> copies them, and so does a <c>ByVal</c> parameter;</item>
    /// <item>the bytes of a <c>String</c> variable are this address;</item>
    /// <item>what is no variable, <c>StrPtr("abc")</c>, is a <c>String</c> of its own until the statement is over.</item>
    /// </list>
    /// </remarks>
    /// <param name="ptr">The address of the <c>String</c>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(IsHidden = true)]
    RuntimeSemanticsEvaluationResult<VBLongPtrValue> StrPtr([StdLibByAddress(typeof(VBStringValue))] VBLongLongValue ptr);

    /// <summary>
    /// <strong>VarPtr</strong> Gets the address of a variable in the session's memory.
    /// </summary>
    /// <remarks>
    /// Declared as the <c>VBA</c> type library declares it, <c>VarPtr(Ptr As Any) As LongPtr</c> (<see cref="StdLibAsAnyAttribute"/>): it takes a variable of
    /// any type but an array, and is given its address. As in <strong>MS-VBA</strong> (recorded in Excel):
    /// <list type="bullet">
    /// <item>the address is never <c>0</c>, and is the same for a variable for as long as it exists, whatever is assigned to it - the same for a module's
    /// variable named bare or qualified, and for a <c>Static</c> local across calls;</item>
    /// <item>a <c>ByRef</c> parameter's is the address of the variable it was given, and a <c>ByVal</c> parameter's its own; a <c>Variant</c> one given a
    /// variable of another type is a <c>VARIANT</c> of its own, at one address for the call;</item>
    /// <item>what is no variable - <c>VarPtr(1)</c>, <c>VarPtr(x + 0)</c>, <c>VarPtr((x))</c>, a public variable of an object named through it - is passed
    /// as a temporary, which has an address of its own until the statement is over;</item>
    /// <item><c>VarPtr(ByVal n)</c> is the bytes <c>n</c> is stored in, read as a whole number;</item>
    /// <item>an element of an array is in a block of the array's elements side by side, and a field of a record at its offset in the record, as
    /// <strong>MS-VBA</strong> lays them out.</item>
    /// </list>
    /// </remarks>
    /// <param name="ptr">The address of the variable.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(IsHidden = true)]
    RuntimeSemanticsEvaluationResult<VBLongPtrValue> VarPtr([StdLibAsAny] VBLongLongValue ptr);

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
