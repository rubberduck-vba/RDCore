using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Server.ProtocolExtensions;
using System.Collections.Immutable;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols.Abstract;

public record class SymbolProperty<T>(string Name) { }
internal static class SymbolProperties
{
    /// <summary>
    /// The programmatic name of a <see cref="ModuleSymbol"/>, as determined by the <c>VB_Name</c> attribute.
    /// </summary>
    public static readonly SymbolProperty<string> Name = new(nameof(Name));
    /// <summary>
    /// The value of the <c>VB_PredeclaredId</c> attribute of a <see cref="ClassModuleSymbol"/>.
    /// </summary>
    public static readonly SymbolProperty<bool> PredeclaredId = new(nameof(PredeclaredId));
    /// <summary>
    /// The value of the <c>VB_Exposed</c> attribute of a <see cref="ClassModuleSymbol"/>
    /// </summary>
    public static readonly SymbolProperty<bool> Exposed = new(nameof(Exposed));
    /// <summary>
    /// The value of the <c>VB_UserMemId</c> attribute of a <see cref="VBTypeMemberSymbol"/>
    /// </summary>
    public static readonly SymbolProperty<int> MemberId = new(nameof(MemberId));

    /// <summary>
    /// <c>true</c> for <c>Static</c> declarations, whether explicitly in local scope or implicitly at the procedure level.
    /// </summary>
    public static readonly SymbolProperty<bool> IsStatic = new(nameof(IsStatic));
    /// <summary>
    /// <c>false</c> for conditionally-compiled symbols in a statically inactive branch.
    /// </summary>
    public static readonly SymbolProperty<bool> IsActive = new(nameof(IsActive));

    /// <summary>
    /// The <c>Option Explicit</c> flag.
    /// </summary>
    public static readonly SymbolProperty<bool> OptionExplicit = new(nameof(OptionExplicit));
    /// <summary>
    /// The <c>Option Private Module</c> flag.
    /// </summary>
    public static readonly SymbolProperty<bool> OptionPrivateModule = new(nameof(OptionPrivateModule));
    /// <summary>
    /// The <c>Option Base</c> flag.
    /// </summary>
    public static readonly SymbolProperty<int> OptionBase = new(nameof(OptionBase));
    /// <summary>
    /// The <c>Option Compare</c> flag.
    /// </summary>
    public static readonly SymbolProperty<VBOptionCompare> OptionCompare = new(nameof(OptionCompare));
    /// <summary>
    /// A small documentation string about this symbol.
    /// </summary>
    /// <remarks>
    /// Provided via <c>VB_Description</c> attributes.
    /// </remarks>
    public static readonly SymbolProperty<string> DocString = new(nameof(DocString));
    /// <summary>
    /// A metadata flag that is used for controlling the member behavior.
    /// </summary>
    /// <remarks>
    /// Provided via <c>VB_UserMemId</c> attributes; unique for each member of a given module, with value 0 denoting the default member.
    /// </remarks>
    public static readonly SymbolProperty<int> UserMemId = new(nameof(UserMemId));
    /// <summary>
    /// A metadata flag that is used for controlling the member behavior.
    /// </summary>
    /// <remarks>
    /// Provided via <c>VB_MemberFlags</c> attributes, with a handful of useful "magic" values.
    /// </remarks>
    public static readonly SymbolProperty<int> MemberFlags = new(nameof(MemberFlags));

}


public enum ScopeKind
{
    /// <summary>
    /// A pseudo-scope for pseudo-symbols that aren't allocated in memory, like <c>VBVoidValue</c>.
    /// </summary>
    Unallocated,
    /// <summary>
    /// Symbols from referenced libraries, mostly. Lives in the globals heap.
    /// </summary>
    Global,
    /// <summary>
    /// Procedure level, scoped to the local stack frame.
    /// </summary>
    Local,
    /// <summary>
    /// Module level, lives in the workspace statics heap.
    /// </summary>
    Module,
    /// <summary>
    /// Instance level, lives in the object heap.
    /// </summary>
    Instance,
}

public abstract record class Symbol
{
    /// <summary>
    /// For symbols representing a user workspace project or a referenced library.
    /// </summary>
    /// <param name="workspaceRoot">A <c>Uri</c> representing the absolute path to the workspace root, or referenced library.</param>
    /// <param name="name">The name of the symbol.</param>
    protected Symbol(Uri workspaceRoot, string name)
        : this(workspaceRoot, name, SymbolKindExt.Project)
    {
    }

    /// <summary>
    /// For symbols without a <c>Range</c>, e.g. the workspace project's own symbol, symbols from referenced libraries.
    /// </summary>
    /// <param name="workspaceRoot">A <c>Uri</c> representing the absolute path to the library.</param>
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

        ParentUri = parentUri ?? workspaceRoot;
        Uri = CreateUri(ParentUri, name);

        Range = range;
        SelectionRange = selectionRange ?? Range;
    }

    public virtual string Name { get; }
    public SymbolKind Kind { get; init; }

    public ImmutableArray<Uri> Children { get; init; }

    public Range? Range { get; init; }
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
    /// Describes how the runtime manages this symbol in memory.
    /// </summary>
    public ScopeKind ScopeKind { get; init; }

    public Symbol WithChildren(IEnumerable<Uri> children) => this with { Children = [.. children] };
    public static Uri CreateUri(Uri parent, string name)
    {
        var builder = new UriBuilder(parent)
        {
            Fragment = string.IsNullOrEmpty(parent.Fragment)
                ? name
                : $"{parent.Fragment.TrimStart('#')}.{name}"
        };
        return builder.Uri;
    }

    public ImmutableDictionary<object, object> Properties { get; init; } = [];
    public T? Get<T>(SymbolProperty<T> key) => Properties.TryGetValue(key, out var value) ? (T)value : default;
    public Symbol With<T>(SymbolProperty<T> key, T value) => this with { Properties = Properties.SetItem(key, value!) };
}
