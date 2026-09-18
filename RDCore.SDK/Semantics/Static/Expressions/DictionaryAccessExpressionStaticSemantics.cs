using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Expressions;

/// <summary>
/// MS-VBAL 5.6.14 Dictionary Access Expressions (static semantics). <c>owner!member</c> is
/// syntactically an alternate way to invoke <see cref="DictionaryAccessExpressionNode.Owner"/>'s
/// default member with a String argument — its declared type is that default member's declared type.
/// </summary>
public sealed record class DictionaryAccessExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<DictionaryAccessExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// The shared instance — this rule has no state of its own.
    /// </summary>
    public static DictionaryAccessExpressionStaticSemantics Instance => _instance.Value;

    /// <summary>
    /// Determines the declared type of a <see cref="DictionaryAccessExpressionNode"/> from its
    /// <see cref="DictionaryAccessExpressionNode.Owner"/>'s already-determined declared type.
    /// </summary>
    /// <param name="context">
    /// The compile-time context this expression is evaluated against. Unused — member lookup here is
    /// structural (the owner type's declared default member), not lexical.
    /// </param>
    /// <param name="expression">The <see cref="DictionaryAccessExpressionNode"/> being evaluated.</param>
    /// <param name="operandDeclaredTypes">
    /// The effective owner's already-determined declared type, at
    /// <see cref="InputIndex.DictionaryAccessOwner"/> — <see cref="DictionaryAccessExpressionNode.Owner"/>'s
    /// declared type, or, for a <c>with-expression</c> (<see cref="DictionaryAccessExpressionNode.Owner"/>
    /// is <c>null</c>), the innermost enclosing <c>With</c> block's target type (MS-VBAL §5.6.15),
    /// which the caller is responsible for substituting in — this rule doesn't care which it was.
    /// <see cref="DictionaryAccessExpressionNode.Member"/> has no independent declared type — it's the
    /// bare name passed as a String argument to the owner's default member, not a lexically resolved
    /// expression.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="expression"/> is not a <see cref="DictionaryAccessExpressionNode"/>.</exception>
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not DictionaryAccessExpressionNode)
        {
            throw new ArgumentException($"Expected a {nameof(DictionaryAccessExpressionNode)}.", nameof(expression));
        }

        var owner = operandDeclaredTypes[(int)InputIndex.DictionaryAccessOwner];
        if (owner is VBVariantType or VBObjectType)
        {
            // syntactically translated into an index expression over Owner with a single String
            // argument; over a Variant/Object l-expression that's an unbound member, declared Variant.
            return StaticSemanticsEvaluationResult.Success(VBVariantType.TypeInfo);
        }

        if (owner is not VBClassType classType)
        {
            // "invalid if the declared type of l-expression is a type other than a specific class,
            // Object or Variant" (MS-VBAL 5.6.14). VBUnknownType (not yet resolved/modeled) can't be
            // shown to violate this, so it's deferred rather than flagged.
            return owner is VBUnknownType
                ? StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo)
                : StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                    $"'{owner.Name}' is not a specific class, Object, or Variant."));
        }

        return classType.DefaultMember is { } defaultMember
            ? StaticSemanticsEvaluationResult.Success(defaultMember.ResolvedType)
            : StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.MethodOrDataMemberNotFound, expression.Location, classType.Name));
    }
}
