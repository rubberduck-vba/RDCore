using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Server.ProtocolExtensions;
using System.Collections.Immutable;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents any workspace object that has a name, and maybe a location in a document.
/// </summary>
/// <remarks>
/// All symbols minimally define a <c>Uri</c>, a <c>Name</c>, and a <c>SymbolKind</c>.
/// </remarks>
public abstract record class Symbol
{
    /// <summary>
    /// For symbols representing a user workspace project or a referenced library.
    /// </summary>
    /// <param name="workspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
    /// <param name="name">The name of the symbol.</param>
    protected Symbol(Uri workspaceRoot, string name)
        : this(workspaceRoot, name, SymbolKindExt.Project)
    {
    }

    /// <summary>
    /// For symbols without a <c>Range</c>, e.g. the workspace project's own symbol, module symbols, or symbols from referenced libraries.
    /// </summary>
    /// <param name="workspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
    /// <param name="name">The name of the symbol.</param>
    /// <param name="kind">A <c>SymbolKind</c> (extended, LSP-compliant) metadata value describing the kind of symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol (<c>null</c> for the top-level library symbol).</param>
    protected Symbol(Uri workspaceRoot, string name, SymbolKindExt kind, Uri? parentUri = default, ScopeKind? scope = ScopeKind.Global)
        : this(workspaceRoot, scope ?? ScopeKind.Global, name, kind, default!, default!, parentUri)
    {
    }

    /// <summary>
    /// For symbols with a <c>Range</c>, i.e. symbols from the workspace project.
    /// </summary>
    protected Symbol(Uri workspaceRoot, ScopeKind scope, string name, SymbolKindExt kind, Range range, Range selectionRange, Uri? parentUri = default)
    {
        Name = name;
        Kind = (SymbolKind)kind;

        //IsUserWorkspace = range is not null && selectionRange is not null;
        ScopeKind = scope;

        WorkspaceRoot = workspaceRoot;
        ParentUri = parentUri ?? workspaceRoot;
        Uri = CreateUri(ParentUri, name);

        Range = range;
        SelectionRange = selectionRange ?? Range;
    }

    private static Uri CreateUri(Uri parent, string name)
    {
        var builder = new UriBuilder(parent)
        {
            Fragment = string.IsNullOrEmpty(parent.Fragment)
                ? name
                : $"{parent.Fragment.TrimStart('#')}.{name}"
        };
        return builder.Uri;
    }

    /// <summary>
    /// The <c>Name</c> of the symbol.
    /// </summary>
    public virtual string Name { get; }

    /// <summary>
    /// LSP-compliant <c>SymbolKind</c> value (may be extended) that the client can use to categorize the symbol.
    /// </summary>
    public SymbolKind Kind { get; init; }

    /// <summary>
    /// An immutable array where each <c>Uri</c> element refers to the <c>Uri</c> of a child symbol.
    /// </summary>
    public ImmutableArray<Uri> Children { get; init; }

    /// <summary>
    /// The entire document <c>Range</c> belonging to this symbol.
    /// </summary>
    public Range? Range { get; init; }

    /// <summary>
    /// The document <c>Range</c> to select when navigating to this symbol.
    /// </summary>
    public Range? SelectionRange { get; init; }

    /// <summary>
    /// A <c>Uri</c> that uniquely identifies the symbol.
    /// </summary>
    public Uri Uri { get; init; }
    /// <summary>
    /// The <c>Uri</c> of the parent symbol.
    /// </summary>
    /// <remarks>
    /// This property is actually <c>null</c> given a <c>SymbolKind.Project</c> symbol.
    /// </remarks>
    public Uri ParentUri { get; init; } = default!;

    /// <summary>
    /// The <c>Uri</c> of the parent library/project.
    /// </summary>
    public Uri WorkspaceRoot { get; init; }

    /// <summary>
    /// Describes how the runtime manages this symbol in memory.
    /// </summary>
    public ScopeKind ScopeKind { get; init; }

    /// <summary>
    /// Creates a copy of this symbol having the specified <c>Children</c>.
    /// </summary>
    public Symbol WithChildren(IEnumerable<Uri> children) => this with { Children = [.. children] };

    /// <summary>
    /// Gets an immutable dictionary of <c>SymbolProperty&lt;T&gt;</c> keyed by name for serialization.
    /// </summary>
    public ImmutableDictionary<object, object> Properties { get; init; } = [];
    /// <summary>
    /// Gets the value of the specified <c>SymbolProperty</c>.
    /// </summary>
    /// <param name="key">The <c>SymbolProperty</c> to get the value for.</param>
    /// <returns><c>null</c> if the specified property is undefined for this symbol.</returns>
    /// <remarks>
    /// Use <c>SymbolProperties</c> static members to 
    /// </remarks>
    public T? GetProperty<T>(SymbolProperty<T> key) => Properties.TryGetValue(key, out var value) ? (T)value : default;
    /// <summary>
    /// Creates a copy of this symbol having the specified <c>SymbolProperty</c> value.
    /// </summary>
    /// <param name="key">The <c>SymbolProperty</c> to affect.</param>
    /// <param name="value">The value to be assigned.</param>
    /// <returns></returns>
    public Symbol With<T>(SymbolProperty<T> key, T value) => this with { Properties = Properties.SetItem(key, value!) };


}
