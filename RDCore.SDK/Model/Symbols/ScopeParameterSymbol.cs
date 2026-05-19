using RDCore.SDK.Model.Symbols.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// Represents a scope parameter symbol.
/// </summary>
/// <remarks>
/// Parameters are semantically treated as local variables and appear as children of the parent member symbol.
/// </remarks>
public sealed record ScopeParameterSymbol : LocalVariableSymbol
{
    /// <summary>
    /// Creates a new <c>ScopeParameterSymbol</c> local variable symbol.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
    /// <param name="selectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
    public ScopeParameterSymbol(Uri workspaceRoot, string name, Uri parentUri, Range range, Range selectionRange) 
        : base(ScopeKind.Local, workspaceRoot, name, parentUri, range, selectionRange) { }
}