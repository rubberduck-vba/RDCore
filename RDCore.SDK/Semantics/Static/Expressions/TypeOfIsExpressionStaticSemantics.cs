using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Expressions;

/// <summary>
/// MS-VBAL 5.6.7 TypeOf...Is Expressions (static semantics). Always classified as a value with a
/// declared type of Boolean; <see cref="TypeOfIsExpressionNode.Operand"/> must have a declared type
/// of a specific UDT, a specific class, Object, or Variant.
/// </summary>
public sealed record class TypeOfIsExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<TypeOfIsExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// The shared instance — this rule has no state of its own.
    /// </summary>
    public static TypeOfIsExpressionStaticSemantics Instance => _instance.Value;

    /// <summary>
    /// Determines the declared type of a <see cref="TypeOfIsExpressionNode"/> — always
    /// <see cref="VBBooleanType"/>, provided <see cref="TypeOfIsExpressionNode.Operand"/>'s declared
    /// type satisfies MS-VBAL 5.6.7's classification requirement.
    /// </summary>
    /// <param name="context">
    /// The compile-time context this expression is evaluated against. Unused — validity here depends
    /// only on the operand's own declared type.
    /// </param>
    /// <param name="expression">The <see cref="TypeOfIsExpressionNode"/> being evaluated.</param>
    /// <param name="operandDeclaredTypes">
    /// <see cref="TypeOfIsExpressionNode.Operand"/>'s already-determined declared type, at
    /// <see cref="InputIndex.TypeOfIsOperand"/>. <see cref="TypeOfIsExpressionNode.TypeExpression"/>
    /// names a type, not a value — MS-VBAL 5.6.7 places no static requirement on it, only on the
    /// operand's classification.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="expression"/> is not a <see cref="TypeOfIsExpressionNode"/>.</exception>
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not TypeOfIsExpressionNode)
        {
            throw new ArgumentException($"Expected a {nameof(TypeOfIsExpressionNode)}.", nameof(expression));
        }

        var operand = operandDeclaredTypes[(int)InputIndex.TypeOfIsOperand];

        // VBUnknownType (not yet resolved/modeled) can't be shown to violate the classification
        // requirement, so it's deferred rather than flagged — same convention as every other rule.
        if (operand is VBUserDefinedType or VBClassType or VBObjectType or VBVariantType or VBUnknownType)
        {
            return StaticSemanticsEvaluationResult.Success(VBBooleanType.TypeInfo);
        }

        return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
            $"'{operand.Name}' is not a UDT, class, Object, or Variant."));
    }
}
