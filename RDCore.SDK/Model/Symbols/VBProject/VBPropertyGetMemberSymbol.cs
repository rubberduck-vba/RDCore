using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents a <c>Property Get</c> procedure (returning member) declaration symbol.
/// </summary>
public sealed record class VBPropertyGetMemberSymbol : VBReturningMemberSymbol, IVBPropertyMemberSymbol
{
    /// <summary>
    /// Creates a new <c>VBPropertyGetMemberSymbol</c> that is not linked to a document location.
    /// </summary>
    /// <remarks>
    /// This constructor is normally used for symbols defined in a referenced project or library.
    /// </remarks>
    /// <param name="scope">The allocation scope of the symbol.</param>
    /// <param name="workspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="accessibility">The access modifier associated with this symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    public VBPropertyGetMemberSymbol(ScopeKind scope, Uri workspaceRoot, string name, AccessModifier accessibility, Uri parentUri)
        : base(scope, workspaceRoot, name, accessibility, SymbolKindExt.Property, parentUri) { }

    /// <summary>
    /// Creates a new <c>VBPropertyGetMemberSymbol</c> that is linked to a document location.
    /// </summary>
    /// <remarks>
    /// This constructor is normally used for symbols defined in the user project's workspace.
    /// </remarks>
    /// <param name="scope">The allocation scope of the symbol.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="workspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
    /// <param name="accessibility">The access modifier associated with this symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
    /// <param name="selectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
    public VBPropertyGetMemberSymbol(ScopeKind scope, Uri workspaceRoot, string name, AccessModifier accessibility, Uri parentUri, Range range, Range selectionRange)
        : base(scope, workspaceRoot, name, accessibility, SymbolKindExt.Property, parentUri, range, selectionRange) { }
}
