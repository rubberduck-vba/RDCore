using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Refactoring;

/// <summary>
/// A module of the workspace, as a rename reads it: its syntax tree, and the text the tree was parsed from.
/// </summary>
/// <param name="Module">The address of the module (<c>workspace#Module</c>), as the scope tree has it.</param>
/// <param name="Syntax">The syntax tree of the module.</param>
/// <param name="Source">The text of the module, whose lines the positions of <paramref name="Syntax"/> are positions of.</param>
public sealed record class RenameModule(Uri Module, ModuleNode Syntax, string Source);

/// <summary>
/// What a rename is worked out in: the workspace, composed, and every module of it.
/// </summary>
/// <param name="Resolver">The resolver of the composed workspace, which says what each name means where it is written.</param>
/// <param name="ScopeTree">The scope tree of the composed workspace.</param>
/// <param name="Modules">Every module of the workspace: a name is renamed wherever it refers to the symbol, and nowhere else.</param>
/// <param name="Environment">What the environment the code is compiled for decides, when it is stated.</param>
/// <param name="ClassSources">
/// What else knows the classes of the objects an expression may hold (<see cref="IPossibleClassesSource"/>), beyond the assignments of the code
/// (<see cref="AssignedClasses"/>), which are always read.
/// </param>
public sealed record class RenameContext(
    ISymbolResolver Resolver, ScopeTree ScopeTree, IReadOnlyList<RenameModule> Modules, StaticEnvironment? Environment = null,
    IReadOnlyList<IPossibleClassesSource>? ClassSources = null);
