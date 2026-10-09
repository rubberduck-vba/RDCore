using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Repl;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The environment host as the interactive shell uses it: the language of the shell, a program that is defined and run in the same session every time, and
/// lines typed at the prompt that are run in the module the program is in.
/// </summary>
/// <remarks>
/// Everything the platform does for a <c>RUN</c> except the two JSON-RPC hops: the language server's own symbol projection, then the host defining those
/// symbols, lowering the module and running a procedure of it.
/// </remarks>
internal sealed class ShellHost
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-shell-ws");
    private static readonly string ModuleName = ReplProgram.ModuleName;

    private readonly EnvironmentSessionProvider _provider;
    private readonly Uri _workspaceRoot;
    private readonly Uri _moduleUri;

    private ShellHost(EnvironmentSessionProvider provider, Uri workspaceRoot)
    {
        _provider = provider;
        _workspaceRoot = workspaceRoot;
        _moduleUri = new UriBuilder(workspaceRoot) { Fragment = ModuleName }.Uri;
    }

    /// <summary>
    /// A host with a session composed for a project of the one module the shell's program is.
    /// </summary>
    public static ShellHost Compose()
    {
        var project = new ProjectFile(Root, new RDCoreProject { Name = ModuleName, Modules = [new RDCoreModule { RelativeUri = $"{ModuleName}.bas" }] });
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)) });
        var provider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, SourceLanguage: SupportedLanguages.BASIC), fs, NullLogger<EnvironmentSessionProvider>.Instance);
        var workspaceRoot = new Uri(Root);
        provider.Compose(project.ProjectInfo, workspaceRoot);

        return new ShellHost(provider, workspaceRoot);
    }

    /// <summary>The session's memory, as the allocator accounts for it.</summary>
    public SessionMemoryInfo Memory => _provider.Session.Memory.Info;

    /// <summary>The provider of the session, for whoever looks at the program that runs in it.</summary>
    public EnvironmentSessionProvider Provider => _provider;

    /// <summary>
    /// Whether the module declares a member of that name, for a name a line of the program assigned.
    /// </summary>
    public bool Declares(string name)
        => _provider.Session.Symbols.TryResolveValue(ModuleName, GlobalSymbols.UnresolvedSymbol, out var module)
            && module is not null
            && _provider.Session.Symbols.MembersOf(module.Uri).Any(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Runs the program, the way <c>RUN</c> does: defined in the session, then <c>Main</c> invoked.
    /// </summary>
    public Task<ExecuteSessionResult> RunAsync(IEnumerable<(int Number, string Statement)> lines, bool debug = false, bool streamOutput = false)
        => ExecuteAsync(Program(lines).ToModuleSource(), ReplProgram.EntryPointName, debug, streamOutput: streamOutput);

    /// <summary>
    /// Runs a line typed at the prompt, the way the shell does: the program and the line, as a second procedure of the module.
    /// </summary>
    public Task<ExecuteSessionResult> ImmediateAsync(IEnumerable<(int Number, string Statement)> lines, string statement, bool alongside = false)
        => ExecuteAsync(Program(lines).ToImmediateModuleSource(statement), ReplProgram.ImmediateEntryPointName, debug: false, immediate: alongside);

    /// <summary>
    /// Takes the program's module out of the session (<c>rdcore/host/discard</c>).
    /// </summary>
    public async Task<DiscardSessionResult> DiscardAsync(string? moduleName = null)
        => await new HostDiscardHandler(_provider, NullLogger<HostDiscardHandler>.Instance)
            .Handle(new HostDiscardParams
            {
                ModuleName = moduleName ?? ModuleName,
                ModuleUri = new UriBuilder(_workspaceRoot) { Fragment = moduleName ?? ModuleName }.Uri,
            }, CancellationToken.None);

    private static ReplProgram Program(IEnumerable<(int Number, string Statement)> lines)
    {
        var program = new ReplProgram();
        foreach (var (number, statement) in lines)
        {
            program.Store(number, statement);
        }

        return program;
    }

    private async Task<ExecuteSessionResult> ExecuteAsync(string source, string entryPoint, bool debug = false, bool immediate = false, bool streamOutput = false)
    {
        var parse = new ModuleParser().Parse(new Uri(Path.Combine(Root, $"{ModuleName}.bas")), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var language = SupportedLanguages.BASIC.ImplicitDeclarationScope;
        var resolver = WorkspaceSymbolResolver.Compose(
            _workspaceRoot, [(_moduleUri, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver(), implicitScope: language);
        var symbols = new SyntaxTreeSymbolProvider(
            _workspaceRoot, _moduleUri, ModuleType.StdModule, parse, resolver, withImplicitDeclarations: true, language).ProvideSymbols();

        await new DefineSymbolsHandler(_provider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance)
            .Handle(new DefineSymbolsParams
            {
                WorkspaceRoot = _workspaceRoot,
                ModuleUri = _moduleUri,
                ModuleName = ModuleName,
                Symbols = SymbolDescriptorProjector.Project(symbols, _moduleUri),
                Directives = parse.SyntaxTree.GetModuleDirectives(),
                Replace = true,
            }, CancellationToken.None);

        return await new HostExecuteHandler(_provider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<HostExecuteHandler>.Instance)
            .Handle(new HostExecuteParams
            {
                Json = PlatformJson.Serialize(new HostExecutePayload(_moduleUri, parse)),
                ModuleName = ModuleName,
                EntryPoint = entryPoint,
                Debug = debug,
                Immediate = immediate,
                StreamOutput = streamOutput,
            }, CancellationToken.None);
    }
}
