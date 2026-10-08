using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Runtime.Abstract.Execution;
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

        var result = context.Resolver.ResolveValue(simpleName, ScopeKind.Local, context.Scope.Uri);
        if (result.IsError)
        {
            return StaticSemanticsEvaluationResult.Error(GetResolutionErrorInfo(expression, simpleName.WrittenName, result.ErrorId!.Value, result.Candidates));
        }

        if (result.IsResolved)
        {
            if (TypeDeclarationCharacterMismatch(simpleName, result.Symbol!) is { } mismatch)
            {
                return StaticSemanticsEvaluationResult.Error(mismatch);
            }

            return StaticSemanticsEvaluationResult.Success(result.Symbol is ITypedSymbol typed ? typed.ResolvedType : VBUnknownType.TypeInfo);
        }

        // unbound: MS-VBAL 5.6.10 leaves this to Option Explicit (RD-VBAL §5.2.1.3). Under Explicit
        // it's a compile error; otherwise it's IVBInferableType's job to narrow the type from use —
        // this rule only ever answers VBUnknownType.
        return context.Scope.EnclosingModuleDirectives()?.Explicit == true
            ? StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.VariableNotDefined, expression.Location, simpleName.WrittenName))
            : StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
    }

    // A type-declaration character says the type of the name it is written on, and the name was declared as a type of its own: <c>x$</c> of a variable declared
    // As Long is an error, at the name (and not at the statement it is in). A name that was found as written - Left$ is a member of the library, with the
    // character in its name - was never declared with the character, and says nothing about it. What is not known yet (a type still to be inferred) cannot
    // be told apart from the character, and is not.
    private static VBCompileErrorInfo? TypeDeclarationCharacterMismatch(SimpleNameExpressionNode name, Symbol symbol)
    {
        if (name.TypeHint is not { Length: > 0 } hint
            || string.Equals(symbol.Name, name.WrittenName, StringComparison.OrdinalIgnoreCase)
            || symbol is not ITypedSymbol { ResolvedType: var declared }
            || declared is VBUnknownType
            || !IntrinsicVBTypes.TryResolveTypeHint(hint, out var hinted)
            || hinted.Equals(declared))
        {
            return null;
        }

        return VBCompileErrorInfo.For(VBCompileErrorId.TypeDeclarationCharacterDoesNotMatch, name.Location,
            $"'{name.WrittenName}': '{hint}' is {hinted.Name}, and '{name.IdentifierName}' is declared as {declared.Name}.");
    }

    internal static VBCompileErrorInfo GetResolutionErrorInfo(
        ExpressionNode expression, string name, VBCompileErrorId errorId, ImmutableArray<Symbol> candidates)
        => VBCompileErrorInfo.For(errorId, expression.Location,
            $"'{name}' — {candidates.Length} candidates: {string.Join(", ", candidates.Select(candidate => candidate.ParentUri.Fragment.TrimStart('#')))}");
}
