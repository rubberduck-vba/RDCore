using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// A <c>TypedSymbol</c> representing any local variable.
/// </summary>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
/// <param name="IsStatic"><c>true</c> if the symbol declaration includes an explicit <c>Static</c> token.</param>
/// <param name="ResolvedType">The resolved <c>VBType</c> of the symbol, if available. <c>VBUnknownType</c> unless specified otherwise.</param>
public record class VBLocalVariableSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, Range Range, Range SelectionRange, bool IsStatic = false, VBType? ResolvedType = default) 
    : BoundTypedSymbol(WorkspaceRoot, ParentUri, Name, Scope, SymbolKindExt.Variable, Range, SelectionRange, ResolvedType ?? VBUnknownType.TypeInfo) 
{
    /// <summary>
    /// <c>true</c> if the declaration has an explicit <c>Static</c> token.
    /// </summary>
    /// <remarks>
    /// Use <em>semantic flags</em> instead to determine if a variable is semantically <c>Static</c>.
    /// </remarks>
    public bool IsStatic { get; init; } = IsStatic;
    /// <summary>
    /// Creates and returns a copy of this symbol with the <c>IsStatic</c> flag set to <c>true</c> unless specified otherwise.
    /// </summary>
    public BoundTypedSymbol WithStaticModifier(bool value = true) => this with { IsStatic = value };
}
