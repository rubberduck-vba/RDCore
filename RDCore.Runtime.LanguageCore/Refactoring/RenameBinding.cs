using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.Runtime.LanguageCore.Static;
using System.Collections.Immutable;
using System.Globalization;
using System.Resources;

namespace RDCore.Runtime.LanguageCore.Refactoring;

// how a rename binds a name: the reference index binds names the same way, so it finds what a rename renames.
public static class RenameBinding
{
    public static (SourceRange Origin, Symbol Symbol)? SymbolAt(RenameContext context, Uri module, SourcePosition position)
    {
        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } source)
        {
            return null;
        }

        if (NameOccurrences.NameAt(source, position) is not { } name)
        {
            return NameOccurrences.TypeNameAt(source, position) is { } typeName
                && context.Resolver.ResolveType(typeName.Name, ScopeKind.Local, NameOccurrences.ScopeOf(context, source, position).Uri).Symbol is { } declared
                ? (typeName.Range, declared)
                : null;
        }

        var found = NameOccurrences.Find(context, name, [source]);
        if (found.Unknown.Any(occurrence => Contains(occurrence.Range, position)))
        {
            return null;
        }

        var here = found.Occurrences.FirstOrDefault(occurrence => Contains(occurrence.Range, position));
        if (here.Binding is { } binding && NameOccurrences.SymbolOf(context, binding) is { } symbol)
        {
            return (here.Range, symbol);
        }

        return here.Scope is { } scope && context.Resolver.ResolveType(name, ScopeKind.Local, scope.Uri).Symbol is { } type
            ? (here.Range, type)
            : null;
    }

    // a property is defined by each of its procedures - its Property Get, Let and Set: what an access calls is not known from the name alone.
    internal static IEnumerable<Symbol> DefinedTogether(RenameContext context, Symbol symbol)
    {
        if (symbol is not (VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol)
            || !context.ScopeTree.TryGetScope(NameOccurrences.ModuleOf(symbol.Uri), out var module))
        {
            return [symbol];
        }

        var accessors = module.DeclaredAs(symbol.Name)
            .Where(declared => declared is VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol)
            .ToList();
        return accessors.Count > 0 ? accessors : [symbol];
    }

    // whether the type a name names where it is written is the symbol.
    internal static bool IsTheType(RenameContext context, Symbol type, Uri scope)
        => context.Resolver.ResolveType(type.Name, ScopeKind.Local, scope).Symbol is { } resolved && resolved.SemanticId == type.SemanticId;

    // whether a string a library calls a procedure by may name one of the symbols: only a member of a module is called by its name, and a string that names a
    // module of the workspace names the member of that one.
    internal static bool IsCalledBy(RenameContext context, IReadOnlyList<Symbol> symbols, NameOccurrence named)
        => symbols.Any(symbol => NameOccurrences.SymbolOf(context, new SemanticId(symbol.ParentUri)) is VBModuleSymbol module
            && (named.Qualifier is not { } qualifier
                || string.Equals(qualifier, module.Name, StringComparison.OrdinalIgnoreCase)
                || !context.Modules.Any(candidate => string.Equals(SymbolUri.ModuleNameOf(candidate.Module), qualifier, StringComparison.OrdinalIgnoreCase))));

    // a member accessed late-bound, on an Object or a Variant: what it refers to is found when the code runs.
    internal static bool IsLateBound(NameOccurrence occurrence)
        => occurrence is { Binding: null, Kind: OccurrenceKind.Member, Fact: { } fact } && fact.Flags.HasFlag(ValueExpressionSemanticFlags.LateBound);

    // whether a late-bound access may call one of the symbols: a member of a class that code outside of it can call, on an object that may be of that class.
    internal static bool MayBeCalledLateBound(RenameContext context, IReadOnlyList<Symbol> symbols, NameOccurrence occurrence)
        => LateBoundMatchOf(context, symbols, occurrence) != LateBoundMatch.None;

    // whether a late-bound access may call one of the symbols, and whether what is known of the object says it may hold the symbol's class.
    internal static LateBoundMatch LateBoundMatchOf(RenameContext context, IReadOnlyList<Symbol> symbols, NameOccurrence occurrence)
    {
        if (!IsLateBound(occurrence))
        {
            return LateBoundMatch.None;
        }

        var classes = symbols
            .Where(symbol => symbol is AccessibleTypedSymbol { AccessModifier: not AccessModifier.Private } or VBTypeMemberSymbol { AccessModifier: not AccessModifier.Private })
            .Select(symbol => NameOccurrences.SymbolOf(context, new SemanticId(symbol.ParentUri)))
            .OfType<VBClassModuleSymbol>()
            .Select(owner => owner.SemanticId)
            .ToList();
        if (classes.Count == 0)
        {
            return LateBoundMatch.None;
        }

        var possible = PossibleClassesOf(context, occurrence) ?? PossibleClasses.Unknown;
        return classes.Any(possible.Classes.Contains) ? LateBoundMatch.Backed
            : classes.Any(possible.MayBe) ? LateBoundMatch.Open
            : LateBoundMatch.None;
    }

    // how a late-bound access may call a symbol: not at all, with what is known of the object backing it, or with nothing known that rules it out.
    internal enum LateBoundMatch
    {
        None,
        Open,
        Backed,
    }

    // what is known of the classes of the object a late-bound member is accessed on: the expression before the '.', or the object of the With block it is in.
    private static PossibleClasses? PossibleClassesOf(RenameContext context, NameOccurrence occurrence)
    {
        if (occurrence.Node is not MemberAccessExpressionNode access
            || context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == occurrence.Module.AbsoluteUri) is not { } module)
        {
            return null;
        }

        var receiver = access.Owner ?? module.Syntax.Descendants().OfType<WithStatementNode>()
            .Where(with => with.SourceLocation.Range.Start <= access.Location.Range.Start && access.Location.Range.End <= with.SourceLocation.Range.End)
            .OrderByDescending(with => with.SourceLocation.Range.Start)
            .FirstOrDefault()?.WithExpression;
        if (receiver is null)
        {
            return null;
        }

        var query = new PossibleClassesQuery(new StaticEvaluationContext(context.Resolver, occurrence.Scope, Environment: context.Environment), receiver,
            context.ScopeTree, module.Module, module.Syntax);
        return PossibleClasses.Combine(((IReadOnlyList<IPossibleClassesSource>)[AssignedClasses.Instance, .. context.ClassSources ?? []])
            .Select(source => source.ClassesOf(query))
            .OfType<PossibleClasses>());
    }

    // how near a symbol is where a name is written: the index of the first scope, from there outward, that declares it; none when no scope there does.
    internal static int ChainIndex(LexicalScope scope, Symbol symbol)
    {
        var index = 0;
        foreach (var outer in scope.SelfAndAncestors())
        {
            if (outer.DeclaredAs(symbol.Name).Any(declared => declared.SemanticId == symbol.SemanticId))
            {
                return index;
            }

            index++;
        }

        return int.MaxValue;
    }

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start <= position && position <= range.End;
}
