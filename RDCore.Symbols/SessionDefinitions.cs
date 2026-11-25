using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Symbols;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;

namespace RDCore.Symbols;

// a module of a project that has a parse, addressed under the project's root. A module of parts has the directives they state (ModuleAssembly); any
// other has its tree's (Directives is null).
public sealed record class ParsedModule(Uri Uri, string Name, ModuleType Kind, ModuleParseResult Parse, ModuleDirectives? Directives = null)
{
    // the modules the parts of a project make, addressed under its root, and those that its parts make invalid (ModuleAssembly.Of), in the order of each
    // module's first part.
    public static (IReadOnlyList<ParsedModule> Modules, IReadOnlyList<RefusedModule> Refused) Assemble(Uri root, IEnumerable<ModulePart> parts)
    {
        var (modules, refused) = ModuleAssembly.Of(parts);
        return (
            [.. modules.Select(module => new ParsedModule(SymbolUri.Module(root, module.Name), module.Name, module.Kind, module.Parse, module.Directives))],
            refused);
    }

    // the directives of each module of parts, by its name (WorkspaceSymbolResolver.Compose).
    public static IReadOnlyDictionary<string, ModuleDirectives> DirectivesOf(IEnumerable<ParsedModule> modules)
        => modules.Where(module => module.Directives is not null)
            .ToDictionary(module => module.Name, module => module.Directives!.Value, StringComparer.OrdinalIgnoreCase);
}

// a project the session's project references (MS-VBAL §4.1), as its modules are defined in the session: addressed under its own root, bound among its own
// project's names (Resolver), and named as the project at the precedence of the reference (DefineSymbolsParams.Library, LibraryPriority).
public sealed record class ReferencedProjectModules(string Name, int Priority, Uri Root, IReadOnlyList<ParsedModule> Modules, ISymbolResolver Resolver)
{
    // the names the project gives the project that references it (ReferencedProjectSymbols.Of), at the precedence of the reference.
    public IEnumerable<Symbol> NamesFor(Uri sessionRoot, ImplicitDeclarationScope scope)
        => ReferencedProjectSymbols.Of(
            sessionRoot, Name, [.. Modules.SelectMany(module => SessionDefinition.SymbolsOf(Root, module, Resolver, scope))], Priority);
}

public enum SessionDefinitionPhase
{
    // the module's types are declared (DefineSymbolsParams.DeclareTypesOnly).
    DeclareTypes,

    // the module's declarations are bound and defined.
    Declare,

    // the module's code is loaded (DefineSymbolsParams.CodeOnly).
    Code,
}

// one definition a session is sent: the module, the project it is of (Referenced; null for the session's own), and its phase. The request is made when it
// is sent (ToRequest), so what fails in making it is the failure of that module alone.
public sealed record class SessionDefinition(
    SessionDefinitionPhase Phase, Uri SessionRoot, ParsedModule Module, ISymbolResolver Resolver, ImplicitDeclarationScope Scope, bool Replace,
    ReferencedProjectModules? Referenced = null)
{
    // the request; null for a module that declares no type, which has nothing to declare in the first phase. A module of a referenced project that is not a
    // standard module is a type itself, which the first phase declares: the session's own project's are composed before any module is defined, and a
    // referenced project's are not. Declared in the first phase, it is a type before any declaration that names it is bound, whatever the order of the
    // modules. The code goes as the JSON of its parse (json; PlatformJson.Serialize when it is not given).
    public DefineSymbolsParams? ToRequest(Func<ModuleParseResult, string>? json = null)
    {
        var tree = Module.Parse.SyntaxTree;
        var request = new DefineSymbolsParams
        {
            WorkspaceRoot = SessionRoot,
            ModuleUri = Module.Uri,
            ModuleName = Module.Name,
            Directives = ModuleKinds.DirectivesOf(Module.Kind, Module.Directives ?? tree.GetModuleDirectives()),
            ImplementedInterfaceNames = tree?.GetImplementedInterfaceNames() ?? [],
            ImplementedInterfaceRanges = tree?.GetImplementedInterfaceRanges() ?? [],
            Library = Referenced?.Name,
            LibraryPriority = Referenced?.Priority ?? 0,
            ModuleType = Module.Kind,
        };

        if (Phase is SessionDefinitionPhase.Code)
        {
            return request with { ParseResultJson = (json ?? PlatformJson.Serialize)(Module.Parse), CodeOnly = true };
        }

        var descriptors = SymbolDescriptorProjector.Project(SymbolsOf(Referenced?.Root ?? SessionRoot, Module, Resolver, Scope), Module.Uri);
        var declareTypesOnly = Phase is SessionDefinitionPhase.DeclareTypes;
        var isAType = Referenced is not null && Module.Kind is not ModuleType.StdModule;
        var declaresAType = descriptors.Any(descriptor => descriptor.Kind is SymbolDescriptorKind.UserDefinedType or SymbolDescriptorKind.Enum);
        if (declareTypesOnly && !isAType && !declaresAType)
        {
            return null;
        }

        // the precompiler constants a module's #If reads are the session's project's: a referenced project's module is sent without them.
        return request with
        {
            Symbols = descriptors,
            PrecompilerTriviaJson = Referenced is null ? DefineSymbolsParams.PrecompilerTriviaJsonOf(Module.Parse) : string.Empty,
            Replace = Replace,
            DeclareTypesOnly = declareTypesOnly,
        };
    }

    // the symbols of a module, addressed under its project's root and bound among its project's names.
    internal static IEnumerable<Symbol> SymbolsOf(Uri root, ParsedModule module, ISymbolResolver resolver, ImplicitDeclarationScope scope)
        => new SyntaxTreeSymbolProvider(root, module.Uri, module.Kind, module.Parse, resolver, withImplicitDeclarations: true, scope).ProvideSymbols();
}

// what a session is sent to define its project and the projects it references, in order. Every type exists before any declaration is bound to it, and every
// module is defined before the code of any is loaded, which is checked against everything the session declares: the types of the referenced projects, then
// the session's own; their declarations, then the session's own; the session's code, then theirs.
public static class SessionDefinitions
{
    public static IEnumerable<SessionDefinition> Of(
        Uri sessionRoot, IReadOnlyList<ParsedModule> modules, ISymbolResolver resolver, ImplicitDeclarationScope scope, bool replace,
        IReadOnlyList<ReferencedProjectModules>? referenced = null)
    {
        referenced ??= [];
        foreach (var phase in new[] { SessionDefinitionPhase.DeclareTypes, SessionDefinitionPhase.Declare })
        {
            foreach (var project in referenced)
            {
                foreach (var module in project.Modules)
                {
                    yield return new SessionDefinition(phase, sessionRoot, module, project.Resolver, scope, replace, project);
                }
            }

            foreach (var module in modules)
            {
                yield return new SessionDefinition(phase, sessionRoot, module, resolver, scope, replace);
            }
        }

        foreach (var module in modules)
        {
            yield return new SessionDefinition(SessionDefinitionPhase.Code, sessionRoot, module, resolver, scope, replace);
        }

        foreach (var project in referenced)
        {
            foreach (var module in project.Modules)
            {
                yield return new SessionDefinition(SessionDefinitionPhase.Code, sessionRoot, module, project.Resolver, scope, replace, project);
            }
        }
    }
}
