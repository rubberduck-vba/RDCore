using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Platform;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Services.Refactoring;

public sealed record class HierarchyItem(Symbol Symbol, Uri Module, SourceRange Range, SourceRange SelectionRange);

public sealed record class HierarchyCall(HierarchyItem Item, IReadOnlyList<SourceRange> Ranges);

// MS-VBAL §5.2.4.2
public static class SymbolHierarchy
{
    public static HierarchyItem? PrepareCalls(RenameContext context, Uri module, SourcePosition position)
        => RenameBinding.SymbolAt(context, module, position) is { Symbol: var symbol } && IsCallable(context, symbol) ? ItemOf(context, symbol) : null;

    public static HierarchyItem? PrepareTypes(RenameContext context, Uri module, SourcePosition position)
        => RenameBinding.SymbolAt(context, module, position) is { Symbol: VBClassModuleSymbol type } && NameOccurrences.IsDeclaredBy(context, type)
            ? ItemOf(context, type)
            : null;

    public static HierarchyItem? ItemOf(RenameContext context, SemanticId id)
        => NameOccurrences.SymbolOf(context, id) is { } symbol && NameOccurrences.IsDeclaredBy(context, symbol) ? ItemOf(context, symbol) : null;

    public static IReadOnlyList<HierarchyCall> IncomingCalls(RenameContext context, HierarchyItem item)
    {
        var calls = new List<(HierarchyItem From, SourceRange Range)>();
        foreach (var occurrence in NameOccurrences.Find(context, item.Symbol.Name, context.Modules).Occurrences)
        {
            if (occurrence.Kind is OccurrenceKind.Declaration or OccurrenceKind.Attribute || occurrence.Binding != item.Symbol.SemanticId)
            {
                continue;
            }

            // a function's own name in its body is how it is given its result, as often as it is a call: the procedure is not its own caller.
            if (CallerAt(context, occurrence.Module, occurrence.Scope) is { } caller && caller.Symbol.SemanticId != item.Symbol.SemanticId)
            {
                calls.Add((caller, occurrence.Range));
            }
        }

        return Grouped(calls);
    }

    public static IReadOnlyList<HierarchyCall> OutgoingCalls(RenameContext context, HierarchyItem item)
    {
        // the static pass recurses once per level of an expression: one large-stack thread for the whole call, not one per name.
        if (!LargeStackThread.IsCurrent)
        {
            return LargeStackThread.Run(() => OutgoingCalls(context, item));
        }

        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == item.Module.AbsoluteUri) is not { } source)
        {
            return [];
        }

        var calls = new List<(HierarchyItem To, SourceRange Range)>();
        foreach (var name in NameOccurrences.NamesIn(source, item.Range))
        {
            foreach (var occurrence in NameOccurrences.Find(context, name, [source]).Occurrences)
            {
                if (occurrence.Kind is not (OccurrenceKind.Name or OccurrenceKind.Member) || !Contains(item.Range, occurrence.Range)
                    || occurrence.Binding is not { } binding || binding == item.Symbol.SemanticId
                    || NameOccurrences.SymbolOf(context, binding) is not { } callee || !IsCallable(context, callee))
                {
                    continue;
                }

                calls.Add((ItemOf(context, callee), occurrence.Range));
            }
        }

        return Grouped(calls);
    }

    public static IReadOnlyList<HierarchyItem> Supertypes(RenameContext context, HierarchyItem item)
        => item.Symbol is VBClassModuleSymbol type
            ? [.. type.ImplementedInterfaces.Where(implemented => NameOccurrences.IsDeclaredBy(context, implemented)).Select(implemented => ItemOf(context, implemented))]
            : [];

    public static IReadOnlyList<HierarchyItem> Subtypes(RenameContext context, HierarchyItem item)
        => item.Symbol is VBClassModuleSymbol type
            ? [.. SymbolNavigation.ImplementersOf(context, type).Select(implementer => ItemOf(context, implementer)).OrderBy(found => found.Symbol.Name, StringComparer.OrdinalIgnoreCase)]
            : [];

    // a procedure, a function or a procedure of a property, that the workspace declares.
    private static bool IsCallable(RenameContext context, Symbol symbol) => SignatureDescription.Of(symbol) is not null && NameOccurrences.IsDeclaredBy(context, symbol);

    // the procedure a name is written in, or its module for a name written outside of every procedure.
    private static HierarchyItem? CallerAt(RenameContext context, Uri module, LexicalScope? scope)
    {
        var procedure = scope?.SelfAndAncestors().FirstOrDefault(candidate => candidate.Kind == LexicalScopeKind.Procedure);
        return procedure is not null && NameOccurrences.SymbolOf(context, new SemanticId(procedure.Uri)) is { } caller
            ? ItemOf(context, caller)
            : NameOccurrences.SymbolOf(context, new SemanticId(module)) is { } declaring ? ItemOf(context, declaring) : null;
    }

    // where an item is declared: the first of its definitions, the start of a module's document.
    private static HierarchyItem ItemOf(RenameContext context, Symbol symbol)
    {
        var module = NameOccurrences.ModuleOf(symbol.Uri);
        if (symbol is not WorkspaceSymbol declared)
        {
            return new HierarchyItem(symbol, module, default, default);
        }

        var first = declared.AllDefinitions.OrderBy(definition => definition.Range.Start).First();
        return new HierarchyItem(symbol, module, first.Range, first.SelectionRange);
    }

    private static IReadOnlyList<HierarchyCall> Grouped(IEnumerable<(HierarchyItem Item, SourceRange Range)> calls)
        => [.. calls
            .GroupBy(call => call.Item.Symbol.SemanticId)
            .Select(group => new HierarchyCall(group.First().Item, [.. group.Select(call => call.Range).Distinct().OrderBy(range => range.Start)]))
            .OrderBy(call => call.Item.Module.AbsoluteUri, StringComparer.Ordinal)
            .ThenBy(call => call.Item.Range.Start)];

    private static bool Contains(SourceRange outer, SourceRange inner) => outer.Start <= inner.Start && inner.End <= outer.End;
}
