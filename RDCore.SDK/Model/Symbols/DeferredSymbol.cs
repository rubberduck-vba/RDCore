using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// A <c>BoundSymbol</c> that is inferred from an <em>unbound expression</em> and accessible to <em>IntelliSense</em> and analyzers, but not yet materialized in source code.
/// </summary>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The name of the symbol.</param>
/// <param name="Scope">The allocation scope of this symbol.</param>
/// <param name="Kind">A <c>SymbolKind</c> (extensible) metadata value describing the kind of symbol.</param>
/// <param name="Range">The entire document <c>Range</c> belonging to this symbol.</param>
/// <param name="SelectionRange">The specific document <c>Range</c> to highlight when this symbol is selected, usually the symbol's <em>identifier</em> name if applicable.</param>
public record class DeferredSymbol(Uri WorkspaceRoot, Uri ParentUri, ScopeKind Scope, string Name, SymbolKindExt Kind, Range Range, Range SelectionRange) 
    : BoundSymbol(WorkspaceRoot, ParentUri, Name, Scope, Kind, Range, SelectionRange)
{
}
