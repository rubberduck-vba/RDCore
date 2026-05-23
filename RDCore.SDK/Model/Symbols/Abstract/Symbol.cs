using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Server.ProtocolExtensions;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// The base <c>Symbol</c> type. All symbols must be addressable with a <c>Uri</c> that is unique across the workspace.
/// </summary>
/// <remarks>
/// The <c>Name</c> of a symbol is not necessarily a valid <em>identifier</em> value.
/// </remarks>
public abstract record class Symbol
{
    /// <summary>
    /// The base <c>Symbol</c> type. All symbols must be addressable with a <c>Uri</c> that is unique across the workspace.
    /// </summary>
    /// <remarks>
    /// The <c>Name</c> of a symbol is not necessarily a valid <em>identifier</em> value.
    /// </remarks>
    /// <param name="workspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol (<c>null</c> for any top-level library/project workspace symbol).</param>
    /// <param name="name">The name of the symbol.</param>
    /// <param name="scope">The allocation scope of the symbol.</param>
    /// <param name="kind">A <c>SymbolKind</c> (extensible) metadata value describing the kind of symbol.</param>
    protected Symbol(Uri workspaceRoot, Uri parentUri, string name, ScopeKind scope, SymbolKindExt kind)
    {
        WorkspaceRoot = workspaceRoot;
        ParentUri = parentUri ?? workspaceRoot;
        Uri = CreateUri(ParentUri, name);

        Name = name;
        ScopeKind = scope;
        Kind = kind;
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
    /// A <c>Uri</c> that uniquely identifies the symbol.
    /// </summary>
    public Uri Uri { get; init; }
    /// <summary>
    /// The <c>Uri</c> of the parent symbol.
    /// </summary>
    /// <remarks>
    /// ⚠️ This property is actually <c>null</c> given a <c>LibrarySymbol</c>.
    /// </remarks>
    public Uri ParentUri { get; init; } = default!;
    /// <summary>
    /// The <c>Uri</c> of the parent library/project.
    /// </summary>
    public Uri WorkspaceRoot { get; init; }


    /// <summary>
    /// The <c>Name</c> of the symbol.
    /// </summary>
    public virtual string Name { get; }

    /// <summary>
    /// Describes the <c>SymbolKind</c>, the type of symbol.
    /// </summary>
    /// <remarks>
    /// Serialized as a simple <c>int</c>; the internal model uses an extended set beyond LSP 3.17 that clients may ignore.
    /// </remarks>
    public SymbolKindExt Kind { get; init; }
    /// <summary>
    /// Describes the <c>SymbolKind</c>, the type of symbol.
    /// </summary>
    /// <remarks>
    /// Can be used by a LSP client to categorize symbols.
    /// </remarks>
    public SymbolKind SymbolKind => (SymbolKind)Kind;

    /// <summary>
    /// An immutable array where each <c>Uri</c> element refers to the <c>Uri</c> of a child symbol.
    /// </summary>
    public ImmutableArray<Uri> Children { get; init; }

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
