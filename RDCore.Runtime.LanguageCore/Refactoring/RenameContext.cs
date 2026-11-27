using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
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
    IReadOnlyList<IPossibleClassesSource>? ClassSources = null)
{
    // whether a location of a parse of a module is in a branch of an #If block that is not live (MS-VBAL §3.4), as the environment host evaluated the
    // module: null when that is not known, and a check reads every branch as an alternative of the others.
    public Func<ModuleParseResult, Func<SourceLocation, bool>?> DeadTestOf { get; init; } = static _ => null;

    // the lower bound Option Base gives the arrays of a module (MS-VBAL §5.2.1.2): a module of parts has one, which any of its parts states (RD-VBAL §5.1),
    // and each part is one of the modules of its address here, with its own tree.
    public int OptionBaseOf(Uri module)
        => Modules.Any(part => part.Module.AbsoluteUri == module.AbsoluteUri && part.Syntax.GetOptionBase() != 0) ? 1 : 0;
}
