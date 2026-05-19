using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Describes a UDT as a member of its parent module.
/// </summary>
public sealed record class VBUserDefinedTypeMemberSymbol : VBTypeMemberSymbol
{
    /// <summary>
    /// Creates a new <c>VBUserDefinedTypeMemberSymbol</c> UDT declaration.
    /// </summary>
    /// <param name="scope">The allocation scope of the symbol.</param>
    /// <param name="workspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="accessibility">The access modifier associated with this symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
    /// <param name="selectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
    public VBUserDefinedTypeMemberSymbol(ScopeKind scope, Uri workspaceRoot, string name, Accessibility accessibility, Range range, Range selectionRange, Uri parentUri)
        : base(scope, workspaceRoot, name, SymbolKindExt.Field, accessibility, parentUri, range, selectionRange) { }
}

