using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// A module-level <c>Const</c> declaration. A constant statically evaluates to a value of a fixed
/// <see cref="VBType"/> and cannot be assigned at run-time; an <c>Enum</c> member is modelled
/// separately by <see cref="VBEnumConstMemberSymbol"/>.
/// </summary>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="ResolvedType">The resolved <see cref="VBType"/> of the constant. Use <see cref="VBUnknownType"/> if the type isn't resolved yet.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
/// <param name="AccessModifier">The access modifier specified for this symbol. Use <see cref="AccessModifier.Implicit"/> if none is specified.</param>
public sealed record class VBConstantMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, VBType ResolvedType, SourceRange Range, SourceRange SelectionRange, AccessModifier AccessModifier)
    : VBReturningMemberSymbol(WorkspaceRoot, ParentUri, Name, Scope, SymbolKindExt.Constant, ResolvedType, Range, SelectionRange, AccessModifier) { }

/// <summary>
/// A library-provided <c>Const</c> declaration that is not bound to a workspace document location.
/// </summary>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="ResolvedType">The resolved <see cref="VBType"/> of the constant. Use <see cref="VBUnknownType"/> if the type isn't resolved yet.</param>
public sealed record class UnboundVBConstantMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, VBType ResolvedType)
    : UnboundVBReturningMemberSymbol(WorkspaceRoot, ParentUri, Name, Scope, SymbolKindExt.Constant, ResolvedType) { }
