using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Platform;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Services.Refactoring;

public sealed record class HierarchyItem(Symbol Symbol, Uri Module, SourceRange Range, SourceRange SelectionRange)
{
    // the document of the part of the module the ranges are in (RD-VBAL §5.1, RenameModule.Document); null where it is not known.
    public Uri? Document { get; init; }
}

public sealed record class HierarchyCall(HierarchyItem Item, IReadOnlyList<SourceRange> Ranges);

// MS-VBAL §5.2.4.2
// the document of a position names the part of a module of parts (RD-VBAL §5.1) it is in; null for the first part at the address.
public static class SymbolHierarchy
{
    public static HierarchyItem? PrepareCalls(RenameContext context, Uri module, SourcePosition position, Uri? document = null)
        => RenameBinding.SymbolAt(context, module, position, document) is { Symbol: var symbol } && IsCallable(context, symbol)
            ? ItemOf(context, symbol)
            : null;

    public static HierarchyItem? PrepareTypes(RenameContext context, Uri module, SourcePosition position, Uri? document = null)
        => RenameBinding.SymbolAt(context, module, position, document) is { Symbol: VBClassModuleSymbol type } && NameOccurrences.IsDeclaredBy(context, type)
            ? ItemOf(context, type)
            : null;

    public static HierarchyItem? ItemOf(RenameContext context, SemanticId id)
        => NameOccurrences.SymbolOf(context, id) is { } symbol && NameOccurrences.IsDeclaredBy(context, symbol) ? ItemOf(context, symbol) : null;

    public static IReadOnlyList<HierarchyCall> IncomingCalls(RenameContext context, HierarchyItem item)
    {
        var calls = new List<(HierarchyItem From, SourceRange Range)>();
        var results = new Dictionary<string, IReadOnlyDictionary<SourcePosition, NameAccess>>(StringComparer.Ordinal);
        foreach (var occurrence in NameOccurrences.Find(context, item.Symbol.Name, context.Modules).Occurrences)
        {
            if (occurrence.Kind is OccurrenceKind.Declaration or OccurrenceKind.Attribute || occurrence.Binding != item.Symbol.SemanticId
                || IsResult(context, results, occurrence))
            {
                continue;
            }

            if (CallerAt(context, occurrence.Module, occurrence.Document, occurrence.Scope) is { } caller)
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

        if (context.Modules.FirstOrDefault(candidate => candidate.Is(item.Module, item.Document)) is not { } source)
        {
            return [];
        }

        var calls = new List<(HierarchyItem To, SourceRange Range)>();
        var results = new Dictionary<string, IReadOnlyDictionary<SourcePosition, NameAccess>>(StringComparer.Ordinal);
        foreach (var name in NameOccurrences.NamesIn(source, item.Range))
        {
            foreach (var occurrence in NameOccurrences.Find(context, name, [source]).Occurrences)
            {
                if (occurrence.Kind is not (OccurrenceKind.Name or OccurrenceKind.Member) || !item.Range.Contains(occurrence.Range)
                    || occurrence.Binding is not { } binding || IsResult(context, results, occurrence)
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

    // a function's or a Property Get's own name in its body, read or assigned without arguments, is its result variable (MS-VBAL §5.3.1, §5.6.2.1), not a call
    // of it: with arguments, as a call statement, it is a call, as it is in a procedure, which has no result variable.
    private static bool IsResult(RenameContext context, Dictionary<string, IReadOnlyDictionary<SourcePosition, NameAccess>> results, NameOccurrence occurrence)
    {
        if (!results.TryGetValue(occurrence.Key, out var accesses))
        {
            accesses = context.Modules.FirstOrDefault(each => each.Is(occurrence.Module, occurrence.Document)) is { } module
                ? NameOccurrences.AssignedIn(module)
                : new Dictionary<SourcePosition, NameAccess>();
            results[occurrence.Key] = accesses;
        }

        return accesses.TryGetValue(occurrence.Range.Start, out var access) && access is NameAccess.Result
            && CallerAt(context, occurrence.Module, occurrence.Document, occurrence.Scope)?.Symbol.SemanticId == occurrence.Binding;
    }

    // a procedure, a function or a procedure of a property, that the workspace declares.
    private static bool IsCallable(RenameContext context, Symbol symbol) => SignatureDescription.Of(symbol) is not null && NameOccurrences.IsDeclaredBy(context, symbol);

    // the procedure a name is written in, or its module for a name written outside of every procedure, in the part the name is written in.
    private static HierarchyItem? CallerAt(RenameContext context, Uri module, Uri? document, LexicalScope? scope)
    {
        var procedure = scope?.SelfAndAncestors().FirstOrDefault(candidate => candidate.Kind == LexicalScopeKind.Procedure);
        return procedure is not null && NameOccurrences.SymbolOf(context, new SemanticId(procedure.Uri)) is { } caller
            ? ItemOf(context, caller)
            : NameOccurrences.SymbolOf(context, new SemanticId(module)) is { } declaring ? ItemOf(context, declaring) with { Document = document } : null;
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
        return new HierarchyItem(symbol, module, first.Range, first.SelectionRange) { Document = declared.DocumentUri };
    }

    private static IReadOnlyList<HierarchyCall> Grouped(IEnumerable<(HierarchyItem Item, SourceRange Range)> calls)
        => [.. calls
            .GroupBy(call => call.Item.Symbol.SemanticId)
            .Select(group => new HierarchyCall(group.First().Item, [.. group.Select(call => call.Range).Distinct().OrderBy(range => range.Start)]))
            .OrderBy(call => RenameModule.KeyOf(call.Item.Module, call.Item.Document ?? call.Item.Module), StringComparer.Ordinal)
            .ThenBy(call => call.Item.Range.Start)];
}
