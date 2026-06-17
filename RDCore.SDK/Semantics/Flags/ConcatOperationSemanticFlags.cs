namespace RDCore.SDK.Semantics.Flags;

/// <summary>
/// The <em>semantic flags</em> of a <em>string concatenation</em> operation.
/// </summary>
[Flags]
public enum ConcatOperationSemanticFlags
{
    /// <summary>
    /// The <em>effective type</em> of the operation is <c>VBStringType</c>.
    /// </summary>
    /// <remarks>
    /// MS-VBAL 5.6.9.4 (runtime semantics) labels it the <em>value type</em> of the operation;
    /// the term <em>effective type</em> is used here for internal consistency.
    /// </remarks>
    StringEffectiveType = 1 << 0,
    /// <summary>
    /// The <em>effective type</em> of the operation is <c>VBNullType</c>.
    /// </summary>
    /// <remarks>
    /// MS-VBAL 5.6.9.4 (runtime semantics) labels it the <em>value type</em> of the operation;
    /// the term <em>effective type</em> is used here for internal consistency.
    /// </remarks>
    NullEffectiveType = 1 << 1,
    /// <summary>
    /// The operation implicates an array operand that does not contain <c>VBByteValue</c> elements.
    /// </summary>
    /// <remarks>
    /// 👉 This operation results in a <c>VBRuntimeErrorTypeMismatchException</c>.
    /// </remarks>
    HasNonByteArrayOperand = 1 << 2,
    /// <summary>
    /// The operation implicates a <c>VBUserDefinedTypeValue</c>.
    /// </summary>
    /// <remarks>
    /// 👉 This operation results in a <c>VBRuntimeErrorTypeMismatchException</c>.
    /// </remarks>
    HasUserDefinedTypeOperand = 1 << 3,
    /// <summary>
    /// The operation implicates a <c>VBNumericType</c> operand.
    /// </summary>
    HasNumericOperand = 1 << 4,
    /// <summary>
    /// The operation implicates a <c>VBResizableByteArrayType</c> operand.
    /// </summary>
    HasByteArrayOperand = 1 << 5,
    /// <summary>
    /// The operation implicates a <c>VBErrorValue</c>.
    /// </summary>
    /// <remarks>
    /// 👉 This operation results in a <c>VBRuntimeErrorTypeMismatchException</c>.
    /// </remarks>
    HasErrorOperand = 1 << 6,
    /// <summary>
    /// The operation implicates a <c>VBNullValue</c> operand.
    /// </summary>
    HasNullOperand = 1 << 7,

    All = StringEffectiveType | NullEffectiveType | HasNonByteArrayOperand | HasUserDefinedTypeOperand | HasNumericOperand | HasByteArrayOperand
        | HasErrorOperand | HasNullOperand
}
