using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Expressions;

/// <summary>
/// MS-VBAL 5.6.13 Index Expressions (static semantics). <c>callee(arguments)</c> is either an array
/// element access, a call to a specific class's default member, or a call to whatever
/// <see cref="IndexExpressionNode.Callee"/> itself already resolves to (a function or property-get
/// reference, whose declared type already <em>is</em> its return type — see
/// <see cref="SimpleNameExpressionStaticSemantics"/> and <see cref="MemberAccessExpressionStaticSemantics"/>).
/// </summary>
public sealed record class IndexExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<IndexExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// The shared instance — this rule has no state of its own.
    /// </summary>
    public static IndexExpressionStaticSemantics Instance => _instance.Value;

    /// <summary>
    /// Determines the declared type of an <see cref="IndexExpressionNode"/> from its
    /// <see cref="IndexExpressionNode.Callee"/>'s already-determined declared type.
    /// </summary>
    /// <param name="context">
    /// The compile-time context this expression is evaluated against. Unused — resolution here is
    /// structural (the callee's declared type), not lexical.
    /// </param>
    /// <param name="expression">The <see cref="IndexExpressionNode"/> being evaluated.</param>
    /// <param name="operandDeclaredTypes">
    /// <see cref="IndexExpressionNode.Callee"/>'s already-determined declared type, at
    /// <see cref="InputIndex.IndexExpressionCallee"/>. <see cref="IndexExpressionNode.Arguments"/>
    /// carry no independent declared type this rule needs — MS-VBAL 5.6.13's argument-count/rank and
    /// parameter-list compatibility checks are not modeled: <see cref="VBArrayType"/> doesn't carry a
    /// rank, and no parameter-list representation exists for a resolved function/property-get type.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="expression"/> is not an <see cref="IndexExpressionNode"/>.</exception>
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not IndexExpressionNode)
        {
            throw new ArgumentException($"Expected an {nameof(IndexExpressionNode)}.", nameof(expression));
        }

        var callee = operandDeclaredTypes[(int)InputIndex.IndexExpressionCallee];
        if (callee is VBVariantType or VBObjectType)
        {
            // late-bound: classified as an unbound member with a declared type of Variant.
            return StaticSemanticsEvaluationResult.Success(VBVariantType.TypeInfo);
        }

        if (callee is VBArrayType array)
        {
            // classified as a variable with the array's element type. MS-VBAL 5.6.13 also requires the
            // argument count to match the array's rank (or be empty) — VBArrayType carries no rank.
            return StaticSemanticsEvaluationResult.Success(array.ItemType);
        }

        if (callee is VBClassType classType)
        {
            // a specific class needs a public default member to be callable this way.
            return classType.DefaultMember is { } defaultMember
                ? StaticSemanticsEvaluationResult.Success(defaultMember.ResolvedType)
                : StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.MethodOrDataMemberNotFound, expression.Location, classType.Name));
        }

        // callee is already classified as a property/function/subroutine reference (its declared type
        // is already that member's return type) or is not yet known (VBUnknownType): the index
        // expression takes on that same classification and declared type, unchanged.
        return StaticSemanticsEvaluationResult.Success(callee);
    }
}
