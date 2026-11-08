using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Repl;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Platform;
using System.IO.Abstractions;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §6.1.2.5.1.1, §6.1.2.5.2.1</strong>: the current directory of a session the environment host composes for a workspace is the workspace root,
/// where a relative path finds the workspace's files, and <c>ChDir</c> moves it. The session's directory is the process's, so these tests run alone, in a workspace
/// on disk, and put the test host's directory back.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("MS-VBAL 6.1.2.5 FileSystem")]
public sealed class WorkspaceCurrentDirectoryTests
{
    private const string ModuleName = "Program";

    private string _saved = string.Empty;
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _saved = Directory.GetCurrentDirectory();
        _root = WorkspacePaths.Normalized(Path.Combine(Path.GetTempPath(), "rdcore-curdir-ws-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(_root, "Sub"));
        File.WriteAllText(Path.Combine(_root, "data.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "Sub", "inner.txt"), "y");
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.SetCurrentDirectory(_saved);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a temporary folder.
        }
    }

    [TestMethod]
    public async Task AWorkspaceSession_StartsInTheWorkspaceRoot_WhereARelativePathFindsItsFiles()
    {
        var provider = Compose(_root);

        var printed = await RunAsync(provider, _root, "Debug.Print CurDir", "Debug.Print Dir(\"data.txt\")", "Debug.Print Dir(\"Sub\\inner.txt\")");

        CollectionAssert.AreEqual(new[] { _root, "data.txt", "inner.txt" }, printed);
    }

    [TestMethod]
    public async Task ChDir_MovesTheDirectory_OfAWorkspaceSession()
    {
        var provider = Compose(_root);

        var printed = await RunAsync(provider, _root, "ChDir \"Sub\"", "Debug.Print CurDir", "Debug.Print Dir(\"inner.txt\")", "Debug.Print \"[\" & Dir(\"data.txt\") & \"]\"");

        CollectionAssert.AreEqual(new[] { Path.Combine(_root, "Sub"), "inner.txt", "[]" }, printed);
    }

    // a reload composes the session anew, as at start-up: what a program did to it, its directory included, is gone. End leaves the directory where it is.
    [TestMethod]
    public async Task AReloadedSession_StartsInTheWorkspaceRoot_AndEndLeavesTheDirectoryWhereItIs()
    {
        var provider = Compose(_root);
        var ended = await RunAsync(provider, _root, "ChDir \"Sub\"", "End");
        Assert.AreEqual(Path.Combine(_root, "Sub"), CurrentDirectoryOf(provider), $"End leaves the directory where the program put it: {string.Join("|", ended)}");

        provider.Compose(Project(_root).ProjectInfo, Wire.FileUri(_root));

        Assert.AreEqual(_root, CurrentDirectoryOf(provider));
    }

    // the shell's scratch workspace is no folder of the user's: a session with no workspace of the user's starts where the host was started.
    [TestMethod]
    public async Task TheShellsScratchWorkspace_LeavesTheDirectoryWhereItWas()
    {
        var fileSystem = new FileSystem();
        using var scratch = await ReplWorkspace.CreateAsync(fileSystem, new ProjectFileWriter(fileSystem));
        Directory.SetCurrentDirectory(_root);

        var provider = new EnvironmentSessionProvider(Profile(), fileSystem, NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(new RDCoreProject { Name = RDCoreProject.DefaultName }, Wire.FileUri(scratch.Root));

        Assert.IsTrue(ReplWorkspace.IsScratch(fileSystem, scratch.Root));
        Assert.AreEqual(_root, CurrentDirectoryOf(provider));
        Directory.SetCurrentDirectory(_saved);
    }

    private static RuntimeEnvironmentProfile Profile()
        => new(Is64Bit: true, 0, 1252, false, ExecutionEngine: RDCore.SDK.Server.Configuration.SdkEnvironmentOptions.DefaultExecutionEngine);

    private static ProjectFile Project(string root)
        => new(root, new RDCoreProject { Name = "Project1", Modules = [new RDCoreModule { RelativeUri = $"{ModuleName}.bas" }] });

    private static EnvironmentSessionProvider Compose(string root)
    {
        var provider = new EnvironmentSessionProvider(Profile(), new FileSystem(), NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(Project(root).ProjectInfo, Wire.FileUri(root));
        return provider;
    }

    private static string CurrentDirectoryOf(EnvironmentSessionProvider provider)
    {
        Assert.IsNull(provider.Session.Files.TryGetCurrentDirectory(null, out var path));
        return path;
    }

    // Program.Main of the statements given, defined in the session and run, as the language server has the host do it.
    private static async Task<string[]> RunAsync(EnvironmentSessionProvider provider, string root, params string[] statements)
    {
        var source = $"Attribute VB_Name = \"{ModuleName}\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", statements)}\r\nEnd Sub\r\n";
        var workspaceRoot = Wire.FileUri(root);
        var moduleUri = new UriBuilder(workspaceRoot) { Fragment = ModuleName }.Uri;
        var parse = new ModuleParser().Parse(Wire.FileUri(Path.Combine(root, $"{ModuleName}.bas")), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var resolver = WorkspaceSymbolResolver.Compose(workspaceRoot, [(moduleUri, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
        var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, ModuleType.StdModule, parse, resolver, withImplicitDeclarations: true).ProvideSymbols();
        await new DefineSymbolsHandler(provider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance)
            .Handle(Wire.RoundTrip(new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = ModuleName,
                Symbols = SymbolDescriptorProjector.Project(symbols, moduleUri),
                Directives = parse.SyntaxTree.GetModuleDirectives(),
                PrecompilerTriviaJson = DefineSymbolsParams.PrecompilerTriviaJsonOf(parse),
                Replace = true,
            }), CancellationToken.None);

        var result = await new HostExecuteHandler(provider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<HostExecuteHandler>.Instance)
            .Handle(new HostExecuteParams
            {
                Json = PlatformJson.Serialize(new HostExecutePayload(moduleUri, parse)),
                ModuleName = ModuleName,
                EntryPoint = "Main",
            }, CancellationToken.None);

        Assert.IsTrue(result.Outcome is ExecutionOutcome.Completed or ExecutionOutcome.Halted, $"{result.Outcome}: {result.ErrorMessage}");
        return [.. result.Output.Select(line => line.Trim())];
    }
}
