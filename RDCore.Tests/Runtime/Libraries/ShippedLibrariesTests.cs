using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Cli;
using System.IO.Abstractions;

namespace RDCore.Tests.Runtime.Libraries;

/// <summary>
/// The descriptions of libraries that ship with the platform (<c>Symbols/</c> of the repository, deployed to the <c>Symbols</c> folder of the platform) are
/// what a project that references <c>Excel</c> or <c>Scripting</c> is checked against, on a machine that has neither.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.3.1.2 References")]
public sealed class ShippedLibrariesTests
{
    /// <summary>The <c>Symbols</c> folder of the repository, where the descriptions that ship are.</summary>
    internal static string Symbols
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory, "the repository root, where RDCore.slnx is");
            return Path.Combine(directory.FullName, "Symbols");
        }
    }

    private static DirectoryLibrarySource Source => new(new FileSystem(), Symbols);

    private static string[] Shipped => [.. Directory.GetFiles(Symbols, "*" + DirectoryLibrarySource.Extension).Select(Path.GetFileNameWithoutExtension).OfType<string>().Order()];

    [TestMethod]
    public void TheLibrariesTheProjectsAskedFor_Ship()
    {
        foreach (var name in new[] { "Excel", "Access", "Word", "ADODB", "Scripting" })
        {
            CollectionAssert.Contains(Shipped, name);
        }
    }

    [TestMethod]
    public void EveryDescriptionThatShips_IsReadable_AndIsTheLibraryItsFileIsNamedFor()
    {
        foreach (var name in Shipped)
        {
            Assert.IsTrue(Source.TryGet(name, out var description), name);
            Assert.AreEqual(name, description.Name);
            Assert.IsNotEmpty(description.Classes.Concat<object>(description.Enums).ToArray(), $"{name} declares something");
        }
    }

    [TestMethod]
    public void TheLibrariesThatShip_AreLoadedTogether_EachWithEveryLibraryItDependsOn_AndNoneDependsOnItself()
    {
        var libraries = ReferencedLibraries.Load(Source, Shipped, ["VBA"]);

        Assert.IsEmpty(libraries.Problems, string.Join("; ", libraries.Problems.Select(problem => $"{problem.Name}: {problem.Kind} {string.Join(" -> ", problem.Related)}")));
        CollectionAssert.AreEquivalent(Shipped, libraries.Libraries.Select(library => library.Name).ToArray());

        // each library comes after the ones it depends on.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries.Libraries)
        {
            Assert.IsTrue(library.DependsOn.All(seen.Contains), library.Name);
            seen.Add(library.Name);
        }
    }

    [TestMethod]
    public void TheSymbolsOfEveryLibraryThatShips_AreRead()
    {
        var libraries = ReferencedLibraries.Load(Source, Shipped, ["VBA"]);

        var symbols = new LibrarySymbolProvider(new Uri("file:///c:/ws/"), libraries).ProvideSymbols().ToArray();

        foreach (var library in libraries.Libraries)
        {
            Assert.IsTrue(symbols.OfType<VBProjectSymbol>().Any(project => project.Name == library.Name), library.Name);
            Assert.AreEqual(
                library.Classes.Length, symbols.OfType<VBClassModuleSymbol>().Count(symbol => symbol.GetProperty(SymbolProperties.Library) == library.Name), library.Name);
        }
    }

    [TestMethod]
    public void TheScriptingDictionary_IsCreatableByItsProgId_AndHasTheDefaultMemberItHas()
    {
        Assert.IsTrue(Source.TryGet("Scripting", out var scripting));

        var dictionary = scripting.Classes.Single(declared => declared.Name == "Dictionary");
        Assert.IsTrue(dictionary.IsCreatable);
        Assert.AreEqual("Scripting.Dictionary", dictionary.ProgId);
        Assert.AreEqual(MemberKind.PropertyGet, dictionary.Members.First(member => member.Name == "Item").Kind, "a property is read before it is assigned");
        Assert.IsTrue(dictionary.Members.Any(member => member.DispId == -4), "For Each over it");
        CollectionAssert.IsSubsetOf(new[] { "Add", "Exists", "Remove", "RemoveAll", "Count", "Keys", "Items" }, dictionary.Members.Select(member => member.Name).ToArray());
    }

    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{string.Join("\r\n", lines)}\r\nPublic Sub Main()\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task AProjectThatReferencesExcel_CanNameItsTypes_AndOneThatDoesNotCannot()
    {
        var code = Program(
            "Public Function Sheet(ByVal app As Excel.Application) As Excel.Worksheet",
            "Dim book As Workbook",
            "Dim area As Range",
            "Dim direction As XlDirection",
            "End Function");

        var referenced = await ModuleWorkspace.LoadErrorsAsync([], code, new ModuleWorkspace.WorkspaceLibraries(["Excel"], Source));
        var unreferenced = await ModuleWorkspace.LoadErrorsAsync([], code, new ModuleWorkspace.WorkspaceLibraries([], Source));

        Assert.IsEmpty(referenced, string.Join("; ", referenced));
        Assert.IsNotEmpty(unreferenced, "without the reference, none of those names is a type");
        StringAssert.Contains(unreferenced[0], "Excel");
    }

    [TestMethod]
    public async Task TheApplicationOfAHost_IsNotAGlobalOfAProject_ThatReferencesItsLibrary()
    {
        // code extracted from a project that ran inside Excel names its application implicitly; here, a project says which object it is talking to.
        var errors = await ModuleWorkspace.LoadErrorsAsync(
            [], Program("Public Sub Use()", "Application.Cursor = 1", "End Sub"), new ModuleWorkspace.WorkspaceLibraries(["Excel"], Source));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "Application");
    }

    [TestMethod]
    public async Task ADictionaryIsNew_WhenTheProjectReferencesTheScriptingLibrary()
    {
        var code = Program("Public Sub Use()", "Dim map As Scripting.Dictionary", "Set map = New Scripting.Dictionary", "map.Add \"a\", 1", "End Sub");

        var referenced = await ModuleWorkspace.LoadErrorsAsync([], code, new ModuleWorkspace.WorkspaceLibraries(["Scripting"], Source));

        Assert.IsEmpty(referenced, string.Join("; ", referenced));
    }
}
