using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// A <c>TypedSymbol</c> representing a procedure-local <c>Const</c> declaration (MS-VBAL &#167;5.4.3.2).
/// A local constant statically evaluates to a value of a fixed <see cref="VBType"/>, is visible only
/// within its declaring procedure, and cannot be assigned at run-time; a module-level <c>Const</c> is
/// modelled by <see cref="VBProject.VBConstantMemberSymbol"/> and an <c>Enum</c> member by
/// <see cref="VBProject.VBEnumConstMemberSymbol"/>.
/// </summary>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the declaring procedure symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
/// <param name="ResolvedType">The resolved <see cref="VBType"/> of the constant, if available. <see cref="VBUnknownType"/> unless specified otherwise.</param>
/// <param name="Value">
/// The declaration's own constant expression, or <c>null</c> when there is none. A constant has no
/// storage to read a value from — it is substituted at each of its use sites (MS-VBAL &#167;5.4.3.2:
/// "a constant statically evaluates to a value") — so this expression <em>is</em> the symbol's value.
/// </param>
public record class VBLocalConstantSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, SourceRange Range, SourceRange SelectionRange, VBType? ResolvedType = default, ExpressionNode? Value = null)
    : BoundTypedSymbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Local, SymbolKindExt.Constant, Range, SelectionRange, ResolvedType ?? VBUnknownType.TypeInfo);
