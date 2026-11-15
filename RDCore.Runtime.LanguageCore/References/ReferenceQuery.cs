using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Platform;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using RDCore.SDK.Semantics.References;
using RDCore.Runtime.LanguageCore.Refactoring;

namespace RDCore.Runtime.LanguageCore.References;

// a name is bound the way a rename binds it, so what is found is what a rename renames.
// each module's static semantics are evaluated once for the query, on one large-stack thread.
public static class ReferenceQuery
{
    public static SymbolReferences? At(RenameContext context, Uri module, SourcePosition position)
        => RenameBinding.SymbolAt(context, module, position) is { } named ? Of(context, named.Symbol) : null;

    public static SymbolReferences Of(RenameContext context, Symbol symbol)
    {
        if (!LargeStackThread.IsCurrent)
        {
            return LargeStackThread.Run(() => Of(context, symbol));
        }

        var together = RenameBinding.DefinedTogether(context, symbol).ToList();
        var symbols = together.Select(defined => defined.SemanticId).ToHashSet();
        var isType = symbol is VBModuleSymbol or VBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol;
        var found = NameOccurrences.Find(context, symbol.Name, context.Modules);

        var indexes = new Dictionary<string, ModuleIndex>(StringComparer.Ordinal);
        ModuleIndex IndexOf(Uri module)
        {
            if (!indexes.TryGetValue(module.AbsoluteUri, out var index))
            {
                index = new ModuleIndex(context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri));
                indexes[module.AbsoluteUri] = index;
            }

            return index;
        }

        var references = new List<SymbolReference>();
        var lateBound = new List<NameOccurrence>();
        foreach (var occurrence in found.Occurrences)
        {
            // a member of an Object or a Variant is bound when the code runs: what it refers to is not known.
            if (RenameBinding.IsLateBound(occurrence))
            {
                lateBound.Add(occurrence);
                continue;
            }

            // a name that means no value where it is written is the type it names: a class that is created with New.
            var refers = occurrence.Binding is { } binding
                ? symbols.Contains(binding)
                : isType && occurrence.Scope is { } scope && RenameBinding.IsTheType(context, symbol, scope.Uri);
            if (refers)
            {
                references.Add(ReferenceOf(context, IndexOf(occurrence.Module), occurrence) with { Symbol = occurrence.Binding ?? symbol.SemanticId });
            }
        }

        if (isType)
        {
            foreach (var source in context.Modules.Where(candidate => candidate.Source.Contains(symbol.Name, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var (name, range) in NameOccurrences.TypeNamesIn(source))
                {
                    if (string.Equals(name, symbol.Name, StringComparison.OrdinalIgnoreCase)
                        && RenameBinding.IsTheType(context, symbol, NameOccurrences.ScopeOf(context, source, range.Start).Uri))
                    {
                        references.Add(new SymbolReference(source.Module, range, ReferenceForm.TypeName, ReferenceUse.None) { Symbol = symbol.SemanticId });
                    }
                }
            }
        }

        // a name written as the symbol's whose meaning is not known, where the symbol could be what it means, as a rename refuses it.
        var unknown = found.Unknown
            .Where(occurrence => occurrence.Kind == OccurrenceKind.String
                ? RenameBinding.IsCalledBy(context, together, occurrence)
                : occurrence.Kind == OccurrenceKind.Member || together.Any(defined => RenameBinding.ChainIndex(occurrence.Scope, defined) < int.MaxValue))
            .Concat(lateBound.Where(occurrence => RenameBinding.MayBeCalledLateBound(context, together, occurrence)))
            .Select(occurrence => ReferenceOf(context, IndexOf(occurrence.Module), occurrence));
        return new SymbolReferences(symbol, [.. symbols], Ordered(references), Ordered(unknown));
    }


    private static IReadOnlyList<SymbolReference> Ordered(IEnumerable<SymbolReference> references)
        => [.. references
            .DistinctBy(reference => (reference.Module.AbsoluteUri, reference.Range))
            .OrderBy(reference => reference.Module.AbsoluteUri, StringComparer.Ordinal)
            .ThenBy(reference => reference.Range.Start)];

    // how the occurrence names its symbol: its form and its use, and the call it is called with or is an argument of.
    private static SymbolReference ReferenceOf(RenameContext context, ModuleIndex index, NameOccurrence occurrence)
    {
        var form = occurrence.Kind switch
        {
            OccurrenceKind.Declaration => ReferenceForm.Declaration,
            OccurrenceKind.Name => ReferenceForm.Name,
            OccurrenceKind.Member => occurrence.Node is MemberAccessExpressionNode { Owner: null } ? ReferenceForm.WithMember : ReferenceForm.Member,
            OccurrenceKind.NamedArgument => ReferenceForm.NamedArgument,
            OccurrenceKind.Attribute => ReferenceForm.Attribute,
            _ => ReferenceForm.String,
        };

        var isExpression = form is ReferenceForm.Name or ReferenceForm.Member or ReferenceForm.WithMember;
        var reference = new SymbolReference(occurrence.Module, occurrence.Range, form, isExpression ? index.AccessOf(occurrence.Range.Start) : ReferenceUse.None)
        {
            Owner = form == ReferenceForm.Member ? occurrence.Owner : null,
            Qualifier = occurrence.Qualifier,
        };

        if (!isExpression || occurrence.Node is not ExpressionNode expression)
        {
            return reference;
        }

        var parent = index.ParentOf(expression);
        var call = parent switch
        {
            IndexExpressionNode called when ReferenceEquals(called.Callee, expression) => (SyntaxNode)called,
            CallStatementNode called when ReferenceEquals(called.Callee, expression) => called,
            _ => null,
        };

        reference = reference with { Call = call };
        return ArgumentOf(index, expression, parent) is { } argument
            ? reference with
            {
                ArgumentOf = argument.Call,
                MayBePassedByRef = !argument.IsByVal && MayBePassedByRef(context, index, occurrence, expression, argument),
            }
            : reference;
    }

    // the call the expression is a whole argument of - by itself, as the value of a named argument, or after a call-site ByVal - when it is one: not an
    // index of an array, which the static pass flags no argument (ValueExpressionSemanticFlags.PassedAsArgument).
    private static Argument? ArgumentOf(ModuleIndex index, ExpressionNode expression, SyntaxNode? parent)
    {
        var isByVal = false;
        ExpressionNode written = expression;
        if (parent is ByValArgumentExpressionNode byVal && ReferenceEquals(byVal.Operand, expression))
        {
            (isByVal, written, parent) = (true, byVal, index.ParentOf(byVal));
        }

        string? name = null;
        if (parent is NamedArgumentNode named && ReferenceEquals(named.Value, written))
        {
            (name, written, parent) = (named.Name, named, index.ParentOf(named));
        }

        var (callee, arguments) = parent switch
        {
            IndexExpressionNode called => (called.Callee, called.Arguments),
            CallStatementNode called => (called.Callee, called.Arguments),
            _ => (null, ImmutableArray<ExpressionNode>.Empty),
        };

        var position = arguments.IndexOf(written, ReferenceEqualityComparer.Instance);
        if (callee is null || position < 0)
        {
            return null;
        }

        return new Argument(parent!, callee, position, name, isByVal);
    }

    // whether the call may write to what the argument names: the static pass flags it passed, it is not a copy of an object's variable, and the parameter it is
    // given to, when the procedure called is known, is not ByVal.
    private static bool MayBePassedByRef(RenameContext context, ModuleIndex index, NameOccurrence occurrence, ExpressionNode expression, Argument argument)
    {
        if (occurrence.Fact is not { } fact || !fact.Flags.HasFlag(ValueExpressionSemanticFlags.PassedAsArgument))
        {
            return false;
        }

        var evaluation = WithBlocksOf(index, expression).Aggregate(new StaticEvaluationContext(context.Resolver, occurrence.Scope, Environment: context.Environment),
            (outer, with) => outer with { EnclosingWithTargetType = ExpressionStaticSemanticsEvaluator.Evaluate(outer, with.WithExpression).Result });
        if (ObjectVariableArgumentStaticSemantics.IsPassedAsACopy(evaluation, argument.Callee, argument.Position, argument.Name, expression))
        {
            return false;
        }

        if (ObjectValueStaticSemantics.ParametersOfCallee(evaluation, argument.Callee) is not { } declared)
        {
            return true;
        }

        // a member of a class has an implicit Me at parameter 0, which is the object the call is on and no argument of it.
        var parameters = declared.Where(parameter => !(parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef)).ToImmutableArray();
        return ObjectVariableArgumentStaticSemantics.ParameterOf(parameters, argument.Position, argument.Name) is not { } parameter
            || parameter.ParameterKind is not ParameterKind.ExplicitByVal;
    }

    // the With blocks a node is in, from the outermost: the object of the innermost is what a member written with no owner is a member of (MS-VBAL §5.6.15).
    private static IEnumerable<WithStatementNode> WithBlocksOf(ModuleIndex index, SyntaxNode node)
    {
        var blocks = new Stack<WithStatementNode>();
        for (var parent = index.ParentOf(node); parent is not null; parent = index.ParentOf(parent))
        {
            if (parent is WithStatementNode with)
            {
                blocks.Push(with);
            }
        }

        return blocks;
    }

    // a whole argument of a call: the call, what it calls, the argument's position in what is written and the parameter it names, and whether it is ByVal.
    private sealed record class Argument(SyntaxNode Call, ExpressionNode Callee, int Position, string? Name, bool IsByVal);

    // what is found once for each module the query reads: how its names are used, and the node that holds each node of its tree.
    private sealed class ModuleIndex(RenameModule? module)
    {
        private IReadOnlyDictionary<SourcePosition, NameAccess>? _access;
        private Dictionary<SyntaxNode, SyntaxNode>? _parents;

        public ReferenceUse AccessOf(SourcePosition start)
        {
            _access ??= module is null ? new Dictionary<SourcePosition, NameAccess>() : NameOccurrences.AssignedIn(module);
            return _access.TryGetValue(start, out var access)
                ? access switch
                {
                    NameAccess.Let => ReferenceUse.Let,
                    NameAccess.Set => ReferenceUse.Set,
                    NameAccess.Result => ReferenceUse.Result,
                    _ => ReferenceUse.Read,
                }
                : ReferenceUse.Read;
        }

        public SyntaxNode? ParentOf(SyntaxNode node)
        {
            if (_parents is null)
            {
                _parents = new Dictionary<SyntaxNode, SyntaxNode>(ReferenceEqualityComparer.Instance);
                if (module is not null)
                {
                    foreach (var (child, parent) in NameOccurrences.Walk(module.Syntax))
                    {
                        if (parent is not null)
                        {
                            _parents[child] = parent;
                        }
                    }
                }
            }

            return _parents.GetValueOrDefault(node);
        }
    }
}
