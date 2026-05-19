using RDCore.SDK.Model.Symbols.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// Represents a <c>Static</c> local variable symbol.
/// </summary>
/// <remarks>
/// A local variable is <c>Static</c> if its parent scope declaration includes the <c>Static</c> modifier, or if the <c>Static</c> keyword is used to declare that variable.
/// </remarks>
public sealed record StaticLocalVariableSymbol : LocalVariableSymbol
{
    /// <summary>
    /// Creates a new <c>StaticLocalVariableSymbol</c> local variable symbol.
    /// </summary>
    /// <remarks>
    /// <c>Static</c> locals are allocated at module level in the <em>workspace statics</em> heap.
    /// </remarks>
    /// <param name="scope">The allocation scope of the symbol.</param>
    /// <param name="workspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
    /// <param name="selectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
    public StaticLocalVariableSymbol(Uri workspaceRoot, string name, Uri parentUri, Range range, Range selectionRange) 
        : base(ScopeKind.Module, workspaceRoot, name, parentUri, range, selectionRange) { }
}
