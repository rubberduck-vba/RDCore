using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Symbols;

// MS-VBAL §4.1, §5.2.3: the names a project of the workspace gives the projects that reference it, which bind them as they bind a library's.
// A procedural module is accessible unless it is a private module (§5.2.1.4); a class module when it is exposed (VB_Exposed, §5.2.4.1.1).
// A member is accessible when it is Public or Global, or has no access modifier where that makes it public (a procedure, an Enum, a user-defined type),
// and its module allows it (§5.3.1): a variable, a procedure or an Enum in a class module or a procedural module that is not private, a constant or a
// user-defined type in a procedural module that is not private. A Friend member is accessible within its project only.
// RD-VBA addition: the default instance of an accessible predeclared class (§5.2.4.1.2) is a name of the referencing projects too, as it is of its own,
// so Board.Reset calls Reset on the default instance of a class Board of a referenced project. §5.6.10 lists only the members of a global class's
// default instance among a referenced project's names.
public static class ReferencedProjectSymbols
{
    public static bool IsAccessible(VBModuleSymbol module)
    {
        ArgumentNullException.ThrowIfNull(module);

        return module is VBClassModuleSymbol ? module.Directives.Exposed : !module.Directives.PrivateModule;
    }

    // whether a referencing project can name the member, or reach it through an object of the class.
    public static bool IsAccessible(Symbol member, VBModuleSymbol module)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(module);

        var isClass = module is VBClassModuleSymbol;
        if (member is not AccessibleTypedSymbol accessible || !isClass && module.Directives.PrivateModule)
        {
            return false;
        }

        var isPublic = accessible.AccessModifier switch
        {
            AccessModifier.Public or AccessModifier.Global => true,
            AccessModifier.Implicit => member is not (VBModuleFieldVariableMemberSymbol or VBConstantMemberSymbol),
            _ => false,
        };

        return isPublic && (!isClass || member is not (VBConstantMemberSymbol or VBUserDefinedTypeMemberSymbol));
    }

    // the project symbol first (addressed under the referencing project's root, as a library's is), then the accessible modules, the default instances
    // of the accessible predeclared classes, their accessible members, and the members of the accessible Enum types. Each is tagged with the project's
    // name (SymbolProperties.Library) and its reference precedence (SymbolProperties.LibraryPriority), which the referencing project's resolver ranks it
    // by among the libraries. The rest of the symbols (the standard library's, the referenced project's own references, project symbol and the
    // default instances it defines for itself) is left out.
    public static ImmutableArray<Symbol> Of(Uri workspaceRoot, string projectName, IEnumerable<Symbol> symbols, int priority)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentNullException.ThrowIfNull(symbols);

        var all = symbols.ToArray();
        var modules = all.OfType<VBModuleSymbol>()
            .Where(module => module.GetProperty(SymbolProperties.Library) is null)
            .GroupBy(module => module.Uri.AbsoluteUri, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var names = ImmutableArray.CreateBuilder<Symbol>();
        names.Add(Tagged(new VBProjectSymbol(workspaceRoot, projectName)));
        var accessible = modules.Values.Where(IsAccessible).Select(module => Tagged(WithoutFriendMembers(module))).ToArray();
        names.AddRange(accessible);
        names.AddRange(accessible.OfType<VBClassModuleSymbol>()
            .Where(module => module.GetProperty(SymbolProperties.PredeclaredId))
            .Select(DefaultInstanceOf));

        // an enum's members parent to the enum, which parents to its module: they are accessible where it is.
        var enums = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in all.Where(symbol => symbol is not VBModuleSymbol and not VBEnumConstMemberSymbol))
        {
            if (modules.TryGetValue(member.ParentUri.AbsoluteUri, out var module) && IsAccessible(member, module))
            {
                if (member is VBEnumMemberSymbol)
                {
                    enums.Add(member.Uri.AbsoluteUri);
                }

                names.Add(Tagged(member));
            }
        }

        names.AddRange(all.OfType<VBEnumConstMemberSymbol>().Where(constant => enums.Contains(constant.ParentUri.AbsoluteUri)).Select(Tagged));
        return names.ToImmutable();

        Symbol Tagged(Symbol symbol) => ReferencedProjectSymbols.Tagged(symbol, projectName, priority);
    }

    // what a session that runs the project's code defines of it: the project symbol, as Of has it, then every module and every member, tagged as Of tags
    // them, and the default instance of every predeclared class (§5.2.4.1.2), which the project's own code names whether or not the class is exposed. The
    // session's resolver sees from the referencing projects only what is accessible (ScopeTreeProjects). The rest of the symbols is left out, as Of leaves
    // it.
    public static ImmutableArray<Symbol> Defined(Uri workspaceRoot, string projectName, IEnumerable<Symbol> symbols, int priority)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentNullException.ThrowIfNull(symbols);

        var all = symbols.ToArray();
        var modules = all.OfType<VBModuleSymbol>()
            .Where(module => module.GetProperty(SymbolProperties.Library) is null)
            .GroupBy(module => module.Uri.AbsoluteUri, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (VBModuleSymbol)Tagged(group.First()), StringComparer.Ordinal);

        var names = ImmutableArray.CreateBuilder<Symbol>();
        names.Add(Tagged(new VBProjectSymbol(workspaceRoot, projectName)));
        names.AddRange(modules.Values);
        names.AddRange(modules.Values.OfType<VBClassModuleSymbol>()
            .Where(module => module.GetProperty(SymbolProperties.PredeclaredId))
            .Select(DefaultInstanceOf));

        // an enum's members parent to the enum, which parents to its module.
        var enums = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in all.Where(symbol => symbol is not VBModuleSymbol and not VBEnumConstMemberSymbol))
        {
            if (modules.ContainsKey(member.ParentUri.AbsoluteUri))
            {
                if (member is VBEnumMemberSymbol)
                {
                    enums.Add(member.Uri.AbsoluteUri);
                }

                names.Add(Tagged(member));
            }
        }

        names.AddRange(all.OfType<VBEnumConstMemberSymbol>().Where(constant => enums.Contains(constant.ParentUri.AbsoluteUri)).Select(Tagged));
        return names.ToImmutable();

        Symbol Tagged(Symbol symbol) => ReferencedProjectSymbols.Tagged(symbol, projectName, priority);
    }

    // a global variable named after the class, declared as the class, with the library and the precedence of the class module (as Of tags it). It is
    // addressed under its class module (SymbolUri.DefaultInstance), so a class of the same name of another project has its instance at another address,
    // and parents to the global scope, as the default instance of a class of the project itself does.
    public static VBPredeclaredInstanceSymbol DefaultInstanceOf(VBClassModuleSymbol classModule)
    {
        ArgumentNullException.ThrowIfNull(classModule);
        if (classModule.GetProperty(SymbolProperties.Library) is not { Length: > 0 } library)
        {
            throw new ArgumentException("The class module is of no referenced project: it has no library.", nameof(classModule));
        }

        var instance = new VBPredeclaredInstanceSymbol(classModule)
        {
            Uri = SymbolUri.DefaultInstance(classModule.Uri),
        };

        return (VBPredeclaredInstanceSymbol)Tagged(instance, library, classModule.GetProperty(SymbolProperties.LibraryPriority));
    }

    // MS-VBAL §5.3.1.1: a class's Friend members are accessible within its project only, so the class a referencing project names has none.
    private static VBModuleSymbol WithoutFriendMembers(VBModuleSymbol module)
    {
        return module is VBClassModuleSymbol classModule
            ? classModule with { Members = WithoutFriend(classModule.Members), DefaultInterfaceMembers = WithoutFriend(classModule.DefaultInterfaceMembers) }
            : module;

        static ImmutableArray<VBTypeMemberSymbol> WithoutFriend(ImmutableArray<VBTypeMemberSymbol> members)
            => members.IsDefault ? members : [.. members.Where(member => member.AccessModifier is not AccessModifier.Friend)];
    }

    // a symbol of the referenced project, by its name and precedence: of a source project (MS-VBAL §4.1), whose code runs in the session.
    private static Symbol Tagged(Symbol symbol, string projectName, int priority)
        => symbol.With(SymbolProperties.Library, projectName).With(SymbolProperties.LibraryPriority, priority).With(SymbolProperties.OfSourceProject, true);
}
