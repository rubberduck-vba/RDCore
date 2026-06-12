using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Xml;

namespace RDCore.SDK.Runtime.Abstract
{
    /// <summary>
    /// The semantic flags that can be attached to a <c>DataTypeConversion</c> semantic operation.
    /// </summary>
    [Flags]
    public enum ConversionSemanticFlags
    {
        /// <summary>
        /// This conversion operation occurs via an explicit <c>CType</c> data type conversion function, or an explicit <em>let-coercion operator</em> expression.
        /// </summary>
        Explicit = 1 << 0,
        /// <summary>
        /// This conversion operation does not occur via an explicit <c>CType</c> data type conversion function.
        /// </summary>
        /// <remarks>
        /// 👉 MS-VBAL equivalent to <c>ConversionSemanticFlags.LetCoerced</c>, but RD-VBAL introduces an <em>explicit coercion operator</em> that changes this.
        /// </remarks>
        Implicit = 1 << 1,
        /// <summary>
        /// This conversion operation produces a run-time error.
        /// </summary>
        Failed = 1 << 2,
        /// <summary>
        /// This conversion operation is recursive.
        /// </summary>
        Recursive = 1 << 3,
        /// <summary>
        /// This conversion implicates let-coercion semantics.
        /// </summary>
        /// <remarks>
        /// 👉 Equivalent to <see cref="ConversionSemanticFlags.Implicit"/>.
        /// </remarks>
        LetCoerced = Implicit,
        /// <summary>
        /// This conversion operation can be made explicit by inserting the appropriate <c>CType</c> data type conversion function call.
        /// </summary>
        CTypeAvailable = 1 << 4,
        /// <summary>
        /// This conversion operation results in a <em>wider</em> (larger) data type.
        /// </summary>
        Widening = 1 << 5,
        /// <summary>
        /// This conversion operation results in a <em>narrower</em> (smaller) data type.
        /// </summary>
        Narrowing = 1 << 6,
        /// <summary>
        /// This conversion loses precision.
        /// </summary>
        Lossy = 1 << 7,
        /// <summary>
        /// This conversion uses the <em>Banker's Rounding</em> algorithm.
        /// </summary>
        /// <remarks>
        /// Probably semantically equivalent to <see cref="ConversionSemanticFlags.Lossy"/>, but kept separate for clarity. Semantic analysis issues both flags.
        /// </remarks>
        BankersRounding = 1 << 8,
        /// <summary>
        /// This data type conversion operation evaluates to a <see cref="VBNumericType"/>.
        /// </summary>
        Numeric = 1 << 9,
        /// <summary>
        /// This conversion operation involves a <c>DateSerial</c> (<see cref="VBDoubleValue"/>) conversion from a <see cref="VBDateValue"/>.
        /// </summary>
        DateSerial = 1 << 10,
        /// <summary>
        /// This conversion operation implicates a <see cref="VBNullValue"/> operand.
        /// </summary>
        NullOperand = 1 << 11,
        /// <summary>
        /// This conversion operation implicates a <see cref="VBEmptyValue"/> operand.
        /// </summary>
        EmptyOperand = 1 << 12,
        /// <summary>
        /// This conversion operation implicates a <see cref="VBErrorValue"/> operand.
        /// </summary>
        ErrorOperand = 1 << 13,
        /// <summary>
        /// This conversion operation implicates a <see cref="VBObjectValue"/> operand.
        /// </summary>
        ObjectOperand = 1 << 14,
        /// <summary>
        /// This conversion operation implicates the operand of a unary operator expression.
        /// </summary>
        UnaryOperand = 1 << 15,
        /// <summary>
        /// This conversion operation implicates the <em>left-hand side</em> (LHS) operand of a binary operator expression.
        /// </summary>
        BinaryLeftOperand = 1 << 16,
        /// <summary>
        /// This conversion operation implicates the <em>right-hand side</em> (RHS) operand of a binary operator expression.
        /// </summary>
        BinaryRightOperand = 1 << 17,

        /// <summary>
        /// This conversion operation has a <see cref="VBUserDefinedTypeValue"/> <em>destination data type</em>.
        /// </summary>
        UserDefinedTypeTarget = 1 << 18,
        /// <summary>
        /// This conversion operation has a <see cref="VBVariantValue"/> <em>destination data type</em>.
        /// </summary>
        VariantTarget = 1 << 19,
        ArrayTarget = 1 << 20,
        ByteArrayTarget = 1 << 21,
        ByteArrayOperand = 1 << 22,
        /// <summary>
        /// Combines all values.
        /// </summary>
        All = Explicit | Implicit | Failed | Recursive | CTypeAvailable | Widening | Narrowing | Lossy | BankersRounding | Numeric | DateSerial | NullOperand | EmptyOperand | UnaryOperand | BinaryLeftOperand | BinaryRightOperand
    }
}
