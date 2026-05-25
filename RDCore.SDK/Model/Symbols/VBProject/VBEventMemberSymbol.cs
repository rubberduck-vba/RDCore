using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents an <c>Event</c> member declaration symbol.
/// </summary>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace that defines this symbol.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The name of the symbol.</param>
/// <param name="Range">The entire document <c>Range</c> belonging to this symbol.</param>
/// <param name="SelectionRange">The specific document <c>Range</c> to highlight when this symbol is selected, usually the symbol's <em>identifier</em> name if applicable.</param>
/// <param name="AccessModifier">The access modifier specified for this symbol. <c>AccessModifier.Implicit</c> unless specified otherwise.</param>
public sealed record class VBEventMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, Range Range, Range SelectionRange, AccessModifier AccessModifier)
    : VBTypeMemberSymbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Instance, SymbolKindExt.Event, VBVoidType.TypeInfo, Range, SelectionRange, AccessModifier) { }

/// <summary>
/// Represents an unbound <c>Event</c> declaration symbol.
/// </summary>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace that defines this symbol.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The name of the symbol.</param>
public sealed record class UnboundVBEventMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name)
    : UnboundVBTypeMemberSymbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Instance, SymbolKindExt.Event, VBVoidType.TypeInfo) { }
