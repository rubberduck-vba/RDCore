using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Platform;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Services.Refactoring;

public readonly record struct ParameterHint(SourcePosition Position, string Name);

public static class ParameterHints
{
    public static IReadOnlyList<ParameterHint> In(RenameContext context, Uri module, SourceRange range)
    {
        // the static pass recurses once per level of an expression: one large-stack thread for the whole call, not one per callee.
        if (!LargeStackThread.IsCurrent)
        {
            return LargeStackThread.Run(() => In(context, module, range));
        }

        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } source)
        {
            return [];
        }

        var bindingOf = NameOccurrences.BindingsOf(context, source);
        var hints = new List<ParameterHint>();
        foreach (var node in source.Syntax.Descendants())
        {
            var (callee, arguments) = node switch
            {
                IndexExpressionNode index => (index.Callee, index.Arguments),
                CallStatementNode call => (call.Callee, call.Arguments),
                _ => (null, []),
            };

            if (callee is null || arguments.IsDefaultOrEmpty || !Intersects(node.SourceLocation.Range, range)
                || CalleeOf(context, source, callee, bindingOf) is not { } procedure || SignatureDescription.Of(procedure) is not { Parameters.Count: > 0 } signature)
            {
                continue;
            }

            for (var position = 0; position < arguments.Length; position++)
            {
                var argument = arguments[position];
                if (argument is NamedArgumentNode)
                {
                    break;
                }

                // past the parameters, an argument is one of the ParamArray's after its first, or one there is no parameter for.
                if (position >= signature.Parameters.Count)
                {
                    if (!signature.Parameters[^1].IsParamArray)
                    {
                        break;
                    }

                    continue;
                }

                var parameter = signature.Parameters[position];

                if (argument.Location.Range.IsEmpty
                    || argument is SimpleNameExpressionNode { IdentifierName: var name } && string.Equals(name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                hints.Add(new ParameterHint(argument.Location.Range.Start, parameter.Name));
            }
        }

        return [.. hints.OrderBy(hint => hint.Position)];
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

    private static bool Intersects(SourceRange node, SourceRange range) => node.Start <= range.End && range.Start <= node.End;
}
