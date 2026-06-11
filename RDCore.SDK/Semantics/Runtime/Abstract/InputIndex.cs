using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Semantics.Runtime.Abstract;

/// <summary>
/// Encodes the index of an input.
/// </summary>
public enum InputIndex
{
    /// <summary>
    /// The first input, at index 0.
    /// </summary>
    FirstInput = 0,
    /// <summary>
    /// The second input, at index 1.
    /// </summary>
    SecondInput = 1,
    /// <summary>
    /// The third input, at index 2.
    /// </summary>
    ThirdInput = 2,

    /// <summary>
    /// The input represents the single operand of a <em>unary operator</em>, at index 0.
    /// </summary>
    UnaryOperand = FirstInput,
    /// <summary>
    /// The input represents the <em>left-hand side</em> (LHS) operand of a <em>binary operator</em>, at index 0.
    /// </summary>
    BinaryLeftOperand = FirstInput,
    /// <summary>
    /// The input represents the <em>right-hand side</em> (RHS) operand of a <em>binary operator</em>, at index 1.
    /// </summary>
    BinaryRightOperand = SecondInput,
    //TernaryFirstOperand = FirstInput,
    //TernaryMiddleOperand = SecondInput,
    //TernaryLastOperand = ThirdInput,

    /// <summary>
    /// The input represents the <em>source value</em> of a <c>CType</c> <em>explicit-coercion</em> function, at index 0.
    /// </summary>
    CTypeSourceValue = FirstInput,
    /// <summary>
    /// The input represents the <em>target type</em> of a <c>CType</c> <em>explicit-coercion</em> function, at index 1.
    /// </summary>
    CTypeTargetType = SecondInput,
    
}

public static class OperandIndexExtensions
{
    /// <summary>
    /// Gets the operand at the specified strongly-typed index.
    /// </summary>
    /// <param name="operands">An array of <c>VBTypedValue</c> operands.</param>
    /// <param name="index">The <c>OperandIndex</c> to retrieve.</param>
    /// <returns>The <c>VBTypedValue</c> at the specified index.</returns>
    public static VBTypedValue GetOperandValue(this VBTypedValue[] operands, InputIndex index) => operands[(int)index];
    /// <summary>
    /// Gets the operand type at the specified strongly-typed index.
    /// </summary>
    /// <param name="operandTypes">An array of <c>VBType</c> operand types.</param>
    /// <param name="index">The <c>OperandIndex</c> to retrieve.</param>
    /// <returns>The <c>VBType</c> at the specified index.</returns>
    public static VBType GetOperandType(this VBType[] operandTypes, InputIndex index) => operandTypes[(int)index];
}