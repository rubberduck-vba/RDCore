using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents a parameter symbol.
/// </summary>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
/// <param name="ParameterKind">Describes how an argument is passed to this parameter.</param>
/// <param name="ResolvedType">The resolved type of the symbol, if available. <c>VBUnknownType</c> otherwise.</param>
/// <param name="IsOptional"><c>true</c> if the parameter has an <c>Optional</c> token.</param>
/// <param name="DefaultValue">
/// The parameter's own <c>&lt;default-value&gt;</c> clause (<strong>MS-VBAL §5.3.1.7</strong>),
/// pre-computed — a parameter's default is necessarily a constant expression, the same as a local
/// <c>Const</c>'s own initializer. <c>null</c> when <see cref="IsOptional"/> is <c>true</c> but no
/// default was specified: an unmapped call-site argument then falls back to
/// <see cref="ResolvedType"/>'s own default value instead (<strong>MS-VBAL §5.3.1.11</strong>).
/// </param>
public record VBParameterSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, SourceRange Range, SourceRange SelectionRange, ParameterKind ParameterKind, VBType ResolvedType, bool IsOptional = false, VBTypedValue? DefaultValue = null)
    : VBLocalVariableSymbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Local, Range, SelectionRange, ResolvedType: ResolvedType)
{ }

public record UnboundVBParameterSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ParameterKind ParameterKind, VBType ResolvedType, bool IsOptional = false)
    : UnboundTypedSymbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Local, SymbolKindExt.Variable, ResolvedType)
{
}

/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
/// <param name="ParameterKind">Describes how an argument is passed to this parameter.</param>
public record ParamArrayParameterSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, SourceRange Range, SourceRange SelectionRange, ParameterKind ParameterKind)
    : VBParameterSymbol(WorkspaceRoot, ParentUri, Name, Range, SelectionRange, ParameterKind, VBFixedSizeArrayType.TypeInfo, IsOptional: false)
{ }
