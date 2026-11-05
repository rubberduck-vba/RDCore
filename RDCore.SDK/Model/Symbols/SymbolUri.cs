using OmniSharp.Extensions.LanguageServer.Protocol;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// The addresses of symbols: what a module, and what a member of anything, is identified by (<see cref="Abstract.Symbol.Uri"/>).
/// </summary>
/// <remarks>
/// <para>
/// A module is addressed under the root of the project it is part of, by its name, as the fragment: <c>file:///C:/Work/Battleship#GameBoard</c>. A member of
/// anything is addressed under it, its name appended to the fragment after a dot: <c>file:///C:/Work/Battleship#GameBoard.Reset</c>. The language server and
/// the environment host address the same symbol by the same <see cref="Uri"/>, and a symbol's address is its identity (<see cref="Abstract.Symbol.SemanticId"/>).
/// </para>
/// <para>
/// No two declarations of a module have one address. Where a module may declare a name twice, one of the two has a suffix after its name: a <c>]</c>, which
/// no name has (an identifier is letters, digits and underscores, and a foreign name ends at its first <c>]</c>: <strong>MS-VBAL §3.3.5</strong>,
/// <strong>§3.3.5.3</strong>), then the reserved word of what it declares:
/// </para>
/// <list type="bullet">
/// <item>a <c>Property Let</c> and a <c>Property Set</c> are <c>Item]Let</c> and <c>Item]Set</c>, beside their <c>Property Get</c>, <c>Item</c>;</item>
/// <item>an Enum, a Type and an Event are <c>PlayerType]Enum</c>, <c>Point]Type</c> and <c>Changed]Event</c>, beside a variable, a constant or a procedure of
/// the name (<strong>MS-VBAL §5.2.3.1</strong> to <strong>§5.2.3.4</strong>, <strong>§5.2.4.3</strong>).</item>
/// </list>
/// <para>
/// What such a symbol declares is addressed under that: <c>GameBoard.PlayerType]Enum.HumanControlled</c>, <c>GameBoard.Item]Let.value</c>. A name is read off
/// an address with <see cref="NameOf"/>, and a symbol is shown with <see cref="QualifiedNameOf"/>, which leave the suffixes out. A name may be the suffix's word
/// itself (a field of a Type named <c>Type</c>, Excel's <c>Worksheet.Type</c>): only what follows the <c>]</c> is a suffix.
/// </para>
/// <para>
/// This is the one place the rule is written: a symbol, the descriptors of symbols on the wire, and every component that names a module address them with it.
/// </para>
/// </remarks>
public static class SymbolUri
{
    /// <summary>
    /// The address of a module: the root of its project, with its name as the fragment.
    /// </summary>
    /// <param name="workspaceRoot">The root of the module's project.</param>
    /// <param name="moduleName">The module's name, as its <c>Attribute VB_Name</c> says.</param>
    public static Uri Module(Uri workspaceRoot, string moduleName)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        return new UriBuilder(workspaceRoot) { Fragment = moduleName }.Uri;
    }

    /// <summary>
    /// The address of a module, under the root of its project given as a path or a URI (<see cref="Root"/>).
    /// </summary>
    /// <param name="workspaceRoot">The root of the module's project: an absolute path, or an absolute URI.</param>
    /// <param name="moduleName">The module's name, as its <c>Attribute VB_Name</c> says.</param>
    /// <exception cref="ArgumentException">The root is neither an absolute path nor an absolute URI.</exception>
    public static Uri Module(string workspaceRoot, string moduleName) => Module(Root(workspaceRoot), moduleName);

    /// <summary>
    /// The root of a project, given as a path or as a URI of any scheme: what the modules of the project are addressed under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A path and a <c>file</c> URI of the same folder are the same root, however the URI escapes its path: <c>file:///c%3A/Work</c>, as a client may write
    /// it (<strong>LSP 3.17</strong> §URI), is the root <c>c:\Work</c> is.
    /// </para>
    /// <para>
    /// A URI of another scheme is the root as it is: a client's documents need not be files (<c>untitled:Untitled-1</c>).
    /// </para>
    /// <para>
    /// The root of a path is written as its <c>file</c> URI (<c>file:///tmp/Work</c>), never as the path: a root travels between the platform's components
    /// (<c>DefineSymbolsParams.WorkspaceRoot</c>) as the text it is written with, and the text of a path is read back as a relative URI where a path begins with
    /// <c>/</c>, which addresses nothing.
    /// </para>
    /// </remarks>
    /// <param name="workspaceRoot">An absolute path, or an absolute URI.</param>
    /// <exception cref="ArgumentException">The root is neither: a relative path addresses nothing until it is said what it is relative to.</exception>
    public static Uri Root(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceRoot);
        if (Path.IsPathFullyQualified(workspaceRoot))
        {
            return FileRoot(workspaceRoot);
        }

        if (Uri.TryCreate(workspaceRoot, UriKind.Absolute, out var uri))
        {
            return uri.IsFile ? FileRoot(FileSystemPathOf(uri)) : uri;
        }

        throw new ArgumentException(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "'{0}' is neither an absolute path nor an absolute URI: the modules of a project are addressed under a root that is.", workspaceRoot), nameof(workspaceRoot));
    }

    // the root of a folder, written as its file URI: a Uri made of a path keeps the path as its text.
    private static Uri FileRoot(string path) => new(new Uri(path).AbsoluteUri);

    // the path a file URI is of, however it escapes it; DocumentUri writes a drive's letter in lowercase, and the root keeps it as the URI writes it.
    private static string FileSystemPathOf(Uri file)
    {
        var path = DocumentUri.From(file).GetFileSystemPath();
        return path is [_, ':', ..] && file.AbsolutePath.TrimStart('/') is [var drive, ..] ? drive + path[1..] : path;
    }

    /// <summary>
    /// The root the module of a document is addressed under: the root of the project it is part of, or the document's own address when it is part of none.
    /// </summary>
    /// <remarks>
    /// A file is part of the project of the workspace, wherever it is: a document a client opens from outside the workspace's folder is compiled with the
    /// workspace's modules, as a program a shell loads is. A document that is no file - one the client holds and has not saved, <c>untitled:Untitled-1</c> - is
    /// part of no project, and so is any document of a server that has no workspace: it is a project of its own, addressed under its own URI, which no module of
    /// a workspace is.
    /// </remarks>
    /// <param name="document">The address of the document, as the client gives it.</param>
    /// <param name="workspaceRoot">The root of the workspace, as a path or a URI; empty when the server has no workspace.</param>
    public static Uri RootOf(Uri document, string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.IsFile && !string.IsNullOrEmpty(workspaceRoot)
            ? Root(workspaceRoot)
            : new UriBuilder(document) { Fragment = string.Empty }.Uri;
    }

    /// <summary>
    /// Where a symbol is in its project or library, as its address has it: the names of the module and of each member down to the symbol, joined by dots
    /// (<c>GameBoard.Reset.Count</c>), as they were written, and not as an address escapes them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Uri.Fragment"/> is escaped: the address of <c>Modèle</c> has the fragment <c>#Mod%C3%A8le</c>. A name read from it is unescaped, and a name
    /// compared with one, shown, or looked up is read with this, not from the fragment.
    /// </para>
    /// <para>
    /// A relative URI addresses no symbol: every symbol is addressed under the absolute root of its project or library (<see cref="Root"/>), and a relative
    /// one reaching this is a mistake of whatever made it, which is not covered up as an empty name.
    /// </para>
    /// </remarks>
    /// <param name="symbol">The address of a symbol, or the root of a project or a library.</param>
    /// <returns>The symbol's path, or an empty string for a root.</returns>
    /// <exception cref="ArgumentException"><paramref name="symbol"/> is a relative URI.</exception>
    public static string PathOf(Uri symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        if (!symbol.IsAbsoluteUri)
        {
            throw new ArgumentException(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "'{0}' is a relative URI: a symbol is addressed under the absolute root of its project or library.", symbol.OriginalString), nameof(symbol));
        }

        return Uri.UnescapeDataString(symbol.Fragment.TrimStart('#'));
    }

    /// <summary>
    /// The name of the module a symbol is addressed under (<see cref="Module(Uri, string)"/>): the first name of its path (<see cref="PathOf"/>), as the
    /// module's name was written, and not as an address escapes it.
    /// </summary>
    /// <remarks>
    /// A module's name is an identifier (<strong>MS-VBAL §4.2</strong>), which has no dot (<strong>MS-VBAL §3.3.5</strong>): whatever follows the first one is
    /// the path of a member.
    /// </remarks>
    /// <param name="symbol">The address of a module, or of any symbol declared in one.</param>
    /// <returns>The module's name, or an empty string for the root of a project or a library.</returns>
    /// <exception cref="ArgumentException"><paramref name="symbol"/> is a relative URI.</exception>
    public static string ModuleNameOf(Uri symbol)
    {
        var path = PathOf(symbol);
        var dot = path.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? path : path[..dot];
    }

    // what tells a name from the suffix after it: no name has it. An identifier is letters, digits and underscores (MS-VBAL §3.3.5), and a foreign name
    // ends at its first ']' (§3.3.5.3). A field of a Type may be named Let or Type, and a library's member Type: the word alone tells nothing.
    private const char SuffixDelimiter = ']';

    // the reserved words a symbol's address has after its name, when the name alone is a namesake's (Symbol.UriSuffix).
    internal const string LetSuffix = "Let";
    internal const string SetSuffix = "Set";
    internal const string EnumSuffix = "Enum";
    internal const string TypeSuffix = "Type";
    internal const string EventSuffix = "Event";

    // a name of a path, without the suffix after it.
    private static string WithoutSuffix(string name) => name.IndexOf(SuffixDelimiter, StringComparison.Ordinal) is >= 0 and var at ? name[..at] : name;

    /// <summary>
    /// The name of the symbol an address is of: the last name of its path (<see cref="PathOf"/>), without the suffix that tells the symbol from a namesake of
    /// its module (<c>Item]Let</c> for a <c>Property Let</c>, <c>PlayerType]Enum</c> for an Enum), as the name was written.
    /// </summary>
    /// <remarks>
    /// A name read from an address is read with this, not by splitting the path: <c>GameBoard.PlayerType]Enum</c> is the address of <c>PlayerType</c>, and
    /// <c>GameBoard.PlayerType]Enum.Human</c> of <c>Human</c>.
    /// </remarks>
    /// <param name="symbol">The address of a symbol.</param>
    /// <returns>The symbol's name, or an empty string for the root of a project or a library.</returns>
    /// <exception cref="ArgumentException"><paramref name="symbol"/> is a relative URI.</exception>
    public static string NameOf(Uri symbol)
    {
        var path = PathOf(symbol);
        return WithoutSuffix(path[(path.LastIndexOf('.') + 1)..]);
    }

    /// <summary>
    /// A symbol as code names it, from its module down: the names of its path (<see cref="PathOf"/>), without the suffixes that tell a symbol from a namesake
    /// (<see cref="NameOf"/>). <c>GameBoard.PlayerType]Enum.Human</c> is <c>GameBoard.PlayerType.Human</c>, and <c>Cls.Item]Let.value</c> is
    /// <c>Cls.Item.value</c>.
    /// </summary>
    /// <remarks>
    /// What is shown of a symbol is this; what identifies it is its address. Two namesakes of a module (an Enum and a Property Get of one name) have one
    /// qualified name and two addresses.
    /// </remarks>
    /// <param name="symbol">The address of a symbol.</param>
    /// <returns>The symbol's qualified name, or an empty string for the root of a project or a library.</returns>
    /// <exception cref="ArgumentException"><paramref name="symbol"/> is a relative URI.</exception>
    public static string QualifiedNameOf(Uri symbol) => string.Join('.', PathOf(symbol).Split('.').Select(WithoutSuffix));

    /// <summary>
    /// The address of a symbol that is a member of another - a procedure of a module, a variable of a procedure, a field of a type - or of a module, when the
    /// parent is the root of a project or a library.
    /// </summary>
    /// <param name="parent">The address of the symbol it is a member of, or the root of its project or library.</param>
    /// <param name="name">Its name.</param>
    public static Uri Member(Uri parent, string name) => Member(parent, name, suffix: null);

    // the address of a member whose name alone is a namesake's (Symbol.UriSuffix): its name, then the suffix after a delimiter no name has.
    internal static Uri Member(Uri parent, string name, string? suffix)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var last = suffix is null ? name : $"{name}{SuffixDelimiter}{suffix}";
        return new UriBuilder(parent)
        {
            Fragment = PathOf(parent) is { Length: > 0 } path ? $"{path}.{last}" : last,
        }.Uri;
    }
}
