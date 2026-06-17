using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// A <c>VBUserDefinedType</c> declaration.
/// </summary>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
/// <param name="AccessModifier">The access modifier specified for this symbol.</param>
public sealed record class VBUserDefinedTypeMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, Range Range, Range SelectionRange, AccessModifier AccessModifier) 
    : VBTypeMemberSymbol(WorkspaceRoot, ParentUri, Name, Scope, SymbolKindExt.UserDefinedType, VBUnknownType.TypeInfo, Range, SelectionRange, AccessModifier) { }

/// <summary>
/// An unbound <c>VBUserDefinedType</c> declaration.
/// </summary>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Scope">The allocation scope of the symbol.</param>
public sealed record class UnboundVBUserDefinedTypeMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope)
    : UnboundVBTypeMemberSymbol(WorkspaceRoot, ParentUri, Name, Scope, SymbolKindExt.UserDefinedType, VBUnknownType.TypeInfo) { }
