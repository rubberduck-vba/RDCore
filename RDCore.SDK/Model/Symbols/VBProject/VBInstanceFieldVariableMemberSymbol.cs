using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents any instance-level (class) variable member declaration.
/// </summary>
/// <remarks>
/// Instance fields are considered to be <em>returning members</em>.
/// </remarks>
public record class VBInstanceFieldVariableMemberSymbol : VBReturningMemberSymbol
{
    /// <summary>
    /// Creates a new <c>VBInstanceFieldVariableMemberSymbol</c> declaration that is not linked to a document location.
    /// </summary>
    /// <remarks>
    /// This constructor is normally used for symbols defined in a referenced project or library.
    /// Instance variables created with this constructor are necessarily public and instance scoped.
    /// </remarks>
    /// <param name="workspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    public VBInstanceFieldVariableMemberSymbol(Uri workspaceRoot, string name, Uri parentUri)
        : base(ScopeKind.Instance, workspaceRoot, name, Accessibility.Public, SymbolKindExt.Field, parentUri)
    {
    }

    /// <summary>
    /// Creates a new <c>VBInstanceFieldVariableMemberSymbol</c> declaration that is linked to a document location.
    /// </summary>
    /// <remarks>
    /// This constructor is normally used for symbols defined in the user project's workspace.
    /// Instance variables created with this constructor are necessarily instance scoped.
    /// </remarks>
    /// <param name="workspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="accessibility">The access modifier associated with this symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
    /// <param name="selectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
    public VBInstanceFieldVariableMemberSymbol(Uri workspaceRoot, string name, Accessibility accessibility, Uri parentUri, Range range, Range selectionRange)
        : base(ScopeKind.Instance, workspaceRoot, name, accessibility, SymbolKindExt.Field, parentUri, range, selectionRange)
    {
    }
}
