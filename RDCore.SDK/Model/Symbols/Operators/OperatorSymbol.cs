using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Semantics.Runtime.Operators.Context;
using RDCore.SDK.Semantics.Runtime.Operators.Flags;

namespace RDCore.SDK.Model.Symbols.Operators
{
    #region Arithmetic operators
    /// <summary>
    /// The addition (<c>__add_op</c>) binary operator static symbol.
    /// </summary>
    public sealed record class BinaryArithmeticAdditionOperatorSymbol() : BinaryArithmeticOperatorSymbol(OperatorSymbolNames.BinaryArithmeticAdditionOp) { }
    /// <summary>
    /// The division (<c>__div_op</c>) binary operator static symbol.
    /// </summary>
    public record class BinaryArithmeticDivisionOperatorSymbol() : BinaryArithmeticOperatorSymbol(OperatorSymbolNames.BinaryArithmeticDivisionOp) { }
    /// <summary>
    /// The exponentiation (<c>__exp_op</c>) binary operator static symbol.
    /// </summary>
    public record class BinaryArithmeticExponentOperatorSymbol() : BinaryArithmeticOperatorSymbol(OperatorSymbolNames.BinaryArithmeticExponentOp) { }
    /// <summary>
    /// The integer division (<c>__idv_op</c>) binary operator static symbol.
    /// </summary>
    public record class BinaryArithmeticIntegerDivisionOperatorSymbol() : BinaryArithmeticOperatorSymbol(OperatorSymbolNames.BinaryArithmeticIntegerDivisionOp) { }
    /// <summary>
    /// The modulo (<c>__mod_op</c>) binary operator static symbol.
    /// </summary>
    public record class BinaryArithmeticModuloOperatorSymbol() : BinaryArithmeticOperatorSymbol(OperatorSymbolNames.BinaryArithmeticModuloOp) { }
    /// <summary>
    /// The multiplication (<c>__mul_op</c>) binary operator static symbol.
    /// </summary>
    public record class BinaryArithmeticMultiplicationOperatorSymbol() : BinaryArithmeticOperatorSymbol(OperatorSymbolNames.BinaryArithmeticMultiplicationOp) { }
    /// <summary>
    /// The subtraction (<c>__sub_op</c>) binary operator static symbol.
    /// </summary>
    public record class BinaryArithmeticSubtractionOperatorSymbol() : BinaryArithmeticOperatorSymbol(OperatorSymbolNames.BinaryArithmeticSubtractionOp) { }

    /// <summary>
    /// The unary addition (<c>__ad1_op</c>) prefix operator static symbol.
    /// </summary>
    /// <remarks>
    /// NOTE: This operator is technically unspecified. Its static semantics are those of unary arithmetic operators (MS-VBAL 5.6.9.3), run-time semantics inferred from, well, common sense actually.
    /// </remarks>
    public record class UnaryArithmeticAdditionOperatorSymbol() : UnaryArithmeticOperatorSymbol(OperatorSymbolNames.UnaryArithmeticAdditionOp) { }
    /// <summary>
    /// The unary negation (<c>__ng1_op</c>) prefix operator static symbol.
    /// </summary>
    public record class UnaryArithmeticNegationOperatorSymbol() : UnaryArithmeticOperatorSymbol(OperatorSymbolNames.UnaryArithmeticNegationOp) { }
    /// <summary>
    /// The arithmetic precedence (<c>__p()_op</c>) operator static symbol.
    /// </summary>
    public record class UnaryArithmeticPrecedenceOperatorSymbol() : UnaryOperatorSymbol<UnaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags>(OperatorSymbolNames.UnaryArithmeticPrecedenceOp) { }
    #endregion

    /// <summary>
    /// The concatenation (<c>__cat_op</c>) binary operator static symbol.
    /// </summary>
    public record class BinaryStringConcatOperatorSymbol() : BinaryOperatorSymbol<ConcatOperationSemanticFlags>(OperatorSymbolNames.BinaryStringConcatOp) { }

    #region Bitwise (logical) operators
    /// <summary>
    /// The bitwise/logical <c>And</c> (<c>__and_op</c>) operator static symbol.
    /// </summary>
    public record class BinaryBitwiseAndOperatorSymbol() : BinaryOperatorSymbol<LogicalOperatorSemanticFlags>(OperatorSymbolNames.BinaryBitwiseAndOp) { }
    /// <summary>
    /// The bitwise/logical <c>Or</c> (<c>___or_op</c>) operator static symbol.
    /// </summary>
    public record class BinaryBitwiseOrOperatorSymbol() : BinaryOperatorSymbol<LogicalOperatorSemanticFlags>(OperatorSymbolNames.BinaryBitwiseOrOp) { }
    /// <summary>
    /// The bitwise/logical <c>XOr</c> (<c>__xor_op</c>) operator static symbol.
    /// </summary>
    public record class BinaryBitwiseXOrOperatorSymbol() : BinaryOperatorSymbol<LogicalOperatorSemanticFlags>(OperatorSymbolNames.BinaryBitwiseXOrOp) { }
    /// <summary>
    /// The bitwise/logical <c>Imp</c> (<c>__imp_op</c>) operator static symbol.
    /// </summary>
    public record class BinaryBitwiseImpOperatorSymbol() : BinaryOperatorSymbol<LogicalOperatorSemanticFlags>(OperatorSymbolNames.BinaryBitwiseImpOp) { }
    /// <summary>
    /// The bitwise/logical <c>Eqv</c> (<c>__eqv_op</c>) operator static symbol.
    /// </summary>
    public record class BinaryBitwiseEqvOperatorSymbol() : BinaryOperatorSymbol<LogicalOperatorSemanticFlags>(OperatorSymbolNames.BinaryBitwiseEqvOp) { }
    #endregion

    #region Comparison operators
    /// <summary>
    /// The value equality (<c>___eq_op</c>) comparison operator static symbol.
    /// </summary>
    public record class BinaryCompareEqOperatorSymbol() : BinaryOperatorSymbol<ComparisonOperatorSemanticFlags>(OperatorSymbolNames.BinaryCompareEqOp) { }
    /// <summary>
    /// The value inequality (<c>__neq_op</c>) comparison operator static symbol.
    /// </summary>
    public record class BinaryCompareNeqOperatorSymbol() : BinaryOperatorSymbol<ComparisonOperatorSemanticFlags>(OperatorSymbolNames.BinaryCompareNeqOp) { }
    /// <summary>
    /// The <em>less than</em> (<c>___lt_op</c>) comparison operator static symbol.
    /// </summary>
    public record class BinaryCompareLtOperatorSymbol() : BinaryOperatorSymbol<ComparisonOperatorSemanticFlags>(OperatorSymbolNames.BinaryCompareLtOp) { }
    /// <summary>
    /// The <em>greater than</em> (<c>___gt_op</c>) comparison operator static symbol.
    /// </summary>
    public record class BinaryCompareGtOperatorSymbol() : BinaryOperatorSymbol<ComparisonOperatorSemanticFlags>(OperatorSymbolNames.BinaryCompareGtOp) { }
    /// <summary>
    /// The <em>less than or equal</em> (<c>__lte_op</c>) comparison operator static symbol.
    /// </summary>
    public record class BinaryCompareLtEqOperatorSymbol() : BinaryOperatorSymbol<ComparisonOperatorSemanticFlags>(OperatorSymbolNames.BinaryCompareLtEqOp) { }
    /// <summary>
    /// The <em>greater than or equal</em> (<c>__gte_op</c>) comparison operator static symbol.
    /// </summary>
    public record class BinaryCompareGtEqOperatorSymbol() : BinaryOperatorSymbol<ComparisonOperatorSemanticFlags>(OperatorSymbolNames.BinaryCompareGtEqOp) { }
    /// <summary>
    /// The <c>Like</c> (<c>__lik_op</c>) string comparison operator static symbol.
    /// </summary>
    public record class BinaryCompareLikeOperatorSymbol(): BinaryOperatorSymbol<ComparisonOperatorSemanticFlags>(OperatorSymbolNames.BinaryCompareLikeOp) { }
    /// <summary>
    /// The <c>Is</c> (<c>___is_op</c>) reference equality comparison operator static symbol.
    /// </summary>
    public record class BinaryCompareIsOperatorSymbol() : BinaryOperatorSymbol<ComparisonOperatorSemanticFlags>(OperatorSymbolNames.BinaryCompareIsOp) { }
    #endregion
    /// <summary>
    /// The <c>.</c> (<c>__bma_op</c>) member access operator static symbol.
    /// </summary>
    public record class BinaryMemberAccessOperatorSymbol() : BinaryOperatorSymbol<MemberAccessOperationSemanticFlags>(OperatorSymbolNames.BinaryMemberAccessOp) { }

    /// <summary>
    /// The bitwise/logical <c>Not</c> (<c>__not_op</c>) operator static symbol.
    /// </summary>
    public record class UnaryBitwiseNotOperatorSymbol() : UnaryLogicalOperatorSymbol(OperatorSymbolNames.UnaryBitwiseNotOp) { }

    /// <summary>
    /// The unary let-coercion (<c>__c()_op</c>) operator static symbol.
    /// </summary>
    public record class BinaryLetCoercionOperatorSymbol() : BinaryOperatorSymbol<ConversionSemanticFlags>(OperatorSymbolNames.UnaryLetCoerceOp) { }
}
