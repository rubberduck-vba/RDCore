using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Runtime.StdLib;

/// <summary>
/// The names the standard library is known by, and how to tell its symbols from those of the workspace and of the libraries it references.
/// </summary>
/// <remarks>
/// The library's symbols are provided by the platform's language core, which reads them off the declarations that define it. What is here is what anything that
/// meets one of those symbols needs to recognize it.
/// </remarks>
public static class StandardLibrary
{
    /// <summary>
    /// The name of the synthetic module every global the language environment provides hangs off.
    /// </summary>
    /// <remarks>
    /// A leading underscore is not a legal VBA identifier, so no source can name this module and no
    /// workspace module can collide with it. It is a standard module, so its members are promoted to the
    /// project scope and resolve unqualified, the way a standard library's members do.
    /// <para>
    /// It is also the parent of a global object's <em>class</em>. A predeclared instance is named after its
    /// class, so a class parented to the global scope would have the same <c>Uri</c> as its own instance,
    /// and the name would resolve to neither of them.
    /// </para>
    /// </remarks>
    public const string GlobalModuleName = "_Global";

    /// <summary>
    /// The name of the object the development environment exposes for diagnostic output.
    /// </summary>
    public const string DebugObjectName = "Debug";

    /// <summary>
    /// Whether <paramref name="symbol"/> is the synthesized <c>Debug</c> object: the default instance of its <c>Debug</c> class, a member of
    /// the library's <see cref="GlobalModuleName"/> module. A variable or a class of the workspace named <c>Debug</c> is not it.
    /// </summary>
    /// <remarks>
    /// <c>Debug</c> is written only as the <c>Debug.Print</c> and <c>Debug.Assert</c> statements. MS-VBA reads any other use of the name (a value,
    /// the target of an assignment or a <c>With</c> block) as a syntax error.
    /// </remarks>
    /// <param name="symbol">The symbol a name resolved to.</param>
    public static bool IsDebugObject(Symbol symbol)
        => symbol is VBPredeclaredInstanceSymbol { ClassModule: var classModule }
            && string.Equals(classModule.Name, DebugObjectName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(SymbolUri.ModuleNameOf(classModule.ParentUri), GlobalModuleName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The name of the project the standard library is.
    /// </summary>
    /// <remarks>
    /// It is what a project-qualified reference to the library names: <c>VBA.Strings.LenB</c>, <c>VBA.LenB</c>
    /// (<strong>MS-VBAL §5.6.12</strong>). It is the same in every dialect: VB6 loads the very same <c>VBA</c> library, and what
    /// it calls <c>VB</c> is the runtime library of its ActiveX controls. Where dialects differ, the library's members differ, and its
    /// name stays.
    /// </remarks>
    public const string LibraryName = RDCore.SDK.Workspace.RDCoreReference.StandardLibraryName;

    /// <summary>
    /// Whether <paramref name="symbol"/> is a member the standard library declares: a procedure of one of its modules, or a member of one of its classes.
    /// </summary>
    /// <remarks>
    /// It carries the key the library's reader stamps on what it reads (<see cref="SymbolProperties.ExternalTarget"/>), and no other library claims it as
    /// its own: a referenced library's members carry a key of their own kind, and the name of their library (<see cref="SymbolProperties.Library"/>). The
    /// library's name is stamped on a class and not on its members, which are only ever provided with the class.
    /// </remarks>
    /// <param name="symbol">The member.</param>
    /// <returns><see langword="true"/> for a member of the standard library's; <see langword="false"/> for the workspace's, or a referenced library's.</returns>
    public static bool IsLibraryMember(Symbol symbol)
        => symbol.GetProperty(SymbolProperties.ExternalTarget) is { Length: > 0 }
        && symbol.GetProperty(SymbolProperties.Library) is null or LibraryName;
}
