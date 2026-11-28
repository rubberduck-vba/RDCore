using RDCore.CLI.App.Build;
using RDCore.Runtime.Support.Build;
using System.IO.Abstractions;
using System.Xml.Linq;

namespace RDCore.Tests.Cli;

// what a built program carries of the runtime, RDCore.Runtime.Support and what it references, transitively, and what a deployed host references,
// RDCore.Runtime.Hosting and its references: each over a folder that holds all of rdc's too, and never the parser.
[TestClass]
public sealed class RuntimeSupportClosureTests
{
    private const string Hosting = "RDCore.Runtime.Hosting";

    private static readonly string Folder = AppContext.BaseDirectory;

    private static readonly string[] RdcsOwn =
        ["rdc", "RDCore.Parsing", "Antlr4.Runtime", "Spectre.Console", "RDCore.LanguageServer", "RDCore.Runtime.Composer"];

    public TestContext TestContext { get; set; } = null!;

    private static IEnumerable<object[]> Roots => [[ProgramOutput.SupportName], [Hosting]];

    private static IReadOnlyList<string> Closure(string root) => new ProgramOutput(new FileSystem(), Folder).RuntimeClosure([root]);

    private static IReadOnlyList<string> ReferencesOf(string name) => ProgramOutput.ReferencesOf(File.ReadAllBytes(Path.Combine(Folder, $"{name}.dll")));

    [TestMethod]
    [DynamicData(nameof(Roots))]
    public void TheRoot_ReferencesNeitherTheParserNorAntlr_InItsAssemblyOrItsProject(string root)
    {
        var references = ReferencesOf(root);
        Assert.DoesNotContain("RDCore.Parsing", references);
        Assert.DoesNotContain("Antlr4.Runtime", references);

        var project = XDocument.Load(ProjectFile(root));
        var included = project.Descendants().Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty).ToArray();
        Assert.IsNotEmpty(included);
        Assert.IsFalse(included.Any(include => include.Contains("RDCore.Parsing", StringComparison.OrdinalIgnoreCase)
            || include.Contains("Antlr", StringComparison.OrdinalIgnoreCase)), string.Join(", ", included));
    }

    [TestMethod]
    [DynamicData(nameof(Roots))]
    public void TheClosure_HoldsNothingOfRdcsOwn(string root)
    {
        var closure = Closure(root);

        Assert.AreEqual(root, closure[0]);
        foreach (var name in RdcsOwn)
        {
            Assert.IsTrue(File.Exists(Path.Combine(Folder, $"{name}.dll")), $"{name} is in the folder the closure is found over");
            Assert.DoesNotContain(name, closure);
        }
    }

    // the host's closure is the runtime's, and its own assembly.
    [TestMethod]
    public void TheHostsClosure_IsTheRuntimeSupportsAndItsOwn()
    {
        CollectionAssert.AreEquivalent(new[] { Hosting }.Concat(Closure(ProgramOutput.SupportName)).ToArray(), Closure(Hosting).ToArray());
    }

    [TestMethod]
    [DynamicData(nameof(Roots))]
    public void EachAssemblyOfTheClosure_IsReferencedByAnother_AndEachOneTheyReferenceInTheFolder_IsInIt(string root)
    {
        var closure = Closure(root);
        var members = closure.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referenced = closure.SelectMany(ReferencesOf).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in closure.Skip(1))
        {
            Assert.Contains(name, referenced, $"{name} is referenced by no assembly of the closure");
        }

        foreach (var name in referenced.Where(name => File.Exists(Path.Combine(Folder, $"{name}.dll"))))
        {
            Assert.Contains(name, members, $"{name} is referenced, and not in the closure");
        }

        TestContext.WriteLine(string.Join(Environment.NewLine, closure.Order(StringComparer.OrdinalIgnoreCase)));
    }

    // the project file of an assembly, found from the test's folder up to the repository's root.
    private static string ProjectFile(string name)
    {
        for (var directory = new DirectoryInfo(Folder); directory is not null; directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, name, $"{name}.csproj");
            if (File.Exists(project))
            {
                return project;
            }
        }

        throw new AssertFailedException($"{name}.csproj is not above the test's folder");
    }
}
