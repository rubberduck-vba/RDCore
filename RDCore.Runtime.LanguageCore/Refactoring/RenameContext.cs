using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace.Forms;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Refactoring;

// the designer of a form's module names the class whose events the form handles, as its own (MS-VBAL §5.3.1.8).
public sealed record class RenameModule(Uri Module, ModuleNode Syntax, string Source, FormDescriptor? Designer = null);

public sealed record class RenameContext(
    ISymbolResolver Resolver, ScopeTree ScopeTree, IReadOnlyList<RenameModule> Modules, StaticEnvironment? Environment = null,
    IReadOnlyList<IPossibleClassesSource>? ClassSources = null);
