using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents any module-level variable member declaration.
/// </summary>
/// <remarks>
/// Module fields are considered to be <em>returning members</em> scoped at the module or global level.
/// </remarks>
public record class VBModuleFieldVariableMemberSymbol : VBReturningMemberSymbol
{
    /// <summary>
    /// Creates a new <c>VBModuleVariableMemberSymbol</c> declaration that is not linked to a document location.
    /// </summary>
    /// <remarks>
    /// This constructor is normally used for symbols defined in a referenced project or library.
    /// Module variables created with this constructor are necessarily public and globally scoped.
    /// </remarks>
    /// <param name="workspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    public VBModuleFieldVariableMemberSymbol(Uri workspaceRoot, string name, Uri parentUri) 
        : base(ScopeKind.Global, workspaceRoot, name, AccessModifier.Public, SymbolKindExt.Field, parentUri)
    {
    }

    /// <summary>
    /// Creates a new <c>VBModuleVariableMemberSymbol</c> declaration that is linked to a document location.
    /// </summary>
    /// <remarks>
    /// This constructor is normally used for symbols defined in the user project's workspace.
    /// </remarks>
    /// <param name="workspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="accessibility">The access modifier associated with this symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
    /// <param name="selectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
    public VBModuleFieldVariableMemberSymbol(Uri workspaceRoot, string name, AccessModifier accessibility, Uri parentUri, Range range, Range selectionRange) 
        : base(ScopeKind.Module, workspaceRoot, name, accessibility, SymbolKindExt.Field, parentUri, range, selectionRange)
    {
    }
}
