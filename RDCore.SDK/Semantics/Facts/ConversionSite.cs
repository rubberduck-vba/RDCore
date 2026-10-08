namespace RDCore.SDK.Semantics.Facts;

/// <summary>
/// Where in the code a value is let-coerced (<strong>MS-VBAL 5.5.1.2</strong>): the construct that asks for the conversion.
/// </summary>
/// <remarks>
/// 👉 A conversion is not always an operand of an operator. Assignments, arguments, conditions, loop bounds, subscripts and
/// <c>Print</c> items coerce too, and what an analyzer says about a conversion depends on which of them it is: narrowing a
/// <c>Double</c> into a <c>Long</c> variable is not the same finding as narrowing it into a subscript. Only the construct
/// that performs the coercion knows which it is, so it states it when it asks for the conversion
/// (<see cref="RDCore.SDK.Runtime.Shared.LetCoercionStackFrame.Site"/>).
/// </remarks>
public enum ConversionSite
{
    /// <summary>
    /// The construct that asked for the conversion did not say where it is.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not a valid answer for a construct that coerces: seeing it in a fact means a site was not given the context it needs.
    /// </remarks>
    Unspecified = 0,

    /// <summary>An operand of an operator expression, coerced to the <em>effective type</em> of the operation.</summary>
    OperatorOperand,

    /// <summary>The value of a <c>Let</c> assignment, coerced to the declared type of its target (<strong>MS-VBAL 5.4.3.8</strong>).</summary>
    Assignment,

    /// <summary>An argument of a call, coerced to the declared type of the parameter.</summary>
    Argument,

    /// <summary>The value of a function or property, coerced to its declared return type.</summary>
    Return,

    /// <summary>The condition of an <c>If</c>, <c>ElseIf</c>, <c>While</c> or <c>Do</c> block, coerced to <c>Boolean</c>.</summary>
    Condition,

    /// <summary>An array subscript or the bounds of an array declaration, coerced to <c>Long</c>.</summary>
    Subscript,

    /// <summary>The start, end or step of a <c>For</c> loop, or the counter it is compared and incremented by.</summary>
    LoopBound,

    /// <summary>The selector of a <c>Select Case</c> block and the expressions of its clauses it is compared with.</summary>
    CaseTest,

    /// <summary>The selector of an <c>On ... GoTo</c> or <c>On ... GoSub</c> statement, coerced to <c>Long</c>.</summary>
    JumpSelector,

    /// <summary>The error number of an <c>Error</c> statement, coerced to <c>Long</c>.</summary>
    ErrorNumber,

    /// <summary>An item of a <c>Print</c> or <c>Debug.Print</c> statement.</summary>
    PrintItem,

    /// <summary>An operand of a file statement: the file number, the record number, the value written or the line read.</summary>
    FileStatement,

    /// <summary>An operand of a statement that edits a <c>String</c>: <c>Mid</c>, <c>LSet</c> and <c>RSet</c>.</summary>
    StringStatement,

    /// <summary>The object expression of a <c>With</c> block, coerced to the default member it denotes when it is a value.</summary>
    WithTarget,
}
