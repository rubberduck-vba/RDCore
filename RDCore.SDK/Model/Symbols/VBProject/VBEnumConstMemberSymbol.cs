using RDCore.SDK.Model.Expressions.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents a symbol that has a parent <c>Enum</c> symbol.
/// </summary>
/// <remarks>
/// <c>Enum</c> members are considered <c>Public</c> constant fields declared <c>As Long</c>; their effective visibility is governed by their parent <c>Enum</c>.
/// </remarks>
public sealed record class VBEnumConstMemberSymbol : VBReturningMemberSymbol
{
    /// <summary>
    /// Creates a new <c>VBEnumConstMemberSymbol</c> that is not linked to a document location.
    /// </summary>
    /// <remarks>
    /// This constructor is normally used for symbols defined in a referenced project or library.
    /// </remarks>
    /// <param name="scope">The allocation scope of the symbol.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="workspaceRoot"></param>
    public VBEnumConstMemberSymbol(ScopeKind scope, Uri workspaceRoot, string name, Uri parentUri, BoundExpression valueExpression)
        : base(scope, workspaceRoot, name, Accessibility.Public, SymbolKindExt.EnumMember, parentUri)
    {
        ResolvedType = VBLongType.TypeInfo;
        ValueExpression = valueExpression;
    }

    /// <summary>
    /// Creates a new <c>VBReturningMemberSymbol</c> that is linked to a document location.
    /// </summary>
    /// <remarks>
    /// This constructor is normally used for symbols defined in the user project's workspace.
    /// </remarks>
    /// <param name="scope">The allocation scope of the symbol.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
    /// <param name="selectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
    public VBEnumConstMemberSymbol(ScopeKind scope, Uri workspaceRoot, string name, Uri parentUri, BoundExpression? valueExpression, Range range, Range selectionRange)
        : base(scope, workspaceRoot, name, Accessibility.Public, SymbolKindExt.EnumMember, parentUri, range, selectionRange)
    {
        ResolvedType = VBLongType.TypeInfo;
        ValueExpression = valueExpression;
    }

    /// <summary>
    /// A <c>BoundExpression</c> that may be present at the declaration site to statically set a constant value.
    /// </summary>
    public BoundExpression? ValueExpression { get; init; }
}
