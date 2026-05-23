using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// Describes the kind of parameter.
/// </summary>
public enum ParameterKind
{
    /// <summary>
    /// The parameter is implicitly declared as being passed by reference (implicit default).
    /// </summary>
    ImplicitByRef,
    /// <summary>
    /// The parameter is explicitly declared as being passed by reference (<c>ByRef</c>).
    /// </summary>
    /// <remarks>
    /// If the member is a <c>Property Let</c> and <c>Property Set</c> declaration, semantics work <c>ByVal</c> regardless.
    /// </remarks>
    ExplicitByRef,
    /// <summary>
    /// The parameter is explicitly declared as being passed by value (<c>ByVal</c>).
    /// </summary>
    ExplicitByVal,
    /// <summary>
    /// The parameter is implicitly declared as being passed by value (<c>ByVal</c>).
    /// </summary>
    /// <remarks>
    /// This is only applicable for the value paraemter of <c>Property Let</c> and <c>Property Set</c> declarations.
    /// </remarks>
    ImplicitByVal,
}

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
public record ParameterSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, Range Range, Range SelectionRange, ParameterKind ParameterKind, VBType? ResolvedType = default, bool IsOptional = false)
    : LocalVariableSymbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Local, Range, SelectionRange, ResolvedType: ResolvedType) 
{ }

/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
/// <param name="ParameterKind">Describes how an argument is passed to this parameter.</param>
public record ParamArrayParameterSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, Range Range, Range SelectionRange, ParameterKind ParameterKind)
    : ParameterSymbol(WorkspaceRoot, ParentUri, Name, Range, SelectionRange, ParameterKind, VBVariantType.TypeInfo, IsOptional: false)
{ }
