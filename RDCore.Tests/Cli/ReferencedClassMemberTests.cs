using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Symbols;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Platform;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.6.12</strong>, RD-VBAL §2.2.3: a member of a class that a referenced project exposes is bound from the referencing project, through a
/// variable of the class or of an interface the project exposes, and through the class's default instance; a name the class does not declare is still
/// none of its members. A class of the project with the name of the referenced project's keeps its own members.
/// </summary>
[TestClass]
public sealed class ReferencedClassMemberTests
{
    private const string LibraryName = "NavalLib";
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "rdcore-referenced-members");
    private static readonly string AppFolder = Path.Combine(Base, "App");
    private static readonly Uri AppRoot = Wire.FileUri(AppFolder);
    private static readonly Uri LibraryRoot = Wire.FileUri(Path.Combine(Base, LibraryName));

    private static readonly Source[] Library =
    [
        new("Widget", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Widget",
            "Attribute VB_PredeclaredId = True", "Attribute VB_Exposed = True", "Option Explicit",
            "Public Function Twice(ByVal N As Long) As Long", "    Twice = N * 2", "End Function",
            "Public Property Get Size() As Long", "End Property")),
        new("IShape", ModuleType.ClassModule, ModuleWorkspace.ClassModule("IShape",
            "Attribute VB_Exposed = True", "Option Explicit",
            "Public Property Get Name() As String", "End Property")),
    ];

    [TestMethod]
    public async Task AMemberOfAReferencedProjectsClass_IsBound_ThroughAVariable_AnInterface_AndTheDefaultInstance()
    {
        var errors = await LoadErrorsAsync([],
            "Dim w As Widget", "Dim s As IShape", "Dim q As NavalLib.Widget",
            "Debug.Print w.Twice(2), w.Size, q.Twice(1)", "Debug.Print Widget.Twice(3), Widget.Size", "Debug.Print s.Name");

        Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
    }

    [TestMethod]
    public async Task ANameTheReferencedProjectsClassDoesNotDeclare_IsNoneOfItsMembers()
    {
        var errors = await LoadErrorsAsync([], "Dim w As Widget", "Debug.Print w.Nope");

        Assert.IsTrue(errors.Any(error => error.Contains("Nope", StringComparison.Ordinal)), string.Join("; ", errors));
    }

    // the project's Widget is defined after the referenced project's: each class is the one its members are defined under.
    [TestMethod]
    public async Task AClassOfTheProject_NamedLikeAReferencedProjectsClass_KeepsItsOwnMembers()
    {
        var own = new Source("Widget", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Widget", "Option Explicit", "Public Sub Polish()", "End Sub"));

        var errors = await LoadErrorsAsync([own], "Dim w As Widget", "w.Polish", "Dim q As NavalLib.Widget", "Debug.Print q.Twice(1)");

        Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
    }

    private sealed record class Source(string Name, ModuleType Type, string Text)
    {
        public string FileName => $"{Name}.{(Type is ModuleType.ClassModule ? "cls" : "bas")}";
    }

    private sealed record class Module(string Name, ModuleType Type, Uri Uri, ModuleParseResult Parse);

    // the referenced project's modules are defined in the session first, as the language server defines them, then the project's own, then the code of
    // the project's own: what stopped a module of the project from loading.
    private static async Task<IReadOnlyList<string>> LoadErrorsAsync(IReadOnlyList<Source> ownClasses, params string[] main)
    {
        var program = ModuleWorkspace.StandardModule("Program", ["Option Explicit", "Public Sub Main()", .. main, "End Sub"]);
        Source[] own = [.. ownClasses, new("Program", ModuleType.StdModule, program)];
        var project = new ProjectFile(AppFolder, new RDCoreProject
        {
            Name = "App",
            Modules = [.. own.Select(module => new RDCoreModule { RelativeUri = module.FileName })],
        });

        var library = Parsed(LibraryRoot, Library);
        var libraryResolver = WorkspaceSymbolResolver.Compose(
            LibraryRoot, library.Select(module => (module.Uri, module.Type, module.Parse)), new IntrinsicSymbolResolver(), LibraryName);
        var librarySymbols = library.SelectMany(module => Symbols(LibraryRoot, module, libraryResolver)).ToArray();

        var app = Parsed(AppRoot, own);
        var appResolver = WorkspaceSymbolResolver.Compose(
            AppRoot, app.Select(module => (module.Uri, module.Type, module.Parse)), new IntrinsicSymbolResolver(), "App",
            libraries: ReferencedProjectSymbols.Of(AppRoot, LibraryName, librarySymbols, 1));

        // the session reads the project's modules from its folder, as the host does.
        var files = own.ToDictionary(module => Path.Combine(AppFolder, module.FileName), module => new MockFileData(module.Text));
        var profile = new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, ExecutionEngine: SdkEnvironmentOptions.DefaultExecutionEngine);
        using var provider = new EnvironmentSessionProvider(profile, new MockFileSystem(files), NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(project.ProjectInfo, AppRoot);

        foreach (var declareTypesOnly in new[] { true, false })
        {
            foreach (var module in library)
            {
                await DefineAsync(provider, Request(module, Symbols(LibraryRoot, module, libraryResolver)) with
                {
                    DeclareTypesOnly = declareTypesOnly,
                    Library = LibraryName,
                    LibraryPriority = 1,
                    PrecompilerTriviaJson = string.Empty,
                });
            }

            foreach (var module in app)
            {
                await DefineAsync(provider, Request(module, Symbols(AppRoot, module, appResolver)) with { DeclareTypesOnly = declareTypesOnly });
            }
        }

        var errors = new List<string>();
        foreach (var module in app)
        {
            var code = Request(module, []) with { ParseResultJson = PlatformJson.Serialize(module.Parse), CodeOnly = true };
            errors.AddRange((await DefineAsync(provider, code)).CodeErrors);
        }

        return errors;
    }

    private static Module[] Parsed(Uri root, IEnumerable<Source> modules) => [.. modules.Select(module =>
    {
        var parse = new ModuleParser().Parse(new Uri(root, module.FileName), module.Text);
        Assert.IsTrue(parse.IsSuccess, $"{module.Name}: {string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose))}");
        return new Module(module.Name, module.Type, new UriBuilder(root) { Fragment = module.Name }.Uri, parse);
    })];

    private static IEnumerable<Symbol> Symbols(Uri root, Module module, ISymbolResolver resolver)
        => new SyntaxTreeSymbolProvider(root, module.Uri, module.Type, module.Parse, resolver, withImplicitDeclarations: true).ProvideSymbols();

    // a module of either project is defined in the session of the project, under the session's root.
    private static DefineSymbolsParams Request(Module module, IEnumerable<Symbol> symbols) => new()
    {
        WorkspaceRoot = AppRoot,
        ModuleUri = module.Uri,
        ModuleName = module.Name,
        ModuleType = module.Type,
        Symbols = SymbolDescriptorProjector.Project([.. symbols], module.Uri),
        Directives = ModuleKinds.DirectivesOf(module.Type, module.Parse.SyntaxTree!.GetModuleDirectives()),
        ImplementedInterfaceNames = module.Parse.SyntaxTree!.GetImplementedInterfaceNames(),
        ImplementedInterfaceRanges = module.Parse.SyntaxTree!.GetImplementedInterfaceRanges(),
        PrecompilerTriviaJson = DefineSymbolsParams.PrecompilerTriviaJsonOf(module.Parse),
        Replace = true,
    };

    private static Task<DefineSymbolsResult> DefineAsync(EnvironmentSessionProvider provider, DefineSymbolsParams request)
        => new DefineSymbolsHandler(
                provider, Substitute.For<IProjectAnalysisSessions>(), Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance)
            .Handle(Wire.RoundTrip(request), CancellationToken.None);
}
