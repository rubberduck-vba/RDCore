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
/// <strong>MS-VBAL 5.6.11</strong> Instance Expressions (static semantics). The declared type of
/// <c>Me</c> is the type defined by the class module containing the enclosing procedure; using it
/// outside a class module is invalid.
/// </summary>
public sealed record class InstanceExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<InstanceExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static InstanceExpressionStaticSemantics Instance => _instance.Value;

    /// <summary>
    /// Determines the declared type of an <see cref="InstanceExpressionNode"/> — the enclosing class
    /// module's own type. Unlike <see cref="SimpleNameExpressionNode"/>, <c>Me</c> is never resolved
    /// by name: it identifies the module enclosing <paramref name="context"/>'s own scope directly.
    /// </summary>
    /// <param name="context">The compile-time context this expression is evaluated against.</param>
    /// <param name="expression">The <see cref="InstanceExpressionNode"/> being evaluated.</param>
    /// <param name="operandDeclaredTypes">Unused — an instance expression has no operands.</param>
    /// <exception cref="ArgumentException"><paramref name="expression"/> is not an <see cref="InstanceExpressionNode"/>.</exception>
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not InstanceExpressionNode)
        {
            throw new ArgumentException($"Expected an {nameof(InstanceExpressionNode)}.", nameof(expression));
        }

        var moduleScope = context.Scope.SelfAndAncestors().FirstOrDefault(scope => scope.Kind == LexicalScopeKind.Module);
        var moduleName = moduleScope?.Uri.Fragment.TrimStart('#');
        // looked up from the global scope, in the type binding context: a module is bound by its own name
        // there, so a member of the class named like the class cannot hide it.
        var module = moduleName is null ? null : context.Resolver.ResolveType(moduleName, ScopeKind.Global, StaticSymbol.GlobalUri).Symbol;

        return module is VBClassModuleSymbol classModule
            ? StaticSemanticsEvaluationResult.Success(new VBClassType(classModule, classModule.DefaultInterfaceMembers))
            : StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.InvalidUseOfMe, expression.Location,
                "'Me' is only valid within a class module."));
    }
}
