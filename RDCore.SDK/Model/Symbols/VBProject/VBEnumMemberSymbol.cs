using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents an <c>Enum</c> type declaration symbol.
/// </summary>
public sealed record class VBEnumMemberSymbol : VBTypeMemberSymbol
{
    /// <summary>
    /// Creates a new <c>VBEnumMemberSymbol</c> that is not linked to a document location.
    /// </summary>
    /// <remarks>
    /// This constructor is normally used for symbols defined in a referenced project or library.
    /// </remarks>
    /// <param name="scope">The allocation scope of the symbol.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="accessibility">The access modifier associated with this symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    public VBEnumMemberSymbol(ScopeKind scope, Uri uri, string name, Accessibility accessibility, Uri parentUri) 
        : base(scope, uri, name, SymbolKindExt.Enum, accessibility, parentUri)
    {
    }

    /// <summary>
    /// Creates a new <c>VBEnumMemberSymbol</c> that is linked to a document location.
    /// </summary>
    /// <remarks>
    /// This constructor is normally used for symbols defined in the user project's workspace.
    /// </remarks>
    /// <param name="scope">The allocation scope of the symbol.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="accessibility">The access modifier associated with this symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
    /// <param name="selectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
    public VBEnumMemberSymbol(ScopeKind scope, Uri uri, string name, Accessibility accessibility, Uri parentUri, Range range, Range selectionRange) 
        : base(scope, uri, name, SymbolKindExt.Enum, accessibility, parentUri, range, selectionRange)
    {
    }
}
