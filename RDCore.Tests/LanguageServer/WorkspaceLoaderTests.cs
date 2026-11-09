using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.LanguageServer.Server;
using RDCore.LanguageServer.Services.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.SDK.Server.Services.States;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class WorkspaceLoaderTests
{
    // an absolute path the MockFileSystem accepts on both Windows and the Linux CI runner.
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-ws");
    private static readonly string ProjectFilePath = Path.Combine(Root, ProjectFile.FileName);

    private static MockFileSystem FileSystemWith(params (string RelativePath, string Content)[] files)
    {
        var project = new ProjectFile(Root, new RDCoreProject
        {
            Modules = [.. files.Select(f => new RDCoreModule { RelativeUri = f.RelativePath })],
        });

        var entries = new Dictionary<string, MockFileData>
        {
            [ProjectFilePath] = new MockFileData(JsonSerializer.Serialize(project)),
        };
        foreach (var (relativePath, content) in files)
        {
            entries[Path.Combine(Root, relativePath)] = new MockFileData(content);
        }
        return new MockFileSystem(entries);
    }

    private static ProjectFileService ProjectService(MockFileSystem fs)
        => new(NullLogger<ProjectFileService>.Instance, fs.Path, fs.File, new ProjectFileLoader(fs), () => SupportedLanguages.RDVBA);

    private static WorkspaceService Service(MockFileSystem fs, IServerStateProvider state, WorkspaceDocumentService documents, DocumentStateProvider documentStates,
        Microsoft.Extensions.Logging.ILogger<WorkspaceService>? logger = null)
    {
        var projectFiles = ProjectService(fs);
        return new WorkspaceService(
            state, logger ?? NullLogger<WorkspaceService>.Instance,
            fs.Path, fs.File, fs.Directory, projectFiles, documents, documentStates,
            () => ProtocolSupportedLanguage.VBA, new WorkspaceProjects(projectFiles), new ProjectFileLoader(fs));
    }

    private static IServerStateProvider InitializingState()
    {
        var state = Substitute.For<IServerStateProvider>();
        state.State.Returns(ServerState.Initializing);
        return state;
    }

    private static WorkspaceService WorkspaceService(MockFileSystem fs, IServerStateProvider? state = null, Microsoft.Extensions.Logging.ILogger<WorkspaceService>? logger = null)
    {
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(
            documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);

        return Service(fs, state ?? InitializingState(), documents, documentStates, logger);
    }

    [TestMethod]
    public async Task ProjectFileService_LoadAsync_Success_SetsProjectAndReturns()
    {
        var fs = FileSystemWith(("src/Mod1.bas", "Attribute VB_Name = \"Mod1\""));
        var sut = ProjectService(fs);

        await sut.LoadAsync(Root);

        Assert.AreEqual(Root, sut.Project.Uri);
        Assert.ContainsSingle(sut.Project.ProjectInfo.Modules);
        Assert.AreEqual("src/Mod1.bas", sut.Project.ProjectInfo.Modules[0].RelativeUri);
    }

    // a project file that is not JSON says so, and where: it used to surface later as a didOpen error about an empty path (HexIDE's findings desk #26).
    [TestMethod]
    public async Task ProjectFileService_LoadAsync_ProjectFileThatIsNotJson_SaysWhereItIsNot()
    {
        var sut = ProjectService(new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectFilePath] = new MockFileData("{\r\n  \"Version\": \"0.1.0\",\r\n  \"ProjectInfo\": { \"Name\": \"Check\" ]\r\n}"),
        }));

        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => sut.LoadAsync(Root));

        StringAssert.Contains(error.Message, ProjectFilePath);
        StringAssert.Contains(error.Message, "3");
        Assert.IsInstanceOfType<JsonException>(error.InnerException);
    }

    [TestMethod]
    public async Task ProjectFileService_LoadAsync_NoProjectFile_IsTheProjectTheFolderHolds_AndWritesNothing()
    {
        var fs = new MockFileSystem();
        fs.AddFile(Path.Combine(Root, "Module1.bas"), new MockFileData("Attribute VB_Name = \"Module1\"\r\n"));
        var sut = ProjectService(fs);

        await sut.LoadAsync(Root);

        Assert.IsTrue(sut.Project.IsSynthesized);
        Assert.AreEqual("Module1.bas", sut.Project.ProjectInfo.Modules.Single().RelativeUri);
        Assert.IsFalse(fs.FileExists(ProjectFilePath));
    }

    [TestMethod]
    [DataRow("{ \"ProjectInfo\": { \"Name\": \"ByHand\", \"Modules\": [ \"Module1.bas\" ] } }", DisplayName = "no Version")]
    [DataRow("{ \"Version\": \"\", \"ProjectInfo\": { \"Name\": \"ByHand\", \"Modules\": [ \"Module1.bas\" ] } }", DisplayName = "an empty Version")]
    public async Task WorkspaceService_LoadAsync_AProjectFileNoPlatformWrote_Loads(string json)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectFilePath] = new MockFileData(json),
            [Path.Combine(Root, "Module1.bas")] = new MockFileData("Attribute VB_Name = \"Module1\"\r\n"),
        });
        var sut = WorkspaceService(fs);

        await sut.LoadAsync(Root);
    }

    [TestMethod]
    public async Task WorkspaceService_LoadAsync_LoadsModuleContent_AndMarksItLoaded()
    {
        var fs = FileSystemWith(("src/Mod1.bas", "Public Sub Foo()\r\nEnd Sub"));
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(
            documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);

        var sut = Service(fs, InitializingState(), documents, documentStates);

        await sut.LoadAsync(Root);

        var loaded = documents.GetAllDocuments().ToArray();
        Assert.ContainsSingle(loaded);
        Assert.AreEqual("Public Sub Foo()\r\nEnd Sub", loaded[0].Text);
        Assert.IsInstanceOfType<LoadedDocumentState>(documentStates.GetCurrentState(loaded[0].Id));
    }

    // a resource file or a README the project lists is a document of the workspace, and none of its code: it is never parsed (HexIDE, desk 2026-10-27).
    [TestMethod]
    public async Task WorkspaceService_LoadAsync_LoadsTheOtherFiles_AndGivesThemToNoneThatReadsCode()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectFilePath] = new MockFileData(JsonSerializer.Serialize(new ProjectFile(Root, new RDCoreProject
            {
                Modules = [new RDCoreModule { RelativeUri = "src/Mod1.bas" }],
                OtherFiles = [new RDCoreFile { RelativeUri = "res/App.res" }, new RDCoreFile { RelativeUri = "README.md" }],
            }))),
            [Path.Combine(Root, "src", "Mod1.bas")] = new MockFileData("Public Sub Foo()\r\nEnd Sub"),
            [Path.Combine(Root, "res", "App.res")] = new MockFileData([0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0xFF, 0xFF]),
            [Path.Combine(Root, "README.md")] = new MockFileData("# Battleship"),
        });
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);
        var sut = Service(fs, InitializingState(), documents, documentStates);

        await sut.LoadAsync(Root);

        CollectionAssert.AreEqual(new[] { "Mod1.bas" }, documents.GetAllDocuments().Select(document => Path.GetFileName(document.RelativePath)).ToArray());
        Assert.IsTrue(documents.TryGetDocument(new Uri(Path.Combine(Root, "README.md")), out var readme), "a document of the workspace all the same");
        Assert.IsInstanceOfType<LoadedDocumentState>(documentStates.GetCurrentState(readme.Id));
        Assert.IsTrue(documents.TryGetDocument(new Uri(Path.Combine(Root, "res", "App.res")), out _));
    }

    // a project file's version is the version of its format, and a project file loads whatever it says: the first builds stamped theirs 1.0.0, which a
    // 0.1 platform refused as a later platform's (desk card version-010-refuses-100). What is not this platform's format is said in the log, and only there.
    [TestMethod]
    [DataRow("\"1.0.0\"", null, DisplayName = "1.0.0, as the first builds stamped it: the format 1.0")]
    [DataRow("\"1.0\"", null, DisplayName = "1.0, this platform's format")]
    [DataRow("\"0.1.3\"", LogLevel.Information, DisplayName = "0.1.3, as the next builds stamped it: an older format")]
    [DataRow("\"1.7\"", LogLevel.Warning, DisplayName = "1.7, a newer minor")]
    [DataRow("\"100.0.0\"", LogLevel.Warning, DisplayName = "100.0.0, a newer major that reads all the same")]
    [DataRow("2", LogLevel.Warning, DisplayName = "the number 2, a newer major")]
    [DataRow("\"v1\"", LogLevel.Warning, DisplayName = "v1, no format's version")]
    [DataRow("{ \"Major\": 1 }", LogLevel.Warning, DisplayName = "an object, no format's version")]
    public async Task WorkspaceService_LoadAsync_LoadsAProjectFileWhateverFormatItSays_AndLogsOneThatIsNotThisOne(string version, LogLevel? logged)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectFilePath] = new MockFileData($$"""{ "Version": {{version}}, "Configuration": [], "ProjectInfo": { "Name": "Written", "Modules": [ "Module1.bas" ] } }"""),
            [Path.Combine(Root, "Module1.bas")] = new MockFileData("Attribute VB_Name = \"Module1\"\r\n"),
        });
        var logger = new CapturingLogger();
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);
        var sut = Service(fs, InitializingState(), documents, documentStates, logger);

        await sut.LoadAsync(Root);

        Assert.ContainsSingle(documents.GetAllDocuments(), "the project's module is loaded");
        var said = logger.Entries.Where(entry => entry.Message.Contains("format", StringComparison.Ordinal)).Select(entry => entry.Level).ToArray();
        CollectionAssert.AreEqual(logged is { } level ? new[] { level } : Array.Empty<LogLevel>(), said);
    }

    private const string Unreadable = "{ \"Version\": \"1.0\", \"ProjectInfo\": { \"Name\": \"Broken\", \"Modules\": 5 } }";

    private static MockFileSystem UnreadableProject(params string[] backups)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectFilePath] = new MockFileData(Unreadable),
            [Path.Combine(Root, "src", "Module1.bas")] = new MockFileData("Attribute VB_Name = \"Module1\"\r\n"),
        });
        foreach (var backup in backups)
        {
            fs.AddFile(Path.Combine(Root, backup), new MockFileData("an earlier backup"));
        }

        return fs;
    }

    // a project file that can't be read is regenerated when the user says to: it is kept next to itself first, and the project is the one the folder's
    // source files make, written in this platform's format.
    [TestMethod]
    [DataRow(new string[0], ".rdproj.bak", DisplayName = "kept as .rdproj.bak")]
    [DataRow(new[] { ".rdproj.bak" }, ".rdproj.1.bak", DisplayName = "kept as .rdproj.1.bak, when there is a .rdproj.bak")]
    public async Task WorkspaceService_LoadAsync_AnUnreadableProjectFile_IsRegenerated_WhenTheUserAccepts(string[] backups, string keptAs)
    {
        var fs = UnreadableProject(backups);
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);
        var sut = Service(fs, InitializingState(), documents, documentStates);
        string? asked = null;

        await sut.LoadAsync(Root, [Root], (message, choices, _) =>
        {
            asked = message;
            return Task.FromResult<string?>(choices[0]);
        });

        StringAssert.Contains(asked, keptAs, "the question says where the file is kept");
        Assert.AreEqual(Unreadable, fs.File.ReadAllText(Path.Combine(Root, keptAs)), "the file as it was is kept next to it");
        foreach (var backup in backups)
        {
            Assert.AreEqual("an earlier backup", fs.File.ReadAllText(Path.Combine(Root, backup)), "an earlier backup is left alone");
        }

        var regenerated = await new ProjectFileLoader(fs).LoadAsync(Root);
        Assert.AreEqual(ProjectFile.SchemaVersion, regenerated.Version);
        CollectionAssert.AreEqual(new[] { "src/Module1.bas" }, regenerated.ProjectInfo.Modules.Select(module => module.RelativeUri.Replace('\\', '/')).ToArray());
        Assert.ContainsSingle(documents.GetAllDocuments(), "the workspace is loaded from the regenerated project");
    }

    // nothing is regenerated unless the user says to: a project file the user would not have regenerated, or that nobody could be asked about, does not load,
    // and is left as it is.
    [TestMethod]
    [DataRow(1, DisplayName = "the user chose not now")]
    [DataRow(-1, DisplayName = "the user closed the question")]
    [DataRow(-2, DisplayName = "the client answered with an error")]
    [DataRow(-3, DisplayName = "nobody is there to ask: the client runs unattended")]
    public async Task WorkspaceService_LoadAsync_AnUnreadableProjectFile_IsNotRegenerated_UnlessTheUserAccepts(int answer)
    {
        var fs = UnreadableProject();
        var sut = WorkspaceService(fs);
        Func<string, IReadOnlyList<string>, CancellationToken, Task<string?>>? ask = answer switch
        {
            -3 => null,
            -2 => (_, _, _) => Task.FromException<string?>(new InvalidOperationException("the client does not know window/showMessageRequest")),
            -1 => (_, _, _) => Task.FromResult<string?>(null),
            _ => (_, choices, _) => Task.FromResult<string?>(choices[answer]),
        };

        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => sut.LoadAsync(Root, [Root], ask));

        StringAssert.Contains(error.Message, ProjectFile.FileName, "the load fails with the message that says which file, and why");
        Assert.AreEqual(Unreadable, fs.File.ReadAllText(ProjectFilePath));
        Assert.IsFalse(fs.File.Exists(ProjectFilePath + ".bak"));
    }

    // the question says where the file stops being valid, in the user's language, and none of what the JSON reader said in its own.
    [TestMethod]
    [DataRow("en", "it isn't valid at line 1, position ")]
    [DataRow("fr", "il n'est pas valide à la ligne 1, position ")]
    public async Task WorkspaceService_LoadAsync_TheRegenerateQuestion_SaysTheLineAndPosition_AndNotTheReadersMessage(string culture, string where)
    {
        var sut = WorkspaceService(UnreadableProject());
        string? asked = null;
        var before = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
        try
        {
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => sut.LoadAsync(Root, [Root], (message, choices, _) =>
            {
                asked = message;
                return Task.FromResult<string?>(choices[^1]);
            }));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = before;
        }

        StringAssert.Contains(asked, where);
        Assert.AreEqual(1, asked!.Split(ProjectFilePath).Length - 1, "the file's path is said once");
        Assert.DoesNotContain("Path: $", asked);
        Assert.DoesNotContain("LineNumber", asked);
    }

    // the position is counted in characters, as an editor counts them, not in the UTF-8 bytes the JSON reader counts: a name of three accented letters before
    // the error puts it where three plain ones do.
    [TestMethod]
    public async Task WorkspaceService_LoadAsync_TheRegenerateQuestion_CountsThePositionInCharacters()
    {
        async Task<string> PositionWith(string name)
        {
            var fs = UnreadableProject();
            fs.File.WriteAllText(ProjectFilePath, Unreadable.Replace("Broken", name, StringComparison.Ordinal), System.Text.Encoding.UTF8);
            string? asked = null;
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => WorkspaceService(fs).LoadAsync(Root, [Root], (message, choices, _) =>
            {
                asked = message;
                return Task.FromResult<string?>(choices[^1]);
            }));
            return System.Text.RegularExpressions.Regex.Match(asked!, @"line \d+, position \d+").Value;
        }

        var before = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en");
        try
        {
            var plain = await PositionWith("Eee");
            StringAssert.Contains(plain, "position ", "the premise: the question says a position");
            Assert.AreEqual(plain, await PositionWith("Éèé"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = before;
        }
    }

    // the question goes over the wire while initialize is not answered yet, as the language server asks it, of a client that says in its initialization options
    // that it answers then; not of one that does not say so, nor of one that runs unattended, whatever else it says.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    [DataRow(true, false, DisplayName = "asked, and regenerated, before initialize is answered")]
    [DataRow(false, false, DisplayName = "a client that does not say it answers is not asked")]
    [DataRow(true, true, DisplayName = "a client that runs unattended is not asked")]
    public async Task TheRegenerateQuestion_IsAskedDuringInitialize_OverTheWire(bool answers, bool unattended)
    {
        var asked = answers && !unattended;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var fs = UnreadableProject();
        var sut = WorkspaceService(fs);
        var toServer = new System.IO.Pipelines.Pipe();
        var toClient = new System.IO.Pipelines.Pipe();
        var questions = new List<string>();
        var loaded = false;
        var serving = OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(server => server
            .WithInput(toServer.Reader).WithOutput(toClient.Writer)
            .OnInitialize(async (server, request, token) =>
            {
                try
                {
                    await sut.LoadAsync(Root, [Root], RDCore.LanguageServer.CoreLanguageServerApp.RegenerateQuestionOf(server, request), token);
                    loaded = true;
                }
                catch (InvalidDataException)
                {
                }
            }), timeout.Token);
        var connecting = OmniSharp.Extensions.LanguageServer.Client.LanguageClient.From(client => client
            .WithInput(toClient.Reader).WithOutput(toServer.Writer)
            .WithInitializationOptions(new RDCore.SDK.Platform.Protocol.RDCoreInitializationOptions { Unattended = unattended, AnswersDuringInitialize = answers })
            .OnShowMessageRequest(request =>
            {
                questions.Add(request.Message);
                return Task.FromResult(request.Actions!.First());
            }), timeout.Token);

        await Task.WhenAll(serving, connecting).WaitAsync(TimeSpan.FromSeconds(20));
        using var server = await serving;
        using var client = await connecting;

        // initialize is answered once the load is over: what the load asked, and did, is in by now.
        Assert.HasCount(asked ? 1 : 0, questions);
        Assert.AreEqual(asked, loaded);
        Assert.AreEqual(asked, fs.File.Exists(ProjectFilePath + ".bak"));
        Assert.AreEqual(!asked, fs.File.ReadAllText(ProjectFilePath) == Unreadable);
    }

    // the shell's own language client - what it says in its initializationOptions, the handler it answers the question with, the connection it makes - started
    // on a workspace whose project file can't be read. A shell whose input is a script was asked whether to regenerate it anyway, and answered that nobody does:
    // with an error, which its language client (OmniSharp's) holds until the server has answered initialize, while the server waited for the answer to answer
    // it. Neither moved again. It is not asked now; a shell at its console is, and answers, before initialize is answered. Either way the server tells it why the
    // workspace was not loaded (window/showMessage, as the language server does), and the shell writes it as an error: it used to have no handler for it.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    [DataRow(false, DisplayName = "a shell whose input is a script is not asked")]
    [DataRow(true, DisplayName = "a shell at its console is asked, and answers")]
    public async Task TheShell_StartedOnAProjectFileThatCantBeRead_HasInitializeAnswered_AndIsToldWhy(bool interactive)
    {
        var fs = UnreadableProject();
        var sut = WorkspaceService(fs);
        var asked = 0;
        var console = Substitute.For<RDCore.CLI.App.Repl.IReplConsole>();
        var told = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        console.When(each => each.WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Arg.Any<string>(), Arg.Any<string?>()))
            .Do(call => told.TrySetResult(call.ArgAt<string>(1)));
        // the message is sent before initialize is answered, and the shell's handler runs whenever its scheduler gets to it: the handshake that follows
        // initialize is answered once the shell has written it, so that the shell does not read to the end of its input and close the connection first.
        using var languageServer = new InProcessLanguageServer(async (server, request, token) =>
        {
            var ask = RDCore.LanguageServer.CoreLanguageServerApp.RegenerateQuestionOf(server, request);
            try
            {
                await sut.LoadAsync(Root, [Root], ask is null ? null : (message, choices, askToken) =>
                {
                    Interlocked.Increment(ref asked);
                    return ask(message, choices, askToken);
                }, token);
            }
            catch (InvalidDataException error)
            {
                server.Window.ShowMessage(new OmniSharp.Extensions.LanguageServer.Protocol.Models.ShowMessageParams
                {
                    Type = OmniSharp.Extensions.LanguageServer.Protocol.Models.MessageType.Error,
                    Message = error.Message,
                });
            }
        }, answerHandshakeAfter: told.Task);
        // the Enter key chooses the last choice, which changes nothing.
        var keys = new Cli.Repl.ScriptedKeys(interactive).Press(Cli.Repl.ScriptedKeys.Key(ConsoleKey.Enter));
        var shell = new RDCore.CLI.RDCoreConsoleClientApp(
            Microsoft.Extensions.Options.Options.Create(new RDCore.SDK.Server.Configuration.SdkAppOptions { Server = { MaxRestartAttempts = 0 } }),
            languageServer,
            new RDCore.CLI.App.Repl.ReplUserInteraction(keys, new StringWriter()),
            console,
            new RDCore.SDK.Formatting.RubberduckIndenterSettingsReader(
                new MockFileSystem(), Root, Substitute.For<RDCore.SDK.Formatting.ILegacyIndenterRegistry>(), isWindows: false),
            Substitute.For<RDCore.CLI.App.Configuration.IPlatformSettings>(),
            Substitute.For<RDCore.SDK.Server.Configuration.ILiveSettings>(),
            Microsoft.Extensions.Options.Options.Create(new RDCore.CLI.AppOptions()),
            new RDCore.CLI.App.Repl.ReplServerQuestions(keys),
            NullLogger<RDCore.CLI.RDCoreConsoleClientApp>.Instance);
        var platform = Substitute.For<RDCore.SDK.Platform.IPlatformCompositionService>();
        platform.GetManifest().Returns(new RDCore.SDK.Platform.PlatformManifest { LangService = "RDCore.LanguageServer/RDCore.LanguageServer.exe" });
        using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSingleton(platform).BuildServiceProvider();

        // not a timing: an initialize that is never answered fails the test here instead of hanging the run.
        await shell.RunAsync(services, []).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.AreEqual(interactive ? 1 : 0, asked);
        Assert.AreEqual(Unreadable, fs.File.ReadAllText(ProjectFilePath), "the project file is as it was: the shell chose to keep it, or was not asked");
        Assert.IsFalse(fs.File.Exists(ProjectFilePath + ".bak"));
        // not a timing either: a message the shell never writes fails the test here.
        StringAssert.Contains(await told.Task.WaitAsync(TimeSpan.FromSeconds(30)), ProjectFilePath);
    }

    // a project file that could not be written in its place is put back as it was: a folder left without one would be made up again at the next start, without
    // a word, and without the references only the file said.
    [TestMethod]
    public async Task WorkspaceService_LoadAsync_ARegeneratedProjectFileThatCantBeWritten_LeavesTheProjectFileAsItWas()
    {
        var fs = UnreadableProject();
        var projectFiles = new ProjectFileService(NullLogger<ProjectFileService>.Instance, fs.Path, new FullDisk(fs), new ProjectFileLoader(fs), () => SupportedLanguages.RDVBA);
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);
        var sut = new WorkspaceService(
            InitializingState(), NullLogger<WorkspaceService>.Instance, fs.Path, fs.File, fs.Directory, projectFiles, documents, documentStates,
            () => ProtocolSupportedLanguage.VBA, new WorkspaceProjects(projectFiles), new ProjectFileLoader(fs));

        var error = await Assert.ThrowsExactlyAsync<IOException>(() => sut.LoadAsync(Root, [Root], (_, choices, _) => Task.FromResult<string?>(choices[0])));

        Assert.AreEqual("There is not enough space on the disk.", error.Message);
        Assert.AreEqual(Unreadable, fs.File.ReadAllText(ProjectFilePath), "the project file is back as it was, not the half of one that was written");
        Assert.IsFalse(fs.File.Exists(ProjectFilePath + ".bak"), "and is not left as a backup beside nothing");
    }

    // the project made up from the folder is let go of with the file it couldn't write: a change to the workspace afterwards (a rename, a file added) changes
    // and writes no project, as after a project file the user would not have regenerated, and the file that was put back stays as it is.
    [TestMethod]
    public async Task WorkspaceService_LoadAsync_ARegeneratedProjectFileThatCantBeWritten_IsNotWrittenOverByTheNextChange()
    {
        var fs = UnreadableProject();
        var projectFiles = new ProjectFileService(NullLogger<ProjectFileService>.Instance, fs.Path, new FullDisk(fs, failures: 1), new ProjectFileLoader(fs),
            () => SupportedLanguages.RDVBA);
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);
        var sut = new WorkspaceService(
            InitializingState(), NullLogger<WorkspaceService>.Instance, fs.Path, fs.File, fs.Directory, projectFiles, documents, documentStates,
            () => ProtocolSupportedLanguage.VBA, new WorkspaceProjects(projectFiles), new ProjectFileLoader(fs));
        await Assert.ThrowsExactlyAsync<IOException>(() => sut.LoadAsync(Root, [Root], (_, choices, _) => Task.FromResult<string?>(choices[0])));
        // the server goes on to run, as the language server does after a load that failed.
        var running = Substitute.For<IServerStateProvider>();
        running.State.Returns(ServerState.Running);
        var reload = new WorkspaceReloadService(running, projectFiles, documents, documentStates, Substitute.For<RDCore.LanguageServer.Parsing.IParsingClientService>(),
            Substitute.For<RDCore.LanguageServer.Symbols.ISymbolSyncService>(), Substitute.For<RDCore.LanguageServer.IPlatformOrchestrationService>(), fs.Path,
            Substitute.For<IWorkspaceRefresh>(), NullLogger<WorkspaceReloadService>.Instance);

        var step = await reload.BeginChangeAsync(project => project.WithModule(new RDCoreModule { RelativeUri = "src/Renamed.bas" }), CancellationToken.None);

        Assert.IsNull(step, "there is no project to change");
        Assert.AreEqual(Unreadable, fs.File.ReadAllText(ProjectFilePath), "the project file put back is not written over");
        Assert.IsFalse(fs.File.Exists(ProjectFilePath + ".bak"));
    }

    // a project file that can't be put back either is kept where it was moved to, and the log says both what failed and where the file is; the load fails
    // with why the file couldn't be written, and the user is told where the file as it was is, in their language: the language server shows them what the load
    // failed with.
    [TestMethod]
    [DataRow("en", "nor put back as it was")]
    [DataRow("fr", "ni remis tel qu'il était")]
    public async Task WorkspaceService_LoadAsync_ARegeneratedProjectFileThatCantBeWrittenNorPutBack_IsKeptAsTheBackup_AndTheUserIsToldWhere(string culture, string said)
    {
        var before = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
        ProjectFileNotPutBackException error;
        try
        {
            (error, _, _) = await ARegeneratedProjectFileThatCantBeWrittenNorPutBackAsync();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = before;
        }

        StringAssert.Contains(error.Message, said);
        StringAssert.Contains(error.Message, ProjectFilePath + ".bak", "where the file as it was is");
        Assert.AreEqual(ProjectFilePath + ".bak", error.BackupPath);
        Assert.AreEqual(ProjectFilePath, error.ProjectFilePath);
        Assert.AreEqual("There is not enough space on the disk.", error.InnerException?.Message, "why it couldn't be regenerated");
    }

    [TestMethod]
    public async Task WorkspaceService_LoadAsync_ARegeneratedProjectFileThatCantBeWrittenNorPutBack_IsKeptAsTheBackup_AndTheLogSaysBoth()
    {
        var (error, fs, logger) = await ARegeneratedProjectFileThatCantBeWrittenNorPutBackAsync();

        Assert.AreEqual("There is not enough space on the disk.", error.InnerException?.Message);
        Assert.AreEqual(Unreadable, fs.File.ReadAllText(ProjectFilePath + ".bak"), "the file as it was is where it was moved to");
        var said = logger.Entries.Single(entry => entry.Level == LogLevel.Error).Message;
        StringAssert.Contains(said, "There is not enough space on the disk.");
        StringAssert.Contains(said, ProjectFilePath + ".bak");
        StringAssert.Contains(said, "The process cannot access the file because it is being used by another process.");
    }

    private static async Task<(ProjectFileNotPutBackException Error, MockFileSystem Fs, CapturingLogger Logger)> ARegeneratedProjectFileThatCantBeWrittenNorPutBackAsync()
    {
        var fs = UnreadableProject();
        var file = new FullDisk(fs) { RefusesToPutBack = true };
        var projectFiles = new ProjectFileService(NullLogger<ProjectFileService>.Instance, fs.Path, file, new ProjectFileLoader(fs), () => SupportedLanguages.RDVBA);
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);
        var logger = new CapturingLogger();
        var sut = new WorkspaceService(
            InitializingState(), logger, fs.Path, file, fs.Directory, projectFiles, documents, documentStates,
            () => ProtocolSupportedLanguage.VBA, new WorkspaceProjects(projectFiles), new ProjectFileLoader(fs));

        var error = await Assert.ThrowsExactlyAsync<ProjectFileNotPutBackException>(() => sut.LoadAsync(Root, [Root], (_, choices, _) => Task.FromResult<string?>(choices[0])));
        return (error, fs, logger);
    }

    // a disk that fills up as a project file is written, the first times it is: the file is made, and holds the start of what was written. One that refuses to
    // put a backup back fails that move, as a file another process holds open does.
    private sealed class FullDisk(MockFileSystem fs, int failures = int.MaxValue) : MockFile(fs)
    {
        private int _failures = failures;

        public bool RefusesToPutBack { get; init; }

        public override System.IO.Abstractions.FileSystemStream Open(string path, FileMode mode)
        {
            if (mode == FileMode.Create && path.EndsWith(ProjectFile.FileName, StringComparison.Ordinal) && _failures > 0)
            {
                _failures--;
                fs.AddFile(path, new MockFileData("{ \"Vers"));
                throw new IOException("There is not enough space on the disk.");
            }

            return base.Open(path, mode);
        }

        public override void Move(string sourceFileName, string destFileName)
        {
            if (RefusesToPutBack && sourceFileName.EndsWith(".bak", StringComparison.Ordinal))
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            base.Move(sourceFileName, destFileName);
        }
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<WorkspaceService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [TestMethod]
    public async Task WorkspaceDocumentService_TryGetDocument_FindsALoadedDocumentByUri_AndMissesAnUnknownOne()
    {
        var fs = FileSystemWith(("src/Mod1.bas", "Public Sub Foo()\r\nEnd Sub"));
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(
            documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);

        var sut = Service(fs, InitializingState(), documents, documentStates);
        await sut.LoadAsync(Root);

        var loaded = documents.GetAllDocuments().Single();

        Assert.IsTrue(documents.TryGetDocument(loaded.Id.Uri.ToUri(), out var found));
        Assert.AreEqual(loaded.Text, found.Text);
        Assert.IsFalse(documents.TryGetDocument(new Uri("file:///c:/nowhere/Ghost.bas"), out _));
    }

    [TestMethod]
    public async Task WorkspaceService_LoadAsync_NotInitializing_Throws()
    {
        var state = Substitute.For<IServerStateProvider>();
        state.State.Returns(ServerState.Running);

        var sut = WorkspaceService(FileSystemWith(("src/Mod1.bas", "")), state);

        await Assert.ThrowsExactlyAsync<InvalidServerStateException>(() => sut.LoadAsync(Root));
    }

    // what the shell starts as its language server, and connects to: an OmniSharp server on the pipe, in this process, which answers initialize as it is given
    // to, and the platform handshake after it - once answerHandshakeAfter has completed, when it is given: the shell goes no further than the handshake until then.
    private sealed class InProcessLanguageServer(
        Func<OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServer, OmniSharp.Extensions.LanguageServer.Protocol.Models.InitializeParams, CancellationToken, Task> initialize,
        Task? answerHandshakeAfter = null)
        : RDCore.SDK.Client.IRDCoreServerProcess, RDCore.SDK.Client.Connection.IChildConnectionFactory
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<IDisposable> _held = [];

        public int ProcessId => Environment.ProcessId;

        public bool HasExited => _exit.Task.IsCompleted;

        public int ExitCode => 0;

        public RDCore.SDK.Client.Connection.ChildConnection Create()
            => Hold(new RDCore.SDK.Client.Connection.ChildConnection(
                this, new RDCore.SDK.Server.RDCorePlatformDefaultTransportLayer(Microsoft.Extensions.Options.Options.Create(new RDCore.SDK.Server.Configuration.SdkAppOptions())),
                NullLogger<RDCore.SDK.Client.Connection.ChildConnection>.Instance));

        public Task StartAsync(string relativePath, string pipeName, CancellationTokenSource tokenSource, string? mode = null)
        {
            var pipe = Hold(new System.IO.Pipes.NamedPipeServerStream(pipeName, System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte,
                System.IO.Pipes.PipeOptions.CurrentUserOnly | System.IO.Pipes.PipeOptions.Asynchronous));
            _ = ServeAsync(pipe);
            return Task.CompletedTask;
        }

        private async Task ServeAsync(System.IO.Pipes.NamedPipeServerStream pipe)
        {
            await pipe.WaitForConnectionAsync();
            Hold(await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(server => server
                .WithInput(System.IO.Pipelines.PipeReader.Create(pipe))
                .WithOutput(System.IO.Pipelines.PipeWriter.Create(pipe))
                .WithSerializer(new RDCore.SDK.Server.Serialization.PullDiagnosticsLspSerializer())
                .OnInitialize((server, request, token) => initialize(server, request, token))
                .OnJsonRequest(RDCore.SDK.Client.RDCorePlatformProtocol.PlatformInitialize, async (Newtonsoft.Json.Linq.JToken _, CancellationToken _) =>
                {
                    if (answerHandshakeAfter is not null)
                    {
                        await answerHandshakeAfter;
                    }

                    return (Newtonsoft.Json.Linq.JToken)Newtonsoft.Json.Linq.JObject.FromObject(
                        new { component = (int)RDCore.SDK.Client.CoreServerComponent.LanguageServer, provided = Array.Empty<string>() });
                })));
        }

        private T Hold<T>(T disposable) where T : IDisposable
        {
            lock (_held)
            {
                _held.Add(disposable);
            }

            return disposable;
        }

        public Task WaitForExitAsync() => _exit.Task;

        public void Shutdown() => _exit.TrySetResult();

        // the connection first, then the server, then the pipe it served on.
        public void Dispose()
        {
            IDisposable[] held;
            lock (_held)
            {
                held = [.. _held];
                _held.Clear();
            }

            _exit.TrySetResult();
            foreach (var disposable in held.OfType<RDCore.SDK.Client.Connection.ChildConnection>().Concat<IDisposable>(
                held.Where(each => each is not RDCore.SDK.Client.Connection.ChildConnection).Reverse()))
            {
                disposable.Dispose();
            }
        }
    }
}
