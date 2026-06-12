namespace RDCore.SDK.Semantics.Flags;

public enum LogicalOperatorSemanticFlags
{
    /// <summary>
    /// The operation involves a <c>VBNullValue</c> operand.
    /// </summary>
    HasNullOperand = 0,
    /// <summary>
    /// The semantic <em>effective type</em> of the operation is <c>VBByteType</c>.
    /// </summary>
    ByteEffectiveType = 1 << 1,
    /// <summary>
    /// The semantic <em>effective type</em> of the operation is <c>VBBooleanType</c> (but then let-coerced to a <c>VBIntegerValue</c> for evaluation).
    /// </summary>
    BooleanEffectiveType = 1 << 2,
    /// <summary>
    /// The semantic <em>effective type</em> of the operation is <c>VBIntegerType</c>.
    /// </summary>
    IntegerEffectiveType = 1 << 3,
    /// <summary>
    /// The semantic <em>effective type</em> of the operation is <c>VBLongType</c>.
    /// </summary>
    LongEffectiveType = 1 << 4,
    /// <summary>
    /// The semantic <em>effective type</em> of the operation is <c>VBLongLongType</c>.
    /// </summary>
    LongLongEffectiveType = 1 << 5,
    /// <summary>
    /// The semantic <em>effective type</em> of the operation is <c>VBNullType</c> (both operands are <c>VBNullValue</c>).
    /// </summary>
    NullEffectiveType = 1 << 6,
    /// <summary>
    /// <c>true</c> if the operation is evaluated using <em>bitwise</em> semantics.
    /// </summary>
    IsBitwiseSemantics = 1 << 7,

    All = HasNullOperand | ByteEffectiveType | BooleanEffectiveType | IntegerEffectiveType | LongEffectiveType | LongLongEffectiveType | NullEffectiveType | IsBitwiseSemantics
}
