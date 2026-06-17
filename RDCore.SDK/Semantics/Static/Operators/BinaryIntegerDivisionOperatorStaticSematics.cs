using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3.6</strong> Binary '\' Operator and 'Mod' Operator (static semantics)
/// </summary>
public sealed record class BinaryIntegerDivisionOperatorStaticSematics : BinaryArithmeticOperatorStaticSemantics
{
    /// <summary>
    /// Determines a static <see cref="VBType"/> from specified operands.
    /// </summary>
    /// <param name="resolver">The static context containing the available static memory space.</param>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="lhs">The declared type of the <em>left-hand side</em> (LHS) operand involved in the evaluation.</param>
    /// <param name="rhs">The declared type of the <em>right-hand side</em> (RHS) operand involved in the evaluation.</param>
    /// <returns>
    /// A <see cref="StaticSemanticsEvaluationResult"/> encapsulating the resulting <see cref="VBType"/> if successful, or <see cref="VBCompileErrorInfo"/> error metadata otherwise.
    /// </returns>
    protected override StaticSemanticsEvaluationResult DetermineOperatorStaticType(ISymbolResolver resolver, BoundExpression expression, VBType lhs, VBType rhs) 
        => lhs switch
        {
            IFloatingPointNumericType or IFixedPointNumericType or VBFixedStringType or VBStringType or VBDateType
                when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType
                    => StaticSemanticsEvaluationResult.Success(VBLongType.TypeInfo),

            VBNumericType or VBFixedStringType or VBStringType or VBDateType
                when rhs is IFloatingPointNumericType or IFixedPointNumericType or VBFixedStringType or VBStringType or VBDateType
                    => StaticSemanticsEvaluationResult.Success(VBLongType.TypeInfo),

            // these *should* be covered by the above...
            VBSingleType 
                when rhs is VBByteType or VBBooleanType or VBIntegerType 
                    => StaticSemanticsEvaluationResult.Success(VBLongType.TypeInfo),
            
            VBByteType or VBBooleanType or VBIntegerType 
                when rhs is VBSingleType 
                    => StaticSemanticsEvaluationResult.Success(VBLongType.TypeInfo),

            VBSingleType 
                when rhs is VBLongType or VBLongLongType 
                    => StaticSemanticsEvaluationResult.Success(VBLongType.TypeInfo),
            VBLongType or VBLongLongType 
                when rhs is VBSingleType 
                    => StaticSemanticsEvaluationResult.Success(VBLongType.TypeInfo),

            VBDoubleType or VBFixedStringType or VBStringType 
                when rhs is IIntegralNumericType or IFloatingPointNumericType or VBFixedStringType or VBStringType
                    => StaticSemanticsEvaluationResult.Success(VBLongType.TypeInfo),
            IIntegralNumericType or IFloatingPointNumericType or VBFixedStringType or VBStringType 
                when rhs is VBDoubleType or VBFixedStringType or VBStringType
                    => StaticSemanticsEvaluationResult.Success(VBLongType.TypeInfo),

            VBCurrencyType 
                when rhs is VBNumericType or VBFixedStringType or VBStringType 
                    => StaticSemanticsEvaluationResult.Success(VBLongType.TypeInfo),
            VBNumericType or VBFixedStringType or VBStringType 
                when rhs is (VBCurrencyType) 
                    => StaticSemanticsEvaluationResult.Success(VBLongType.TypeInfo),

            VBDateType 
                when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType 
                    => StaticSemanticsEvaluationResult.Success(VBLongType.TypeInfo),
            VBNumericType or VBFixedStringType or VBStringType or VBDateType 
                when rhs is VBDateType 
                    => StaticSemanticsEvaluationResult.Success(VBLongType.TypeInfo),

            _ => base.DetermineOperatorStaticType(resolver, expression, lhs, rhs)
        };
}