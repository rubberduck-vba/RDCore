using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Expressions;

// MS-VBAL §5.6.11
public sealed record class InstanceExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<InstanceExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static InstanceExpressionStaticSemantics Instance => _instance.Value;

    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not InstanceExpressionNode)
        {
            throw new ArgumentException($"Expected an {nameof(InstanceExpressionNode)}.", nameof(expression));
        }

        var moduleScope = context.Scope.SelfAndAncestors().FirstOrDefault(scope => scope.Kind == LexicalScopeKind.Module);
        var moduleName = moduleScope is null ? null : SymbolUri.ModuleNameOf(moduleScope.Uri);
        // looked up from the global scope, in the type binding context: a module is bound by its own name
        // there, so a member of the class named like the class cannot hide it.
        var module = moduleName is null ? null : context.Resolver.ResolveType(moduleName, ScopeKind.Global, StaticSymbol.GlobalUri).Symbol;

        return module is VBClassModuleSymbol classModule
            ? StaticSemanticsEvaluationResult.Success(VBClassType.Of(classModule))
            : StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.InvalidUseOfMe, expression.Location,
                "'Me' is only valid within a class module."));
    }
}
