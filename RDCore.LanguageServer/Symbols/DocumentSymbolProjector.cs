using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// Projects a module's declared <see cref="Symbol"/>s onto a hierarchical LSP <see cref="DocumentSymbol"/>
/// tree: one top-level symbol for the module itself, its declared members as direct children, and — the
/// same nesting <see cref="SymbolDescriptorProjector"/> uses — <c>Enum</c> constants and user-defined-
/// <c>Type</c> fields nested under their owner.
/// </summary>
/// <remarks>
/// Kept apart from the handler and free of parse/workspace plumbing so the projection can be tested
/// against synthetic symbols directly, the same way <see cref="SymbolDescriptorProjector"/> is.
/// </remarks>
internal static class DocumentSymbolProjector
{
    /// <summary>
    /// Projects the module itself — as the single top-level <see cref="DocumentSymbol"/> — with its
    /// declared members nested as children.
    /// </summary>
    /// <param name="moduleUri">The <c>Uri</c> the module's own members are parented to.</param>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="moduleKind">The module's kind — <see cref="SymbolKindExt.Module"/> or <see cref="SymbolKindExt.Class"/>.</param>
    /// <param name="moduleRange">The module's whole declaration range, e.g. spanning its full source text.</param>
    /// <param name="moduleSelectionRange">The range to select when navigating to the module symbol itself.</param>
    /// <param name="symbols">Every symbol the module declares, as yielded by <see cref="SyntaxTreeSymbolProvider"/>.</param>
    public static DocumentSymbol Project(
        Uri moduleUri, string moduleName, SymbolKindExt moduleKind,
        SourceRange moduleRange, SourceRange moduleSelectionRange, IEnumerable<Symbol> symbols)
    {
        // Uri.Equals ignores the fragment, but symbol parentage lives entirely in the fragment
        // (workspace#Module.Member), so compare by the full string instead — same rationale as
        // SymbolDescriptorProjector.
        var moduleKey = moduleUri.ToString();
        var all = symbols as IReadOnlyCollection<Symbol> ?? [.. symbols];
        var childrenByParent = all.Where(s => s.ParentUri.ToString() != moduleKey).ToLookup(s => s.ParentUri.ToString());

        var members = new List<DocumentSymbol>();
        foreach (var symbol in all.Where(s => s.ParentUri.ToString() == moduleKey))
        {
            if (Describe(symbol, childrenByParent) is { } documentSymbol)
            {
                members.Add(documentSymbol);
            }
        }

        return new DocumentSymbol
        {
            Name = moduleName,
            Kind = ToDocumentSymbolKind(moduleKind) ?? SymbolKind.Module,
            Range = moduleRange.ToLsp(),
            SelectionRange = moduleSelectionRange.ToLsp(),
            Children = members.Count == 0 ? null : new Container<DocumentSymbol>(members),
        };
    }

    private static DocumentSymbol? Describe(Symbol symbol, ILookup<string, Symbol> childrenByParent)
    {
        // only a BoundSymbol carries the Range/SelectionRange a DocumentSymbol needs; an unbound
        // symbol reaching here (a stray/unexpected parentage) has nothing to project.
        if (symbol is not BoundSymbol bound || ToDocumentSymbolKind(bound.Kind) is not { } kind)
        {
            return null;
        }

        // only Enum constants and UDT fields nest under their owner here — the same restriction
        // SymbolDescriptorProjector applies, and for the same reason: a procedure's locals share the
        // childrenByParent lookup by virtue of their ParentUri, but were never meant to appear as a
        // member of anything in an outline.
        var children = childrenByParent[bound.Uri.ToString()]
            .Where(IsNestableMember)
            .Select(child => Describe(child, childrenByParent))
            .OfType<DocumentSymbol>()
            .ToArray();

        return new DocumentSymbol
        {
            Name = bound.Name,
            Kind = kind,
            // the primary declaration site: one outline entry per logical symbol, even when the same
            // name is declared in more than one conditional-compilation branch.
            Range = bound.PrimaryRange.ToLsp(),
            SelectionRange = bound.PrimarySelectionRange.ToLsp(),
            Children = children.Length == 0 ? null : new Container<DocumentSymbol>(children),
        };
    }

    private static bool IsNestableMember(Symbol symbol) => symbol is VBEnumConstMemberSymbol or VBUserDefinedTypeFieldSymbol;

    /// <summary>
    /// Maps the model's extended <see cref="SymbolKindExt"/> to a valid LSP <see cref="SymbolKind"/>,
    /// or <c>null</c> when the kind has no safe LSP representation — an RDCore-only extension kind
    /// (128+) must never reach the client as a raw, out-of-range <see cref="SymbolKind"/> integer.
    /// </summary>
    private static SymbolKind? ToDocumentSymbolKind(SymbolKindExt kind) => kind switch
    {
        SymbolKindExt.Ignored or SymbolKindExt.Attribute or SymbolKindExt.Directive or SymbolKindExt.LineLabel => null,
        SymbolKindExt.DateLiteral => SymbolKind.String,
        SymbolKindExt.VariantLiteral => SymbolKind.Variable,
        SymbolKindExt.TypeDescriptor => SymbolKind.TypeParameter,
        _ when (int)kind is >= 1 and <= 26 => kind.ToLsp(),
        _ => null,
    };
}
