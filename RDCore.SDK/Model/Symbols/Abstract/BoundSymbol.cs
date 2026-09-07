using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// A <c>Symbol</c> that is bound to a workspace document <c>Location</c>.
/// </summary>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The name of the symbol.</param>
/// <param name="Scope">The allocation scope of this symbol.</param>
/// <param name="Kind">A <c>SymbolKind</c> (extensible) metadata value describing the kind of symbol.</param>
/// <param name="Range">The entire document <c>Range</c> belonging to this symbol.</param>
/// <param name="SelectionRange">The specific document <c>Range</c> to highlight when this symbol is selected, usually the symbol's <em>identifier</em> name if applicable.</param>
public abstract record class BoundSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind,
    SourceRange Range, SourceRange SelectionRange) : Symbol(WorkspaceRoot, ParentUri, Name, Scope, Kind)
{
    /// <summary>
    /// Every source site where this symbol is declared, in source order — populated only when the
    /// same name is declared in more than one conditional-compilation branch. Empty for the common
    /// single-declaration case, in which <see cref="BoundSymbol.Range"/> and
    /// <see cref="BoundSymbol.SelectionRange"/> are the sole site.
    /// </summary>
    public ImmutableArray<SymbolDefinition> Definitions { get; init; } = [];

    /// <summary>
    /// Every declaration site of this symbol: <see cref="Definitions"/> when it is populated,
    /// otherwise a single synthesized <see cref="DefinitionState.Live"/> site over
    /// <see cref="BoundSymbol.Range"/>. Use this when a caller needs to treat single- and
    /// multi-branch symbols uniformly.
    /// </summary>
    public IEnumerable<SymbolDefinition> AllDefinitions => Definitions.IsDefaultOrEmpty
        ? [new SymbolDefinition(Range, SelectionRange, DefinitionState.Live)]
        : Definitions;

    /// <summary>
    /// The primary declaration span — the first <see cref="DefinitionState.Live"/> site, else the
    /// first site, else <see cref="BoundSymbol.Range"/>. LSP navigation targets the primary; when no
    /// precompiler-evaluation pass has run yet this is simply the first branch.
    /// </summary>
    public SourceRange PrimaryRange => PrimaryDefinition?.Range ?? Range;

    /// <summary>
    /// The primary selection span, chosen the same way as <see cref="PrimaryRange"/>.
    /// </summary>
    public SourceRange PrimarySelectionRange => PrimaryDefinition?.SelectionRange ?? SelectionRange;

    private SymbolDefinition? PrimaryDefinition => Definitions.IsDefaultOrEmpty
        ? null
        : Definitions.FirstOrDefault(definition => definition.State == DefinitionState.Live) ?? Definitions[0];
}

/// <summary>
/// A <c>Symbol</c> that is <strong>not bound</strong> to a workspace document <c>Location</c>.
/// </summary>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The name of the symbol.</param>
/// <param name="Scope">The allocation scope of this symbol.</param>
/// <param name="Kind">A <c>SymbolKind</c> (extensible) metadata value describing the kind of symbol.</param>
public abstract record class UnboundSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind) 
    : Symbol(WorkspaceRoot, ParentUri, Name, Scope, Kind)
{ }
