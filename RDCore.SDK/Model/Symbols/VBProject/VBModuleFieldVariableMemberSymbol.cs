using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents a module-level variable (field) member declaration.
/// </summary>
/// <remarks>
/// Module fields are considered to be <em>returning members</em> scoped at the module or global level.
/// </remarks>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="ResolvedType">The resolved <c>VBType</c> of this member. Use <c>VBUnknownType</c> if the type isn't resolved yet.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
/// <param name="AccessModifier">The access modifier specified for this symbol. Use <c>AccessModifier.Implicit</c> if none is specified.</param>
public record class VBModuleFieldVariableMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, VBType ResolvedType, Range Range, Range SelectionRange, AccessModifier AccessModifier) 
    : VBReturningMemberSymbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Instance, SymbolKindExt.Field, ResolvedType, Range, SelectionRange, AccessModifier) { }

/// <summary>
/// Represents an unbound module-level variable (field) member declaration.
/// </summary>
/// <remarks>
/// Module fields are considered to be <em>returning members</em> scoped at the module or global level.
/// </remarks>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="ResolvedType">The resolved <c>VBType</c> of this member. Use <c>VBUnknownType</c> if the type isn't resolved yet.</param>
public record class UnboundVBModuleFieldVariableMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, VBType ResolvedType)
    : UnboundVBReturningMemberSymbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Instance, SymbolKindExt.Field, ResolvedType) { }
