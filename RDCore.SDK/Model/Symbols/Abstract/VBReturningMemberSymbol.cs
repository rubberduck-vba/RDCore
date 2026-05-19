using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents any <em>returning member</em>.
/// </summary>
/// <remarks>
/// A <em>returning member</em> is any type member symbol that statically evaluates to a representable <c>VBType</c>.
/// </remarks>
public abstract record class VBReturningMemberSymbol : VBTypeMemberSymbol
{
    /// <summary>
    /// Creates a new <c>VBReturningMemberSymbol</c> that is not linked to a document location.
    /// </summary>
    /// <remarks>
    /// This constructor is normally used for symbols defined in a referenced project or library.
    /// </remarks>
    /// <param name="scope">The allocation scope of the symbol.</param>
    /// <param name="workspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="kind">Describes the kind (category) of symbol for the LSP client.</param>
    /// <param name="accessibility">The access modifier associated with this symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    protected VBReturningMemberSymbol(ScopeKind scope, Uri workspaceRoot, string name, Accessibility accessibility, SymbolKindExt kind, Uri parentUri)
        : base(scope, workspaceRoot, name, kind, accessibility, parentUri)
    {
    }


    /// <summary>
    /// Creates a new <c>VBReturningMemberSymbol</c> that is linked to a document location.
    /// </summary>
    /// <remarks>
    /// This constructor is normally used for symbols defined in the user project's workspace.
    /// </remarks>
    /// <param name="scope">The allocation scope of the symbol.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="workspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
    /// <param name="kind">Describes the kind (category) of symbol for the LSP client.</param>
    /// <param name="accessibility">The access modifier associated with this symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
    /// <param name="selectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
    protected VBReturningMemberSymbol(ScopeKind scope, Uri workspaceRoot, string name, Accessibility accessibility, SymbolKindExt kind, Uri parentUri, Range range, Range selectionRange)
        : base(scope, workspaceRoot, name, kind, accessibility, parentUri, range, selectionRange)
    {
    }
}
