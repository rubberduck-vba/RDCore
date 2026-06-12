namespace RDCore.SDK.Model.Symbols.Operators;

/// <summary>
/// Defines the internal operators symbol names.
/// </summary>
/// <remarks>
/// These names are intentionally illegal VBA identifier names, 
/// and they should all match the regular expression <c>"___?(?&lt;opname&gt;.{2,3})_op"</c>.
/// </remarks>
public static class OperatorSymbolNames
{
    public const string BinaryArithmeticAdditionOp = "__add_op";
    public const string BinaryArithmeticDivisionOp = "__div_op";
    public const string BinaryArithmeticExponentOp = "__exp_op";
    public const string BinaryArithmeticIntegerDivisionOp = "__idv_op";
    public const string BinaryArithmeticModuloOp = "__mod_op";
    public const string BinaryArithmeticMultiplicationOp = "__mul_op";
    public const string BinaryArithmeticSubtractionOp = "__sub_op";

    public const string BinaryAssignmentValueOp = "__let_op";
    public const string BinaryAssignmentReferenceOp = "__set_op";

    public const string BinaryBitwiseAndOp = "__and_op";
    public const string BinaryBitwiseEqvOp = "__eqv_op";
    public const string BinaryBitwiseImpOp = "__imp_op";
    public const string BinaryBitwiseOrOp = "___or_op";
    public const string BinaryBitwiseXOrOp = "__xor_op";

    public const string BinaryCompareEqOp = "___eq_op";
    public const string BinaryCompareNeqOp = "__neq_op";
    public const string BinaryCompareLtOp = "___lt_op";
    public const string BinaryCompareLtEqOp = "__lte_op";
    public const string BinaryCompareGtOp = "___gt_op";
    public const string BinaryCompareGtEqOp = "__gte_op";
    public const string BinaryCompareLikeOp = "__lik_op";
    public const string BinaryCompareIsOp = "___is_op";

    public const string BinaryDictionaryAccessOp = "__bda_op";
    public const string BinaryMemberAccessOp = "__bma_op";
    public const string BinaryStringConcatOp = "__cat_op";

    public const string UnaryArithmeticAdditionOp = "__ad1_op";
    public const string UnaryArithmeticNegationOp = "__ng1_op";
    public const string UnaryArithmeticPrecedenceOp = "__p()_op";

    public const string UnaryBitwiseNotOp = "__not_op";
    public const string UnaryLetCoerceOp = "__c()_op";
}
