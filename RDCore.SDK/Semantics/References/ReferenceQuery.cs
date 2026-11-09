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
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.References;

/// <summary>
/// Finds where the modules of a workspace name a symbol, and how each names it (<see cref="SymbolReference"/>): what a refactoring that rewrites the references
/// of a symbol (its calls, its assignments, its arguments) works from.
/// </summary>
/// <remarks>
/// <para>
/// A name is bound the way a rename binds it (<see cref="SymbolRename"/>), by the static semantics, so that what is found is what a rename of the symbol renames:
/// a name that refers to another symbol of the same name is no reference of it. A property is one symbol with each of its procedures, its <c>Property Get</c>,
/// <c>Let</c> and <c>Set</c> (<see cref="SymbolReferences.Symbols"/>); each reference says which procedure it calls (<see cref="SymbolReference.Symbol"/>,
/// <see cref="SymbolReference.Use"/>).
/// </para>
/// <para>
/// A name whose meaning is not known where it is written (a member accessed late-bound, a name in an expression nested too deeply to be analyzed, a name in a
/// string a library calls a procedure by) is no reference, and is in <see cref="SymbolReferences.Unknown"/> where the symbol could be what it means.
/// </para>
/// <para>
/// The static semantics are evaluated for each module that writes the symbol's name, once for the query, on a large-stack thread.
/// </para>
/// </remarks>
public static class ReferenceQuery
{
    /// <summary>
    /// The references of the symbol named at <paramref name="position"/> of <paramref name="module"/>: a value, or the type a name that means no value names.
    /// </summary>
    /// <param name="context">The workspace.</param>
    /// <param name="module">The module.</param>
    /// <param name="position">A position in the name, or at its end.</param>
    /// <returns>
    /// The references of the symbol; <see langword="null"/> where no name is written, or where what the name refers to is not known.
    /// </returns>
    public static SymbolReferences? At(RenameContext context, Uri module, SourcePosition position)
        => SymbolNavigation.SymbolAt(context, module, position) is { } named ? Of(context, named.Symbol) : null;

    /// <summary>
    /// The references of <paramref name="symbol"/>, in every module of the workspace.
    /// </summary>
    /// <param name="context">The workspace.</param>
    /// <param name="symbol">A symbol the workspace declares, or a symbol of a library it references.</param>
    /// <returns>The references of the symbol, and the places whose meaning is not known that could be.</returns>
    public static SymbolReferences Of(RenameContext context, Symbol symbol)
    {
        if (!LargeStackThread.IsCurrent)
        {
            return LargeStackThread.Run(() => Of(context, symbol));
        }

        var together = SymbolNavigation.DefinedTogether(context, symbol).ToList();
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
            if (SymbolRename.IsLateBound(occurrence))
            {
                lateBound.Add(occurrence);
                continue;
            }

            // a name that means no value where it is written is the type it names: a class that is created with New.
            var refers = occurrence.Binding is { } binding
                ? symbols.Contains(binding)
                : isType && occurrence.Scope is { } scope && SymbolNavigation.IsTheType(context, symbol, scope.Uri);
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
                        && SymbolNavigation.IsTheType(context, symbol, NameOccurrences.ScopeOf(context, source, range.Start).Uri))
                    {
                        references.Add(new SymbolReference(source.Module, range, ReferenceForm.TypeName, ReferenceUse.None) { Symbol = symbol.SemanticId });
                    }
                }
            }
        }

        // a name written as the symbol's whose meaning is not known, where the symbol could be what it means, as a rename refuses it.
        var unknown = found.Unknown
            .Where(occurrence => occurrence.Kind == OccurrenceKind.String
                ? SymbolRename.IsCalledBy(context, together, occurrence)
                : occurrence.Kind == OccurrenceKind.Member || together.Any(defined => SymbolRename.ChainIndex(occurrence.Scope, defined) < int.MaxValue))
            .Concat(lateBound.Where(occurrence => SymbolRename.MayBeCalledLateBound(context, together, occurrence)))
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
                MayBePassedByRef = !argument.IsByVal && MayBePassedByRef(context, occurrence, argument),
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

    // whether the call may write to what the argument names: the static pass flags it passed, and no copy of an object's variable, and the parameter it is
    // given to, when the procedure called is known, is not ByVal.
    private static bool MayBePassedByRef(RenameContext context, NameOccurrence occurrence, Argument argument)
    {
        if (occurrence.Fact is not { } fact || !fact.Flags.HasFlag(ValueExpressionSemanticFlags.PassedAsArgument)
            || (fact.Flags & (ValueExpressionSemanticFlags.ObjectVariablePassedAsACopy | ValueExpressionSemanticFlags.ObjectVariablePassedLateAsACopy)) != 0)
        {
            return false;
        }

        var evaluation = new StaticEvaluationContext(context.Resolver, occurrence.Scope, Environment: context.Environment);
        if (ObjectValueStaticSemantics.ParametersOfCallee(evaluation, argument.Callee) is not { } declared)
        {
            return true;
        }

        // a member of a class has an implicit Me at parameter 0, which is the object the call is on and no argument of it.
        var parameters = declared.Where(parameter => !(parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef)).ToImmutableArray();
        return ObjectVariableArgumentStaticSemantics.ParameterOf(parameters, argument.Position, argument.Name) is not { } parameter
            || parameter.ParameterKind is not ParameterKind.ExplicitByVal;
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
