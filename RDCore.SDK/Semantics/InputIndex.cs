namespace RDCore.SDK.Semantics;

/// <summary>
/// Encodes the index of an input.
/// </summary>
public enum InputIndex
{
    /// <summary>
    /// The first input.
    /// </summary>
    First = 0,
    /// <summary>
    /// The second input.
    /// </summary>
    Second = 1,
    /// <summary>
    /// The third input.
    /// </summary>
    //Third = 2,

    /// <summary>
    /// The operand is for a <em>unary operator</em> at index 0.
    /// </summary>
    UnaryOperand = First,
    /// <summary>
    /// The operand is for a <em>binary operator</em> at index 0.
    /// </summary>
    BinaryLeftOperand = First,
    /// <summary>
    /// The operand is for a <em>binary operator</em> at index 1.
    /// </summary>
    BinaryRightOperand = Second,
    //TernaryFirstOperand = First,
    //TernaryMiddleOperand = Second,
    //TernaryLastOperand = Third,

    /// <summary>
    /// The <em>source value</em> of a <em>let-coercion</em> operation.
    /// </summary>
    CoercionSourceValue = First,
    /// <summary>
    /// The <em>destination type</em> of a <em>let-coercion</em> operation.
    /// </summary>
    CoercionDestinationType = Second,
}
