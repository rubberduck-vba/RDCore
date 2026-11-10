using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Repl;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Platform;
using System.IO.Abstractions;
using RDCore.SDK.Platform.Components;

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
        // under the temporary folder's real path: on macOS /var is a link to /private/var, and CurDir gives the real path of the directory a
        // session is in.
        var temporary = RDCore.SDK.Platform.PlatformRootConfinement.RealPath(Path.GetTempPath());
        _root = WorkspacePaths.Normalized(Path.Combine(temporary, "rdcore-curdir-ws-" + Guid.NewGuid().ToString("N")));
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
        using var provider = Compose(_root);

        // a relative path is written with the separator of the platform the session runs on.
        var inner = Path.Combine("Sub", "inner.txt");
        var printed = await RunAsync(provider, _root, "Debug.Print CurDir", "Debug.Print Dir(\"data.txt\")", $"Debug.Print Dir(\"{inner}\")");

        CollectionAssert.AreEqual(new[] { _root, "data.txt", "inner.txt" }, printed, string.Join("|", printed));
    }

    // a relative path's \ is a separator on every platform: VBA written with Windows paths finds and makes the workspace's files on Linux too.
    [TestMethod]
    public async Task ARelativePathWithABackslash_IsInAFolderOfTheWorkspace_OnEveryPlatform()
    {
        using var provider = Compose(_root);

        var printed = await RunAsync(provider, _root, "Debug.Print Dir(\"Sub\\inner.txt\")", "Open \"Sub\\out.txt\" For Output As #1", "Close #1", "Debug.Print Dir(\"Sub\\out.txt\")");

        CollectionAssert.AreEqual(new[] { "inner.txt", "out.txt" }, printed, string.Join("|", printed));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "Sub", "out.txt")));
    }

    // Linux lets a program delete its own current directory, and every read of it then fails: CurDir, and a relative path in any file function, are error 76,
    // and a full path still works. Windows refuses to delete it (75), and the directory is there for all of them.
    [TestMethod]
    public async Task ADeletedCurrentDirectory_IsPathNotFound_ToWhatReadsIt()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Gone"));
        using var provider = Compose(_root);

        var printed = await RunAsync(provider, _root,
            "On Error Resume Next",
            "Dim s As String",
            "ChDir \"Gone\"",
            "RmDir CurDir",
            "Debug.Print \"RmDir \" & Err.Number",
            "Err.Clear: s = CurDir: Debug.Print \"CurDir \" & Err.Number",
            "Err.Clear: s = Dir(\"*.txt\"): Debug.Print \"Dir \" & Err.Number",
            "Err.Clear: Open \"x.txt\" For Output As #1: Debug.Print \"Open \" & Err.Number",
            "Close #1",
            "Err.Clear: s = FileLen(\"x.txt\"): Debug.Print \"FileLen \" & Err.Number",
            "Err.Clear: MkDir \"new\": Debug.Print \"MkDir \" & Err.Number",
            "Err.Clear: ChDir \"..\": Debug.Print \"ChDir \" & Err.Number",
            $"Err.Clear: ChDir \"{_root}\": Debug.Print \"ChDir root \" & Err.Number",
            "Debug.Print CurDir",
            "Debug.Print Dir(\"data.txt\")");

        string[] deleted = ["RmDir 0", "CurDir 76", "Dir 76", "Open 76", "FileLen 76", "MkDir 76", "ChDir 76", "ChDir root 0", _root, "data.txt"];
        string[] refusedOnWindows = ["RmDir 75", "CurDir 0", "Dir 0", "Open 0", "FileLen 0", "MkDir 0", "ChDir 0", "ChDir root 0", _root, "data.txt"];
        CollectionAssert.AreEqual(OperatingSystem.IsWindows() ? refusedOnWindows : deleted, printed, string.Join("|", printed));
    }

    // an LSP client's file: URI may spell the drive in lower case, as OmniSharp's does: CurDir gives the drive as a drive is written, in upper case.
    [TestMethod]
    public void AWorkspaceRootWithALowerCaseDrive_IsTheCurrentDirectory_WithAnUpperCaseDrive()
    {
        var uri = Wire.FileUri(_root);
        var lowered = OperatingSystem.IsWindows() ? new Uri(uri.AbsoluteUri.Replace($"file:///{_root[0]}:", $"file:///{char.ToLowerInvariant(_root[0])}:")) : uri;
        using var provider = new EnvironmentSessionProvider(Profile(), new FileSystem(), NullLogger<EnvironmentSessionProvider>.Instance);

        provider.Compose(Project(_root).ProjectInfo, lowered);

        Assert.AreEqual(_root, CurrentDirectoryOf(provider), ignoreCase: false);
    }

    [TestMethod]
    public async Task ChDir_MovesTheDirectory_OfAWorkspaceSession()
    {
        using var provider = Compose(_root);

        var printed = await RunAsync(provider, _root, "ChDir \"Sub\"", "Debug.Print CurDir", "Debug.Print Dir(\"inner.txt\")", "Debug.Print \"[\" & Dir(\"data.txt\") & \"]\"");

        CollectionAssert.AreEqual(new[] { Path.Combine(_root, "Sub"), "inner.txt", "[]" }, printed);
    }

    // a reload composes the session anew, as at start-up: what a program did to it, its directory included, is gone. End leaves the directory where it is.
    [TestMethod]
    public async Task AReloadedSession_StartsInTheWorkspaceRoot_AndEndLeavesTheDirectoryWhereItIs()
    {
        using var provider = Compose(_root);
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

        using var provider = new EnvironmentSessionProvider(Profile(), fileSystem, NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(new RDCoreProject { Name = RDCoreProject.DefaultName }, Wire.FileUri(scratch.Root));

        Assert.IsTrue(ScratchWorkspace.Is(fileSystem, scratch.Root));
        Assert.AreEqual(_root, CurrentDirectoryOf(provider));
    }

    // the directory is the process's: a session that ends gives back the one it found, wherever a program moved it, and its workspace folder can be deleted.
    [TestMethod]
    public async Task ASessionThatIsDisposed_GivesTheProcessBackTheDirectoryItFound()
    {
        Directory.SetCurrentDirectory(_root);
        var workspace = NewWorkspace("Workspace");
        var provider = Compose(workspace);
        await RunAsync(provider, workspace, "ChDir \"Sub\"");

        provider.Dispose();

        Assert.AreEqual(_root, Directory.GetCurrentDirectory());
        Directory.Delete(workspace, recursive: true);
    }

    // a reload starts where the host did: the session it replaces gives the directory back first, and a root that is not there leaves it there.
    [TestMethod]
    public async Task ASessionThatIsReplaced_GivesTheProcessBackTheDirectoryItFound()
    {
        Directory.SetCurrentDirectory(_root);
        var workspace = NewWorkspace("Workspace");
        using var provider = Compose(workspace);
        await RunAsync(provider, workspace, "ChDir \"Sub\"");

        var missing = Path.Combine(_root, "Missing");
        provider.Compose(Project(missing).ProjectInfo, Wire.FileUri(missing));

        Assert.AreEqual(_root, CurrentDirectoryOf(provider));
        Directory.Delete(workspace, recursive: true);
    }

    // Linux lets a process's current directory be deleted (Windows refuses), and every later read of it then fails: a session composed after that does not fail.
    [TestMethod]
    public void AWorkspaceDeletedWhileItIsTheCurrentDirectory_FailsNoLaterCompose()
    {
        var workspace = NewWorkspace("Gone");
        using var gone = Compose(workspace);
        try
        {
            Directory.Delete(workspace, recursive: true);
        }
        catch (IOException)
        {
            // Windows: the process is in it.
        }

        using var provider = new EnvironmentSessionProvider(Profile(), new FileSystem(), NullLogger<EnvironmentSessionProvider>.Instance);
        // a root as unit tests spell one: no full path on Linux.
        provider.Compose(Project(_root).ProjectInfo, new Uri("file:///C:/rdcore-no-such-workspace/"));
        provider.Compose(Project(_root).ProjectInfo, Wire.FileUri(_root));

        Assert.AreEqual(_root, CurrentDirectoryOf(provider));
    }

    // a root that is no full path on this platform names no folder here: it is not resolved against the current directory, where it could name one.
    [TestMethod]
    public void ARootThatIsNoFullPath_LeavesTheDirectoryWhereItWas()
    {
        var start = Path.Combine(_root, "Sub");
        Directory.SetCurrentDirectory(start);
        // the workspace root without its drive on Windows; on Linux a path with a drive, whose local path (C:\ws\) is one relative name, backslashes and all.
        var root = OperatingSystem.IsWindows() ? new Uri("file://" + _root[2..].Replace('\\', '/')) : new Uri("file:///C:/ws/");
        var resolved = Path.GetFullPath(root.LocalPath);
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(resolved);
        }

        Assert.IsFalse(Path.IsPathFullyQualified(root.LocalPath), root.LocalPath);
        Assert.IsTrue(Directory.Exists(resolved), $"{root.LocalPath} names {resolved}, a folder under the current directory");

        using var provider = new EnvironmentSessionProvider(Profile(), new FileSystem(), NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(Project(_root).ProjectInfo, root);

        Assert.AreEqual(start, CurrentDirectoryOf(provider));
    }

    // a workspace folder with a Sub folder in it, under the test's own folder, which the cleanup deletes.
    private string NewWorkspace(string name)
    {
        var workspace = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(workspace, "Sub"));
        return workspace;
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
        await new DefineSymbolsHandler(provider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance)
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

        var result = await new HostExecuteHandler(provider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<HostExecuteHandler>.Instance)
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
