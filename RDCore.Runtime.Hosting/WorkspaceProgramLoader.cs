using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.External;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Runtime.Semantics;
using RDCore.Symbols;
using System.Diagnostics;
using System.IO.Abstractions;

namespace RDCore.Runtime.Hosting;

// a workspace loaded to be run: the session it was loaded in, the modules that loaded, how long each step took, what kept a module from being parsed or
// loaded, the libraries the project references, and the documents of the project's own files that parsed, each with its parse.
internal sealed record class LoadedProgram(
    EnvironmentSessionProvider Provider, IReadOnlyList<ParsedModule> Loaded, TimeSpan Parse, TimeSpan Symbols, TimeSpan Load, TimeSpan Sweep,
    IReadOnlyList<string> Problems, IReadOnlyList<LibraryDescription> Libraries, IReadOnlyList<RDCoreDocument> Documents);

// loads a workspace in process, the way the environment host loads it: the session composed from the project, each module's symbols and code defined in it.
// rdc bench, run, build and compilation load a workspace with it, and so does a composer. libraries: where the descriptions of the libraries a project
// references are found. options: the workspace's settings, which say the files that may have statements outside their procedures; the defaults when none are
// given.
internal sealed class WorkspaceProgramLoader(
    IFileSystem fileSystem, IErrorDetailsRecorder errorDetails, ILoggerFactory loggers, ILibrarySource libraries, IOptions<SdkAppOptions>? options = null)
{
    private readonly IOptions<SdkWorkspaceOptions> _workspace
        = Microsoft.Extensions.Options.Options.Create(options?.Value.Workspace ?? new SdkWorkspaceOptions());

    // the language is the one the project file says, as the language server takes it, over the setting's: a project imported from a .vbp is Visual Basic 6.0
    // (SupportedLanguages.OfProject). No client names one here.
    public static IRuntimeEnvironmentProfile InTheLanguageOf(ProgramWorkspace workspace, IRuntimeEnvironmentProfile profile)
        => SupportedLanguages.OfProject(workspace.Project) is { } language && language != profile.Language
            ? SDK.Runtime.RuntimeEnvironmentProfile.Of(profile) with { SourceLanguage = language }
            : profile;

    // the workspace parsed, its symbols defined, its code loaded and swept, in a session composed for profile and reaching the outside world it is given
    // (outside), or the machine's when outside is null; what was defined in it, in order, kept in definitions when it is given. The caller disposes the
    // provider: the session's current directory is the workspace, which the caller may delete.
    public async Task<LoadedProgram> LoadAsync(ProgramWorkspace workspace, IRuntimeEnvironmentProfile profile, ExternalWorld? outside, CancellationToken token,
        List<DefineSymbolsParams>? definitions = null)
    {
        var root = workspace.RootUri;
        profile = InTheLanguageOf(workspace, profile);
        var provider = new EnvironmentSessionProvider(
            profile, fileSystem, loggers.CreateLogger<EnvironmentSessionProvider>(), libraries, outside, options: options);
        try
        {
            provider.Compose(workspace.Project, root);
            return await LoadAsync(provider, workspace, root, token, definitions);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    private async Task<LoadedProgram> LoadAsync(EnvironmentSessionProvider provider, ProgramWorkspace workspace, Uri root, CancellationToken token,
        List<DefineSymbolsParams>? definitions)
    {
        var problems = new List<string>();
        var documents = new List<RDCoreDocument>();
        var stopwatch = Stopwatch.StartNew();
        var own = Parsed(workspace, root, problems, string.Empty, documents);
        var parseTime = stopwatch.Elapsed;

        // as the language server does: the names of every module bound among all of them and the libraries the project references, then each module's symbols
        // defined in the host, then the code of each.
        // they are bound in the environment the session was composed for: the profile's language and pointer width, which need not be the runner's.
        stopwatch.Restart();
        var composed = provider.Session.Environment;
        var referenced = LibrarySymbolProvider.Load(libraries, workspace.Project, composed.Language);
        var scope = composed.Language.ImplicitDeclarationScope;

        // each project the project references is bound as its own analysis binds it: its modules, the libraries it references, and its own name. It gives the
        // project its names at the precedence of the reference, after the libraries'.
        var projects = workspace.ReferencedProjects.Select(each =>
        {
            var projectRoot = each.Workspace.RootUri;
            var modules = Parsed(each.Workspace, projectRoot, problems, $"{each.Name}.", documents: null);
            var ownLibraries = LibrarySymbolProvider.Load(libraries, each.Workspace.Project, composed.Language);
            var projectResolver = WorkspaceSymbolResolver.Compose(
                projectRoot, modules.Select(module => (module.Uri, module.Kind, module.Parse)), new IntrinsicSymbolResolver(composed.Is64Bit), each.Name,
                scope, [.. new LibrarySymbolProvider(projectRoot, ownLibraries, composed.Is64Bit).ProvideSymbols()], HostsOf(each.Workspace),
                ParsedModule.DirectivesOf(modules));
            return new ReferencedProjectModules(each.Name, referenced.Priorities.GetValueOrDefault(each.Name), projectRoot, modules, projectResolver);
        }).ToArray();

        IReadOnlyList<Symbol> librarySymbols =
        [
            .. new LibrarySymbolProvider(root, referenced, composed.Is64Bit).ProvideSymbols(),
            .. projects.SelectMany(project => project.NamesFor(root, scope)),
        ];

        var resolver = WorkspaceSymbolResolver.Compose(
            root, own.Select(module => (module.Uri, module.Kind, module.Parse)), new IntrinsicSymbolResolver(composed.Is64Bit),
            workspace.Project.Name, scope, librarySymbols, HostsOf(workspace), ParsedModule.DirectivesOf(own));

        var define = new DefineSymbolsHandler(
            provider, new ProjectAnalysisSessions(composed, fileSystem, loggers.CreateLogger<ProjectAnalysisSessions>(), libraries, options),
            errorDetails,
            loggers.CreateLogger<DefineSymbolsHandler>());
        var symbolsTime = TimeSpan.Zero;
        var loaded = new List<ParsedModule>();
        foreach (var definition in SessionDefinitions.Of(root, own, resolver, scope, replace: true, projects))
        {
            if (definition.ToRequest() is not { } request)
            {
                continue;
            }

            if (definition.Phase is SessionDefinitionPhase.Code && symbolsTime == TimeSpan.Zero)
            {
                symbolsTime = stopwatch.Elapsed;
                stopwatch.Restart();
            }

            definitions?.Add(request);
            var defined = await define.Handle(request, token);
            if (definition.Phase is not SessionDefinitionPhase.Code)
            {
                continue;
            }

            // what keeps a module of a referenced project from loading is named as of its project: it refuses the run as one of the project's own does.
            var name = definition.Referenced is { } project ? $"{project.Name}.{definition.Module.Name}" : definition.Module.Name;
            if (defined.CodeErrors.Count > 0)
            {
                problems.Add($"{name}: {string.Join("; ", defined.CodeErrors)}");
            }
            else if (definition.Referenced is null)
            {
                loaded.Add(definition.Module);
            }
        }

        var loadTime = stopwatch.Elapsed;

        // the sweep of each module started when it was loaded, and runs in the background: it is over before anything is timed that it could slow down.
        await provider.Image.Semantics.WhenAllEvaluatedAsync(token);
        var sweepTime = stopwatch.Elapsed;

        return new LoadedProgram(provider, loaded, parseTime, symbolsTime, loadTime, sweepTime, problems, referenced.Libraries, documents);
    }

    // the modules of a workspace that parse, addressed under root; what keeps one from parsing is a problem, named with the prefix (its project's, for a
    // referenced one). A module with a syntax error is defined and loaded as the language server does it: its load refuses it, unless the error is in a
    // branch of a #If that is excluded, which only the host knows. The parser reads text alone: a form's designer is set on its parse from its text and its
    // companion file, as the language server sets it. A file of several modules is parsed once, and each of its modules is a part with its own part of the
    // parse (RDCoreDocument.ParseOf): the file's own module first, then each block's, named as its header writes it. The parts of one name are one module
    // (ModuleAssembly); a module its parts make invalid is a problem, and none of its parts is defined. Each file that parses is a document, in documents
    // when it is given.
    private IReadOnlyList<ParsedModule> Parsed(ProgramWorkspace workspace, Uri root, List<string> problems, string prefix, List<RDCoreDocument>? documents)
    {
        var parts = new List<ModulePart>();
        foreach (var module in workspace.Modules)
        {
            var file = workspace.DocumentOf(module);
            var parse = new ModuleParser(workspaceOptions: _workspace).Parse(file, module.Source) with
            {
                Designer = FormDesigner.Read(module.RelativePath, module.Source, module.Companion, out var designerProblem),
            };
            if ((module.CompanionProblem ?? designerProblem) is { } problem)
            {
                loggers.CreateLogger<WorkspaceProgramLoader>().LogWarning(problem, "⚠️ The designer of {module} cannot be read.", module.RelativePath);
            }

            if (parse.SyntaxTree is null)
            {
                problems.Add($"{prefix}{module.Name}: {string.Join("; ", parse.SyntaxErrors.Select(error => error.Description))}");
                continue;
            }

            documents?.Add(new RDCoreDocument(file, parse));
            parts.AddRange(ModulePart.PartsOf(file, module.Name, module.Type, parse));
        }

        var (modules, refused) = ParsedModule.Assemble(root, parts);
        foreach (var invalid in refused)
        {
            problems.Add($"{prefix}{invalid.Name}: {string.Join("; ", invalid.Errors.Select(error => error.Description))}");
        }

        return modules;
    }

    // what host document each document module is the code of, as the language server has it: its code names the members of its host's class, and they are
    // not taken for variables it declares by naming them.
    private static Dictionary<string, DocClassType> HostsOf(ProgramWorkspace workspace)
    {
        var hosts = new Dictionary<string, DocClassType>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in workspace.Modules)
        {
            var listed = workspace.Project.Modules
                .FirstOrDefault(entry => string.Equals(entry.RelativeUri, module.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (listed?.Super is { } super)
            {
                hosts.TryAdd(module.Name, super);
            }
        }

        return hosts;
    }
}
