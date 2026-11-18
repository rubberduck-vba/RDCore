using NSubstitute;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A project of the workspace binds its names among the names each project of the workspace it references gives it: that project's public names, at the
/// precedence of the reference (MS-VBAL §4.1, §5.2.3).
/// </summary>
[TestClass]
public sealed class ReferencedProjectServiceTests
{
    private static readonly string ConsoleRoot =Path.Combine(Path.GetTempPath(), "rdcore-referenced-projects", "NavalConsole");
    private static readonly string Lib = Path.Combine(Path.GetTempPath(), "rdcore-referenced-projects", "NavalLib");

    private readonly IWorkspaceProjects _projects = Substitute.For<IWorkspaceProjects>();
    private readonly IReferencedLibraryService _libraries = Substitute.For<IReferencedLibraryService>();
    private readonly List<(WorkspaceDocument Document, ModuleParseResult Parse)> _modules = [];
    private readonly Symbol[] _own = [];

    public ReferencedProjectServiceTests()
    {
        Add(ConsoleRoot, "Program.bas", "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        Add(Lib, "Grid.bas", "Attribute VB_Name = \"Grid\"\r\nPublic Sub PlaceShip()\r\nEnd Sub\r\nPrivate Sub Clear()\r\nEnd Sub\r\n");
        Add(Lib, "Internals.bas", "Attribute VB_Name = \"Internals\"\r\nOption Private Module\r\nPublic Sub Reset()\r\nEnd Sub\r\n");
        ProjectsAre(Project(ConsoleRoot, "NavalConsole", "NavalLib"), Project(Lib, "NavalLib"));

        _libraries.SymbolsUnder(default!).ReturnsForAnyArgs(_own);
        _libraries.SymbolsOf(default!, default!).ReturnsForAnyArgs(Array.Empty<Symbol>());
        _libraries.LibrariesOf(default!).ReturnsForAnyArgs(call => Load((RDCoreProject)call[0]));
    }

    private void Add(string root, string file, string source)
    {
        var document = new WorkspaceDocument(file, root, source) { Projects = [root] };
        _modules.Add((document, new ModuleParser().Parse(document.Id.Uri.ToUri(), source)));
    }

    private static WorkspaceProject Project(string root, string name, params string[] references)
        => new(root, new ProjectFile(root, new RDCoreProject
        {
            Name = name,
            References = [.. references.Select(reference => new RDCoreReference { Name = reference, IsProject = true })],
        }));

    private void ProjectsAre(params WorkspaceProject[] projects)
    {
        _projects.Projects.Returns(projects);
        _libraries.Libraries.Returns(Load(projects[0].Project.ProjectInfo));
    }

    // a reference to a project loads nothing: it keeps its place in the order.
    private static ReferencedLibraries Load(RDCoreProject project) => LibrarySymbolProvider.Load(Substitute.For<ILibrarySource>(), project.References ?? []);

    private ReferencedProjectService Sut()
    {
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.WorkspaceRoot.Returns(ConsoleRoot);
        documents.GetAllDocuments().Returns(_ => _modules.Select(module => module.Document).ToList());

        var parsing = Substitute.For<IParsingClientService>();
        parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>()).Returns(call =>
        {
            var uri = ((Uri)call[0]).AbsoluteUri;
            var parse = _modules.FirstOrDefault(module => module.Document.Id.Uri.ToUri().AbsoluteUri == uri).Parse;
            call[1] = parse;
            return parse is not null;
        });

        return new ReferencedProjectService(_libraries, _projects, documents, parsing, new IntrinsicSymbolResolver(),
            Microsoft.Extensions.Options.Options.Create(new SdkAppOptions()));
    }

    private static string[] NamesOf(IEnumerable<Symbol> symbols, string library)
        => [.. symbols.Where(symbol => symbol.GetProperty(SymbolProperties.Library) == library).Select(symbol => symbol.Name)];

    [TestMethod]
    public void TheNamesOfAProjectTheProjectReferences_AreAmongItsLibraries_AtThePrecedenceOfTheReference()
    {
        var symbols = Sut().LibrariesOf(SymbolUri.Root(ConsoleRoot), ConsoleRoot);

        CollectionAssert.AreEquivalent(new[] { "NavalLib", "Grid", "PlaceShip" }, NamesOf(symbols, "NavalLib"));
        Assert.IsTrue(symbols.Where(symbol => symbol.GetProperty(SymbolProperties.Library) == "NavalLib")
            .All(symbol => symbol.GetProperty(SymbolProperties.LibraryPriority) == 1));
        Assert.AreEqual(SymbolUri.Root(ConsoleRoot).AbsoluteUri, symbols.OfType<VBProjectSymbol>().Single().WorkspaceRoot.AbsoluteUri,
            "the project is named in the one referencing it");
    }

    [TestMethod]
    public void AProjectThatReferencesNoProject_IsBoundAmongItsLibraries()
    {
        var symbols = Sut().LibrariesOf(SymbolUri.Root(Lib), Lib);

        Assert.IsEmpty(symbols);
        _libraries.Received().SymbolsOf(Arg.Any<Uri>(), Arg.Is<RDCoreProject>(project => project.Name == "NavalLib"));
    }

    [TestMethod]
    public void TheList_IsTheSameOne_ForAsLongAsWhatItIsMadeOfIs()
    {
        var sut = Sut();

        Assert.AreSame(sut.LibrariesOf(SymbolUri.Root(ConsoleRoot), ConsoleRoot), sut.LibrariesOf(SymbolUri.Root(ConsoleRoot), ConsoleRoot));
    }

    [TestMethod]
    public void AModuleTheReferencedProjectChanged_IsWhatItsNamesAre()
    {
        var sut = Sut();
        var before = sut.LibrariesOf(SymbolUri.Root(ConsoleRoot), ConsoleRoot);
        _modules.RemoveAt(1);
        Add(Lib, "Grid.bas", "Attribute VB_Name = \"Grid\"\r\nPublic Sub Sink()\r\nEnd Sub\r\n");

        var after = sut.LibrariesOf(SymbolUri.Root(ConsoleRoot), ConsoleRoot);

        Assert.AreNotSame(before, after);
        CollectionAssert.AreEquivalent(new[] { "NavalLib", "Grid", "Sink" }, NamesOf(after, "NavalLib"));
    }

    [TestMethod]
    public void ANameTwoProjectsShare_NamesNeither()
    {
        var other = Path.Combine(Path.GetTempPath(), "rdcore-referenced-projects", "NavalLib2");
        ProjectsAre(Project(ConsoleRoot, "NavalConsole", "NavalLib"), Project(Lib, "NavalLib"), Project(other, "NavalLib"));

        Assert.IsEmpty(NamesOf(Sut().LibrariesOf(SymbolUri.Root(ConsoleRoot), ConsoleRoot), "NavalLib"));
    }

    [TestMethod]
    public void ACircularReference_GivesTheNamesOfTheProjectsBeforeTheReferenceThatClosesIt()
    {
        ProjectsAre(Project(ConsoleRoot, "NavalConsole", "NavalLib"), Project(Lib, "NavalLib", "NavalConsole"));

        var symbols = Sut().LibrariesOf(SymbolUri.Root(ConsoleRoot), ConsoleRoot);

        CollectionAssert.AreEquivalent(new[] { "NavalLib", "Grid", "PlaceShip" }, NamesOf(symbols, "NavalLib"));
        Assert.IsEmpty(NamesOf(symbols, "NavalConsole"));
    }
}
