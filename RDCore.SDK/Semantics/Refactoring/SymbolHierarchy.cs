using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Completion;

namespace RDCore.SDK.Semantics.Refactoring;

/// <summary>
/// A symbol of a hierarchy - a procedure that calls or is called, a class that implements or is implemented - and where it is declared.
/// </summary>
/// <param name="Symbol">The symbol.</param>
/// <param name="Module">The address of the module that declares it.</param>
/// <param name="Range">Its whole declaration; a module's is the start of its document.</param>
/// <param name="SelectionRange">The name it declares.</param>
public sealed record class HierarchyItem(Symbol Symbol, Uri Module, SourceRange Range, SourceRange SelectionRange);

/// <summary>
/// A call of a hierarchy: the procedure on the other side of it, and where the calls are written - in the caller, for an incoming call; in the procedure the
/// hierarchy is of, for an outgoing one.
/// </summary>
/// <param name="Item">The caller of an incoming call, or what an outgoing call calls.</param>
/// <param name="Ranges">Where each call is written: the name that calls.</param>
public sealed record class HierarchyCall(HierarchyItem Item, IReadOnlyList<SourceRange> Ranges);

/// <summary>
/// The call hierarchy and the type hierarchy of the workspace (<strong>LSP 3.17</strong> §Call Hierarchy, §Type Hierarchy): which procedures call which, and
/// which classes implement which (<strong>MS-VBAL §5.2.4.2</strong>). Names are bound the way a rename binds them (<see cref="SymbolNavigation"/>).
/// </summary>
/// <remarks>
/// A procedure, a function and each procedure of a property are items of the call hierarchy; a call is a name that refers to one of them, whatever writes the
/// name - a call statement, an expression, an argument passed <c>AddressOf</c>. What is declared outside of the workspace - a member of a library - is no item:
/// it has no place to be shown at. A class module is an item of the type hierarchy; its supertypes are the classes it implements, and its subtypes the classes
/// that implement it.
/// </remarks>
public static class SymbolHierarchy
{
    /// <summary>
    /// The item of the call hierarchy named at <paramref name="position"/> of <paramref name="module"/>: a procedure of the workspace.
    /// </summary>
    /// <returns>The item; <see langword="null"/> for a name that names no procedure the workspace declares.</returns>
    public static HierarchyItem? PrepareCalls(RenameContext context, Uri module, SourcePosition position)
        => SymbolNavigation.SymbolAt(context, module, position) is { Symbol: var symbol } && IsCallable(context, symbol) ? ItemOf(context, symbol) : null;

    /// <summary>
    /// The item of the type hierarchy named at <paramref name="position"/> of <paramref name="module"/>: a class module of the workspace.
    /// </summary>
    /// <returns>The item; <see langword="null"/> for a name that names no class the workspace declares.</returns>
    public static HierarchyItem? PrepareTypes(RenameContext context, Uri module, SourcePosition position)
        => SymbolNavigation.SymbolAt(context, module, position) is { Symbol: VBClassModuleSymbol type } && NameOccurrences.IsDeclaredBy(context, type)
            ? ItemOf(context, type)
            : null;

    /// <summary>
    /// The symbol an item of a hierarchy is of, by its identity: what a client sends back to ask for the calls or the types of an item.
    /// </summary>
    /// <returns>The item, as the workspace declares it now; <see langword="null"/> when the workspace no longer declares it.</returns>
    public static HierarchyItem? ItemOf(RenameContext context, SemanticId id)
        => NameOccurrences.SymbolOf(context, id) is { } symbol && NameOccurrences.IsDeclaredBy(context, symbol) ? ItemOf(context, symbol) : null;

    /// <summary>
    /// The procedures that call the item, each with where in it the calls are written; a call from no procedure - a constant's expression, an attribute - is
    /// from the module.
    /// </summary>
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

    /// <summary>
    /// What the item calls, each with where in the item the calls are written.
    /// </summary>
    public static IReadOnlyList<HierarchyCall> OutgoingCalls(RenameContext context, HierarchyItem item)
    {
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

    /// <summary>
    /// The classes of the workspace that the item's class implements.
    /// </summary>
    public static IReadOnlyList<HierarchyItem> Supertypes(RenameContext context, HierarchyItem item)
        => item.Symbol is VBClassModuleSymbol type
            ? [.. type.ImplementedInterfaces.Where(implemented => NameOccurrences.IsDeclaredBy(context, implemented)).Select(implemented => ItemOf(context, implemented))]
            : [];

    /// <summary>
    /// The classes of the workspace that implement the item's class.
    /// </summary>
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
