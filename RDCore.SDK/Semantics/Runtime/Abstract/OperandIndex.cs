using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Semantics.Runtime.Abstract
{
    /// <summary>
    /// Encodes the index of an operand.
    /// </summary>
    public enum OperandIndex
    {
        /// <summary>
        /// The operand is for a <em>unary operator</em> at index 0.
        /// </summary>
        UnaryOperand = 0,
        /// <summary>
        /// The operand is for a <em>binary operator</em> at index 0.
        /// </summary>
        BinaryLeftOperand = UnaryOperand,
        /// <summary>
        /// The operand is for a <em>binary operator</em> at index 1.
        /// </summary>
        BinaryRightOperand = 1,
        //TernaryFirstOperand = BinaryLeftOperand,
        //TernaryMiddleOperand = BinaryRightOperand,
        //TernaryLastOperand = 2,
    }

    public static class OperandIndexExtensions
    {
        /// <summary>
        /// Gets the operand at the specified strongly-typed index.
        /// </summary>
        /// <param name="operands">An array of <c>VBTypedValue</c> operands.</param>
        /// <param name="index">The <c>OperandIndex</c> to retrieve.</param>
        /// <returns>The <c>VBTypedValue</c> at the specified index.</returns>
        public static VBTypedValue GetOperandValue(this VBTypedValue[] operands, OperandIndex index) => operands[(int)index];
        /// <summary>
        /// Gets the operand type at the specified strongly-typed index.
        /// </summary>
        /// <param name="operandTypes">An array of <c>VBType</c> operand types.</param>
        /// <param name="index">The <c>OperandIndex</c> to retrieve.</param>
        /// <returns>The <c>VBType</c> at the specified index.</returns>
        public static VBType GetOperandType(this VBType[] operandTypes, OperandIndex index) => operandTypes[(int)index];
    }
}