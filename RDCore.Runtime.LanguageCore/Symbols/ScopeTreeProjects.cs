using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.LanguageCore.Symbols;

// the project code is of, as a lookup from a scope sees it: the root of its project scope, and the library its module is tagged with, none for the
// session's own project.
internal readonly record struct ProjectOrigin(Uri? Project, string? Library);

// MS-VBAL §4.1, §5.6.10: what the code of one project of a scope tree sees of another's. A project reference gives the referencing project the public
// entities of the project it names: a Public or Global variable, constant, procedure, user-defined type or Enum of a procedural module that is not a
// private module (§5.2.3.1, §5.2.3.2, §5.3.1.1, §5.2.3.3, §5.2.3.4; Option Private Module, §5.2.1.4), and a class whose instancing exposes it with its
// public members (§5.2.4.1.1). A Private member is accessible within its module only, a Friend one within its project only (§5.3.1.1).
// ReferencedProjectSymbols decides each of them.
// The names of a project the session's project references are tagged with that project's name (SymbolProperties.Library), and its modules are under a
// root of their own; a type library's are under the session's root, where every project symbol is. The session's own names are tagged with no library,
// and the code of a referenced project sees none of them: no project references the session's (§4.1, a project's references are its own).
internal sealed class ScopeTreeProjects
{
    private static readonly ConditionalWeakTable<ScopeTree, ScopeTreeProjects> Trees = new();

    private readonly Dictionary<string, VBModuleSymbol> _modules;
    private readonly Dictionary<string, VBEnumMemberSymbol> _enums;
    private readonly HashSet<string> _roots;
    private readonly HashSet<string> _referencedRoots;

    private ScopeTreeProjects(ScopeTree tree)
    {
        var global = tree.Global.Declarations.ToArray();
        _modules = global.OfType<VBModuleSymbol>()
            .GroupBy(module => module.Uri.AbsoluteUri, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        // an Enum that is seen outside its module is in its project's scope, with its constants.
        _enums = tree.Projects.SelectMany(project => project.Declarations.OfType<VBEnumMemberSymbol>())
            .GroupBy(declared => declared.Uri.AbsoluteUri, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        _roots = tree.Projects.Select(project => project.Uri.AbsoluteUri).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var named = global.OfType<VBProjectSymbol>().Select(project => project.WorkspaceRoot.AbsoluteUri).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _referencedRoots = _roots
            .Where(root => !named.Contains(root)
                && _modules.Values.Where(module => SameRoot(module.WorkspaceRoot.AbsoluteUri, root)).All(module => LibraryOf(module) is not null))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static ScopeTreeProjects Of(ScopeTree tree) => Trees.GetValue(tree, static each => new ScopeTreeProjects(each));

    public ProjectOrigin OriginOf(IEnumerable<LexicalScope> origin)
    {
        var scopes = origin as IReadOnlyCollection<LexicalScope> ?? [.. origin];
        var project = scopes.FirstOrDefault(scope => scope.Kind == LexicalScopeKind.Project)?.Uri;
        var module = scopes.FirstOrDefault(scope => scope.Kind == LexicalScopeKind.Module) is { } moduleScope
            && _modules.TryGetValue(moduleScope.Uri.AbsoluteUri, out var declared)
                ? declared
                : null;

        return new ProjectOrigin(project, module is null ? null : LibraryOf(module));
    }

    // a name of the origin's own project.
    public static bool IsOwn(Symbol symbol, ProjectOrigin origin) => string.Equals(LibraryOf(symbol), origin.Library, StringComparison.Ordinal);

    // MS-VBAL §5.6.10: whether code of the origin's project sees a name another scope of the tree declares: a name of its own project, of a type library
    // or of no project is seen; a name of the session's own project is not, from another; a referenced project's name is seen when it is accessible.
    public bool IsSeen(Symbol candidate, ProjectOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (IsOwn(candidate, origin))
        {
            return true;
        }

        var root = candidate.WorkspaceRoot.AbsoluteUri;
        if (origin.Project is { } project && SameRoot(root, project.AbsoluteUri))
        {
            return true;
        }

        if (LibraryOf(candidate) is null)
        {
            return !_roots.Contains(root);
        }

        return !_referencedRoots.Contains(root) || IsAccessible(candidate, origin.Project);
    }

    // MS-VBAL §5.6.12: whether code of the origin's project sees a member of a module it qualifies, as the module's project's visibility says.
    public bool IsSeen(Symbol member, VBModuleSymbol module, ProjectOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(module);

        var root = module.WorkspaceRoot.AbsoluteUri;
        if (origin.Project is { } project && SameRoot(root, project.AbsoluteUri) || _referencedRoots.Contains(root))
        {
            return ProjectVisibility.Instance.IsVisibleFrom(member, module, origin.Project);
        }

        // a type library's module: what its project sees of it is what every project sees. The session's own modules are not seen from another project,
        // and code of no known project sees no module's members.
        return origin.Project is not null && LibraryOf(module) is not null && ProjectVisibility.Instance.IsProjectVisible(member, module);
    }

    private bool IsAccessible(Symbol candidate, Uri? originProject) => candidate switch
    {
        VBProjectSymbol => true,
        VBModuleSymbol module => ReferencedProjectSymbols.IsAccessible(ModuleAt(module.Uri) ?? module),
        VBPredeclaredInstanceSymbol instance => ReferencedProjectSymbols.IsAccessible(ModuleAt(instance.ClassModule.Uri) ?? instance.ClassModule),

        // an Enum's constant is seen where its Enum is.
        VBEnumConstMemberSymbol constant => _enums.TryGetValue(constant.ParentUri.AbsoluteUri, out var declaringEnum)
            && ModuleAt(declaringEnum.ParentUri) is { } enumModule
            && ProjectVisibility.Instance.IsVisibleFrom(declaringEnum, enumModule, originProject),
        _ => ModuleAt(candidate.ParentUri) is { } module && ProjectVisibility.Instance.IsVisibleFrom(candidate, module, originProject),
    };

    private VBModuleSymbol? ModuleAt(Uri uri) => _modules.TryGetValue(uri.AbsoluteUri, out var module) ? module : null;

    private static string? LibraryOf(Symbol symbol) => symbol.GetProperty(SymbolProperties.Library);

    private static bool SameRoot(string root, string other) => string.Equals(root, other, StringComparison.OrdinalIgnoreCase);
}
