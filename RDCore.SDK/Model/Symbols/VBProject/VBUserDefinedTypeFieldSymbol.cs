using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents a field (element) of a user-defined <c>Type … End Type</c> declaration
/// (MS-VBAL 5.2.3.3). Unlike a module field it is not independently accessible — it is reached only
/// through an instance of its enclosing type — so it is a distinct symbol whose <c>ParentUri</c> is
/// the <see cref="VBUserDefinedTypeMemberSymbol"/>, not the module.
/// </summary>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the enclosing user-defined-type symbol.</param>
/// <param name="Name">The identifier name of the field.</param>
/// <param name="ResolvedType">The resolved <c>VBType</c> of this field. Use <c>VBUnknownType</c> if the type isn't resolved yet.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
/// <param name="AccessModifier">The access modifier specified for this symbol. Use <c>AccessModifier.Implicit</c> if none is specified.</param>
public sealed record class VBUserDefinedTypeFieldSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, VBType ResolvedType, SourceRange Range, SourceRange SelectionRange, AccessModifier AccessModifier)
    : VBReturningMemberSymbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Instance, SymbolKindExt.Field, ResolvedType, Range, SelectionRange, AccessModifier) { }

/// <summary>
/// Represents an unbound field of a user-defined <c>Type</c> declaration.
/// </summary>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the enclosing user-defined-type symbol.</param>
/// <param name="Name">The identifier name of the field.</param>
/// <param name="ResolvedType">The resolved <c>VBType</c> of this field. Use <c>VBUnknownType</c> if the type isn't resolved yet.</param>
public sealed record class UnboundVBUserDefinedTypeFieldSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, VBType ResolvedType)
    : UnboundVBReturningMemberSymbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Instance, SymbolKindExt.Field, ResolvedType) { }
