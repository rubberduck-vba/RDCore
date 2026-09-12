using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static.Expressions;

/// <summary>
/// MS-VBAL 5.6.10 Simple Name Expressions (static semantics).
/// The declared type of a <em>simple name expression</em> is the declared type of the entity its
/// identifier resolves to (<strong>RD-VBAL §2.3.1.2</strong> for the resolution order itself).
/// </summary>
public sealed record class SimpleNameExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<SimpleNameExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static SimpleNameExpressionStaticSemantics Instance => _instance.Value;

    /// <summary>
    /// Determines the declared type of a <see cref="SimpleNameExpressionNode"/> from the entity its
    /// identifier resolves to in <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The compile-time context this expression is evaluated against.</param>
    /// <param name="expression">The <see cref="SimpleNameExpressionNode"/> being evaluated.</param>
    /// <param name="operandDeclaredTypes">Unused — a simple name has no operands.</param>
    /// <exception cref="ArgumentException"><paramref name="expression"/> is not a <see cref="SimpleNameExpressionNode"/>.</exception>
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not SimpleNameExpressionNode simpleName)
        {
            throw new ArgumentException($"Expected a {nameof(SimpleNameExpressionNode)}.", nameof(expression));
        }

        var result = context.Resolver.Resolve(simpleName.IdentifierName, ScopeKind.Local, context.Scope.Uri);
        if (result.IsError)
        {
            return StaticSemanticsEvaluationResult.Error(GetResolutionErrorInfo(expression, simpleName.IdentifierName, result.ErrorId!.Value, result.Candidates));
        }

        if (result.IsResolved)
        {
            return StaticSemanticsEvaluationResult.Success(result.Symbol is ITypedSymbol typed ? typed.ResolvedType : VBUnknownType.TypeInfo);
        }

        // unbound: MS-VBAL 5.6.10 leaves this to Option Explicit (RD-VBAL §5.2.1.3). Under Explicit
        // it's a compile error; otherwise it's IVBInferableType's job to narrow the type from use —
        // this rule only ever answers VBUnknownType.
        return context.Scope.EnclosingModuleDirectives()?.Explicit == true
            ? StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.VariableNotDefined, expression.Location, simpleName.IdentifierName))
            : StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
    }

    private static VBCompileErrorInfo GetResolutionErrorInfo(
        ExpressionNode expression, string name, VBCompileErrorId errorId, ImmutableArray<Symbol> candidates)
        => VBCompileErrorInfo.For(errorId, expression.Location,
            $"'{name}' — {candidates.Length} candidates: {string.Join(", ", candidates.Select(candidate => candidate.ParentUri.Fragment.TrimStart('#')))}");
}
