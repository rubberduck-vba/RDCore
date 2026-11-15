using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Platform;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;

namespace RDCore.LanguageServer.Services.Refactoring;

// the hints of a module of a workspace: the SDK's, with the procedures its calls call bound as the static pass binds them.
public static class InlayHints
{
    public static IReadOnlyList<SourceInlayHint> In(RenameContext context, Uri module, SourceRange range, SdkInlayHintOptions sites)
    {
        // the static pass recurses once per level of an expression: one large-stack thread for the whole module, not one per callee.
        if (!LargeStackThread.IsCurrent)
        {
            return LargeStackThread.Run(() => In(context, module, range, sites));
        }

        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } source)
        {
            return [];
        }

        var bindingOf = sites.Arguments ? NameOccurrences.BindingsOf(context, source) : null;
        return SourceInlayHints.In(source.Syntax, range, sites,
            callee => bindingOf is not null && CalleeOf(context, source, callee, bindingOf) is { } procedure ? SignatureDescription.Of(procedure) : null);
    }

    // the procedure a call calls: the symbol the static pass binds its name to, or the one its name resolves to in its scope.
    private static Symbol? CalleeOf(RenameContext context, RenameModule module, ExpressionNode callee, Func<ExpressionNode, SemanticId?> bindingOf)
    {
        if (bindingOf(callee) is { } binding && NameOccurrences.SymbolOf(context, binding) is { } declared)
        {
            return declared;
        }

        if (callee is not SimpleNameExpressionNode name)
        {
            return null;
        }

        var scope = context.ScopeTree.ScopeFor(CompletionCandidates.OriginAt(context.ScopeTree, module.Module, callee.Location.Range.Start));
        return context.Resolver.ResolveValue(name.IdentifierName, ScopeKind.Global, scope.Uri).Symbol;
    }
}
