using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Refactoring;

public sealed record class RenameModule(Uri Module, ModuleNode Syntax, string Source);

public sealed record class RenameContext(
    ISymbolResolver Resolver, ScopeTree ScopeTree, IReadOnlyList<RenameModule> Modules, StaticEnvironment? Environment = null,
    IReadOnlyList<IPossibleClassesSource>? ClassSources = null);
