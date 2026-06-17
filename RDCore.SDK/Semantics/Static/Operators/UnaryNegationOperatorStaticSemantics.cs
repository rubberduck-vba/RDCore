using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3.1</strong> Unary '-' Operator (static semantics)
/// </summary>
public sealed record class UnaryNegationOperatorStaticSemantics : UnaryArithmeticOperatorStaticSemantics
{
    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="operand">The declared type of the operand.</param>
    protected override StaticSemanticsEvaluationResult DetermineOperatorStaticType(ISymbolResolver resolver, BoundExpression expression, VBType operand) 
        => operand switch
        {
            VBByteType => StaticSemanticsEvaluationResult.Success(VBIntegerType.TypeInfo),
            _ => base.DetermineOperatorStaticType(resolver, expression, operand)
        };
}
