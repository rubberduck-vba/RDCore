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
// user-defined type in a procedural module that is not private. A Friend member is accessible within its project only, and a class's default instance
// (VB_PredeclaredId) is a name of its own project only.
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

    // the project symbol first (addressed under the referencing project's root, as a library's is), then the accessible modules, their accessible
    // members, and the members of the accessible Enum types. Each is tagged with the project's name (SymbolProperties.Library) and its reference
    // precedence (SymbolProperties.LibraryPriority), which the referencing project's resolver ranks it by among the libraries. The rest of the symbols
    // (the standard library's, the referenced project's own references, project symbol and default instances) is left out.
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
        names.AddRange(modules.Values.Where(IsAccessible).Select(Tagged));

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

        Symbol Tagged(Symbol symbol) => symbol.With(SymbolProperties.Library, projectName).With(SymbolProperties.LibraryPriority, priority);
    }
}
