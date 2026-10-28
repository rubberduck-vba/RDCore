using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The workspace of a client that opened several folders is composed one project at a time: a module name is unique within its project, so two projects
/// can each have a <c>Module1</c>, and a file that two projects list is a module of each.
/// </summary>
[TestClass]
public sealed class RenameServiceProjectsTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "rdcore-rename-projects");
    private static readonly string RootA = Path.Combine(Base, "A");
    private static readonly string RootB = Path.Combine(Base, "B");
    private static readonly string Shared = Path.Combine(Base, "Shared");

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly WorkspaceProjects _projects;

    private static WorkspaceDocument Doc(string root, string file, string text, params string[] projects)
        => new(file, root, text) { Projects = projects.Length == 0 ? [root] : projects };

    private static Uri UriOf(WorkspaceDocument document) => document.Id.Uri.ToUri();

    private readonly WorkspaceDocument _moduleA = Doc(RootA, "Module1.bas", "Attribute VB_Name = \"Module1\"\r\nPublic Sub Run()\r\nEnd Sub\r\n");
    private readonly WorkspaceDocument _moduleB = Doc(RootB, "Module1.bas", "Attribute VB_Name = \"Module1\"\r\nPublic Sub Run()\r\nEnd Sub\r\nPublic Sub Other()\r\nEnd Sub\r\n");
    private readonly WorkspaceDocument _lib = Doc(RootB, "Lib.bas", "Attribute VB_Name = \"Lib\"\r\nPublic Sub Go()\r\n    Util.Tidy\r\nEnd Sub\r\n");
    private readonly WorkspaceDocument _util = Doc(RootA, Path.Combine("..", "Shared", "Util.bas"), "Attribute VB_Name = \"Util\"\r\nPublic Sub Tidy()\r\nEnd Sub\r\n", RootA, RootB);

    public RenameServiceProjectsTests()
    {
        var primary = Substitute.For<IProjectFileService>();
        primary.Project.Returns(new ProjectFile(RootA, new RDCoreProject { Name = "A" }));
        _projects = new WorkspaceProjects(primary);
        _projects.Initialize(RootA, [RootA, RootB, Shared], [], []);
        _projects.Add(new WorkspaceProject(RootB, new ProjectFile(RootB, new RDCoreProject { Name = "B" })));

        _documents.WorkspaceRoot.Returns(RootA);
        var all = new[] { _moduleA, _moduleB, _lib, _util };
        _documents.GetAllDocuments().Returns(all);
        foreach (var document in all)
        {
            var uri = UriOf(document);
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(document))));
        }
    }

    private RenameService Sut()
    {
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = RootA, Language = "vba" } });
        return new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, workspaceProjects: _projects);
    }

    [TestMethod]
    public async Task ARename_InOneProject_DoesNotTouchTheSameNameInAnother()
    {
        var answer = await Sut().RenameAsync(UriOf(_moduleA), new Position(1, 12), "Execute", CancellationToken.None);

        Assert.IsNotNull(answer.Value, answer.Refusal);
        var edited = answer.Value.DocumentChanges!.Select(change => change.TextDocumentEdit!.TextDocument.Uri.ToUri().AbsoluteUri).ToArray();
        CollectionAssert.AreEqual(new[] { UriOf(_moduleA).AbsoluteUri }, edited, "B has a Module1.Run of its own");
    }

    [TestMethod]
    public async Task ARename_InTheSecondProject_IsOfThatProjectsModules()
    {
        var answer = await Sut().RenameAsync(UriOf(_moduleB), new Position(1, 12), "Execute", CancellationToken.None);

        Assert.IsNotNull(answer.Value, answer.Refusal);
        var edited = answer.Value.DocumentChanges!.Select(change => change.TextDocumentEdit!.TextDocument.Uri.ToUri().AbsoluteUri).ToArray();
        CollectionAssert.AreEqual(new[] { UriOf(_moduleB).AbsoluteUri }, edited);
    }

    [TestMethod]
    public async Task ANameInOneProject_ResolvesToTheFileTheProjectListsFromOutsideItsFolder()
    {
        var workspace = await Sut().ComposeAsync(CancellationToken.None);

        // Lib is B's, and calls Util.Tidy: the file B lists from the shared folder, which B has a module of.
        var found = workspace!.Definitions(UriOf(_lib), new Position(2, 9));

        Assert.AreEqual(UriOf(_util).AbsoluteUri, found.Single().TargetUri.ToUri().AbsoluteUri);
    }

    [TestMethod]
    public async Task AWorkspaceOfOneProject_IsComposedAsItWas()
    {
        var single = new WorkspaceProjects(Substitute.For<IProjectFileService>());
        var libraries = Substitute.For<IReferencedLibraryService>();
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = RootA, Language = "vba" } });
        var sut = new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, workspaceProjects: single);

        var workspace = await sut.ComposeAsync(CancellationToken.None);

        // the modules of the project the documents say, and only those: A's Module1 is renamable, B's is no module of it.
        Assert.AreEqual("Run", workspace!.Prepare(UriOf(_moduleA), new Position(1, 12)).Value!.Placeholder);
        Assert.IsNull(workspace.Prepare(UriOf(_moduleB), new Position(1, 12)).Value);
    }
}
