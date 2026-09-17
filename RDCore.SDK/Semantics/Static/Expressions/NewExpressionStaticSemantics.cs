using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Expressions;

/// <summary>
/// <strong>MS-VBAL 5.6.8</strong> New Expressions (static semantics). A <c>New</c> expression is
/// invalid if the type referenced by its <c>TypeExpression</c> is not instantiable.
/// </summary>
public sealed record class NewExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<NewExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static NewExpressionStaticSemantics Instance => _instance.Value;

    /// <summary>
    /// Determines the declared type of a <see cref="NewExpressionNode"/> — the type referenced by its
    /// <c>TypeExpression</c>, if that reference is an instantiable class module.
    /// </summary>
    /// <param name="context">The compile-time context this expression is evaluated against.</param>
    /// <param name="expression">The <see cref="NewExpressionNode"/> being evaluated.</param>
    /// <param name="operandDeclaredTypes">Unused — <c>TypeExpression</c> names a type, not a value.</param>
    /// <exception cref="ArgumentException"><paramref name="expression"/> is not a <see cref="NewExpressionNode"/>.</exception>
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not NewExpressionNode newExpression)
        {
            throw new ArgumentException($"Expected a {nameof(NewExpressionNode)}.", nameof(expression));
        }

        if (newExpression.TypeExpression is not SimpleNameExpressionNode simpleName)
        {
            // a qualified type-expression (New Project.ClassName) isn't modeled yet — defer rather
            // than misreport an instantiability error for a shape this rule doesn't understand.
            return StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
        }

        var result = context.Resolver.Resolve(simpleName.IdentifierName, ScopeKind.Local, context.Scope.Uri);
        if (result.IsError)
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(result.ErrorId!.Value, expression.Location,
                $"'{simpleName.IdentifierName}' — {result.Candidates.Length} candidates: {string.Join(", ", result.Candidates.Select(candidate => candidate.ParentUri.Fragment.TrimStart('#')))}"));
        }

        if (result.Symbol is VBClassModuleSymbol classModule)
        {
            return StaticSemanticsEvaluationResult.Success(new VBClassType(classModule, classModule.Members));
        }

        // resolved to something that isn't a class (TypeMismatch), or didn't resolve at all
        // (UserDefinedTypeNotDefined) - either way, MS-VBAL 5.6.8 requires an instantiable class.
        var errorId = result.IsResolved ? VBCompileErrorId.TypeMismatch : VBCompileErrorId.UserDefinedTypeNotDefined;
        return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(errorId, expression.Location,
            $"'{simpleName.IdentifierName}' does not reference an instantiable class."));
    }
}
