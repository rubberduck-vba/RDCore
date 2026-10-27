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
    /// The address of a module, under the root of its project given as a path or a URI.
    /// </summary>
    /// <param name="workspaceRoot">The root of the module's project: an absolute path, or an absolute URI.</param>
    /// <param name="moduleName">The module's name, as its <c>Attribute VB_Name</c> says.</param>
    public static Uri Module(string workspaceRoot, string moduleName)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceRoot);
        return Module(new Uri(workspaceRoot), moduleName);
    }

    /// <summary>
    /// The address of a symbol that is a member of another - a procedure of a module, a variable of a procedure, a field of a type - or of a module, when the
    /// parent is the root of a project or a library.
    /// </summary>
    /// <param name="parent">The address of the symbol it is a member of, or the root of its project or library.</param>
    /// <param name="name">Its name, and the suffix that tells it from a namesake (<c>Name.Get</c> for a property accessor), when it has one.</param>
    public static Uri Member(Uri parent, string name)
    {
        ArgumentNullException.ThrowIfNull(parent);
        return new UriBuilder(parent)
        {
            Fragment = string.IsNullOrEmpty(parent.Fragment) ? name : $"{parent.Fragment.TrimStart('#')}.{name}",
        }.Uri;
    }
}
