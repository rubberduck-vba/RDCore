using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents a symbol that can be resolved to a <c>VBType</c>.
/// </summary>
public abstract record class TypedSymbol : Symbol
{
    /// <summary>
    /// Creates a new <c>TypedSymbol</c> that is not linked to a document location.
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
    protected TypedSymbol(ScopeKind scope, Uri workspaceRoot, string name, SymbolKindExt kind, Accessibility accessibility, Uri parentUri)
        : base(workspaceRoot, name, kind, parentUri, scope)
    {
        Accessibility = accessibility;
        ResolvedType = VBUnknownType.TypeInfo;
    }
    /// <summary>
    /// Creates a new <c>TypedSymbol</c> that is linked to a document location.
    /// </summary>
    /// <remarks>
    /// This constructor is normally used for symbols defined in the user project's workspace.
    /// </remarks>
    /// <param name="scope">The allocation scope of the symbol.</param>
    /// <param name="workspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="kind">Describes the kind (category) of symbol for the LSP client.</param>
    /// <param name="accessibility">The access modifier associated with this symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
    /// <param name="selectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
    protected TypedSymbol(ScopeKind scope, Uri workspaceRoot, string name, SymbolKindExt kind, Accessibility accessibility, Uri parentUri, Range range, Range selectionRange)
        : base(workspaceRoot, scope, name, kind, range, selectionRange, parentUri)
    {
        Accessibility = accessibility;
        ResolvedType = VBUnknownType.TypeInfo;
    }

    /// <summary>
    /// Gets the access modifier for this symbol.
    /// </summary>
    /// <remarks>
    /// Returns <c>Accessibility.Implicit</c> if no access modifier was specified; semantics define the symbol's accessibility, taking this value into consideration.
    /// </remarks>
    public Accessibility Accessibility { get; init; }

    /// <summary>
    /// The resolved <c>VBType</c> of this symbol.
    /// </summary>
    public VBType ResolvedType { get; init; }

    /// <summary>
    /// Creates and returns a copy of this symbol with the specified <c>Accessibility</c> modifier value.
    /// </summary>
    public TypedSymbol WithAccessibility(Accessibility accessibility) => this with { Accessibility = accessibility };

    /// <summary>
    /// Creates and returns a copy of this symbol with the specified <c>ResolvedType</c>.
    /// </summary>
    public TypedSymbol WithResolvedType(VBType type) => this with { ResolvedType = type };
}
