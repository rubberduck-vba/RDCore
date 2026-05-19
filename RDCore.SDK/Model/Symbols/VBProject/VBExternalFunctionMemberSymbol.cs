using RDCore.SDK.Model.Symbols.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents an external <c>Declare Function</c> member declaration.
/// </summary>
public sealed record class VBExternalFunctionMemberSymbol : VBFunctionMemberSymbol
{
    /// <summary>
    /// Creates a new <c>VBLibraryFunctionMemberSymbol</c> declaration.
    /// </summary>
    /// <param name="scope">The allocation scope of the symbol.</param>
    /// <param name="workspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
    /// <param name="name">The identifier name of the symbol.</param>
    /// <param name="accessibility">The access modifier associated with this symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
    /// <param name="selectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
    /// <param name="isPtrSafe"><c>true</c> if the <c>PtrSafe</c> token is present in the declaration statement.</param>
    /// <param name="lib"></param>
    /// <param name="alias">The <c>Alias</c> string of the declaration statement, if present.</param>
    public VBExternalFunctionMemberSymbol(ScopeKind scope, Uri workspaceRoot, string name, Accessibility accessibility, Uri parentUri, Range range, Range selectionRange, bool isPtrSafe, string lib, string? alias) 
        : base(scope, workspaceRoot, name, accessibility, parentUri, range, selectionRange) 
    {
        IsPtrSafe = isPtrSafe;
        ExternalLibrary = lib;
        ExternalAlias = alias;
    }

    /// <summary>
    /// <c>true</c> if the declaration statement includes a <c>PtrSafe</c> modifier.
    /// </summary>
    public bool IsPtrSafe { get; init; }
    /// <summary>
    /// The external library that defines the function being imported.
    /// </summary>
    public string ExternalLibrary { get; init; }
    /// <summary>
    /// An optional alias that identifies the function being imported.
    /// </summary>
    public string? ExternalAlias { get; init; }
}