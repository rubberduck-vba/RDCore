using RDCore.CLI.App.Build;
using RDCore.Runtime.Support.Build;
using System.IO.Abstractions;
using System.Xml.Linq;

namespace RDCore.Tests.Cli;

// what a built program carries of the runtime: RDCore.Runtime.Support and what it references, transitively, over a folder that holds all of rdc's too.
[TestClass]
public sealed class RuntimeSupportClosureTests
{
    private static readonly string Folder = AppContext.BaseDirectory;

    private static readonly string[] RdcsOwn = ["rdc", "RDCore.Parsing", "Antlr4.Runtime", "Spectre.Console", "RDCore.LanguageServer"];

    public TestContext TestContext { get; set; } = null!;

    private static IReadOnlyList<string> Closure() => new ProgramOutput(new FileSystem(), Folder).RuntimeClosure([ProgramOutput.SupportName]);

    private static IReadOnlyList<string> ReferencesOf(string name) => ProgramOutput.ReferencesOf(File.ReadAllBytes(Path.Combine(Folder, $"{name}.dll")));

    [TestMethod]
    public void TheRuntimeSupport_ReferencesNeitherTheParserNorAntlr_InItsAssemblyOrItsProject()
    {
        var references = ReferencesOf(ProgramOutput.SupportName);
        Assert.DoesNotContain("RDCore.Parsing", references);
        Assert.DoesNotContain("Antlr4.Runtime", references);

        var project = XDocument.Load(ProjectFile());
        var included = project.Descendants().Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty).ToArray();
        Assert.IsNotEmpty(included);
        Assert.IsFalse(included.Any(include => include.Contains("RDCore.Parsing", StringComparison.OrdinalIgnoreCase)
            || include.Contains("Antlr", StringComparison.OrdinalIgnoreCase)), string.Join(", ", included));
    }

    [TestMethod]
    public void TheClosure_HoldsNothingOfRdcsOwn()
    {
        var closure = Closure();

        Assert.AreEqual(ProgramOutput.SupportName, closure[0]);
        foreach (var name in RdcsOwn)
        {
            Assert.IsTrue(File.Exists(Path.Combine(Folder, $"{name}.dll")), $"{name} is in the folder the closure is found over");
            Assert.DoesNotContain(name, closure);
        }
    }

    [TestMethod]
    public void EachAssemblyOfTheClosure_IsReferencedByAnother_AndEachOneTheyReferenceInTheFolder_IsInIt()
    {
        var closure = Closure();
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

    // the project file, found from the test's folder up to the repository's root.
    private static string ProjectFile()
    {
        for (var directory = new DirectoryInfo(Folder); directory is not null; directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "RDCore.Runtime.Support", "RDCore.Runtime.Support.csproj");
            if (File.Exists(project))
            {
                return project;
            }
        }

        throw new AssertFailedException("RDCore.Runtime.Support.csproj is not above the test's folder");
    }
}
