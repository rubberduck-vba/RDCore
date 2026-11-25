using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.StdLib;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §4.2: a module's VB_Name is a quoted-identifier, and an IDENTIFIER is not a reserved-identifier (§3.3.5.2). A module named by its file, which
// says no VB_Name, is named by the same rule. MS-VBA refuses such a name when a module is renamed, and when a standard module is imported ("Not a legal
// object name"). A reserved-type-identifier that is not a reserved-name (Long) is an IDENTIFIER, which MS-VBA refuses too: RD-VBA compiles it, and warns
// of it (RDC11034). A name that is not a Latin-identifier at all is not checked here: a client's own module of the session is named so on purpose
// (_Immediate), and the host is not told which module is the client's.
public static class ModuleNameStaticSemantics
{
    public static ImmutableArray<VBCompileErrorInfo> Check(ModuleNode module, string name)
    {
        if (!VBIdentifier.IsReservedIdentifier(name))
        {
            return [];
        }

        return [VBCompileErrorInfo.For(VBCompileErrorId.NotALegalObjectName, LocationOf(module),
            string.Format(Exceptions.VBCompileError_NotALegalObjectName_Verbose, name))];
    }

    // MS-VBAL §4.2: a module's name might not be the project name of a project that its project references. A library is referenced as a project is
    // (§4.1), and the standard library is the project VBA: each is a project symbol of the global scope, tagged with its name. MS-VBA refuses to rename a
    // module to such a name ("Name conflicts with existing module, project, or object library"). A module named as its own project keeps the name, which
    // its project symbol is not tagged with (WorkspaceSymbolResolver). The module of a referenced project, whose own references the session does not
    // have, is checked against the standard library alone, which every project references.
    public static ImmutableArray<VBCompileErrorInfo> CheckReferences(ModuleNode module, Symbol symbol, LexicalScope global)
    {
        var ownLibrary = symbol.GetProperty(SymbolProperties.Library);
        bool Conflicts(Symbol declared) => declared is VBProjectSymbol && declared.GetProperty(SymbolProperties.Library) is { } library
            && (ownLibrary is null || library == StandardLibrary.LibraryName);
        if (!global.DeclaredAs(symbol.Name).Any(Conflicts))
        {
            return [];
        }

        return [VBCompileErrorInfo.For(VBCompileErrorId.NameConflictsWithModuleProjectOrLibrary, LocationOf(module),
            string.Format(Exceptions.VBCompileError_NameConflictsWithModuleProjectOrLibrary_Verbose, symbol.Name))];
    }

    // MS-VBAL §4.2: every module of a project has a name no other module of it has, case aside. Of the modules that share one, none is the module of that
    // name, and none loads: which one would is the order the project lists them in, and the program is invalid. MS-VBA refuses to rename a module to a name
    // another has ("Name conflicts with existing module, project, or object library"), and names one it imports so another way (Dup1).
    public static IReadOnlySet<string> NamesTakenTwice(IEnumerable<string> names)
        => names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase).Where(group => group.Skip(1).Any())
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // the error of a module whose name another module of its project has (NamesTakenTwice).
    public static VBCompileErrorInfo NameTaken(ModuleNode module, string name)
        => VBCompileErrorInfo.For(VBCompileErrorId.NameConflictsWithModuleProjectOrLibrary, LocationOf(module),
            string.Format(Exceptions.VBCompileError_ModuleNameTaken_Verbose, name));

    // the name is reported where the module says it, and at the start of the module when its file says it.
    private static SourceLocation LocationOf(ModuleNode module)
        => module.GetDeclaredNameDirective()?.Location ?? new SourceLocation(module.SourceLocation.Uri, new SourceRange(0, 0, 0, 0));
}
