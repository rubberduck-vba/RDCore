using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Runtime.Libraries;
using static RDCore.Tests.Cli.ModuleWorkspace;
using static RDCore.Tests.Runtime.Libraries.LibraryFixtures;

namespace RDCore.Tests.Cli;

/// <summary>
/// A project that references a library can name the types the library declares, qualified by its name or not; one that does not, or that references a library
/// nothing describes, cannot - and is told that a reference may be missing. The language server and the environment host each bind the names, from the same
/// description, to the same symbols.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.3.1.2 References")]
public sealed class ReferencedLibraryTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{string.Join("\r\n", lines)}\r\nPublic Sub Main()\r\nEnd Sub\r\n";

    private static WorkspaceLibraries References(ILibrarySource source, params string[] names) => new(names, source);

    [TestMethod]
    public async Task ATypeOfAReferencedLibrary_IsFound_QualifiedByTheLibraryAndByItsOwnName()
    {
        var errors = await LoadErrorsAsync([], Program(
            "Public A As Widgets.Gadget",
            "Public B As Gadget",
            "Public Mode As Widgets.WidgetMode",
            "Public Function Make(ByVal m As WidgetMode, ByRef g As Widgets.Gadget) As Part",
            "Dim local As Widgets.Part",
            "End Function"),
            References(new InMemoryLibrarySource(Widgets), "Widgets"));

        Assert.IsEmpty(errors, string.Join("; ", errors));
    }

    [TestMethod]
    public async Task AMemberOfAnEnumerationOfAReferencedLibrary_IsAName_AndAMemberOfAnObjectIsUsedAsOneOfIt()
    {
        var errors = await LoadErrorsAsync([], Program(
            "Public Sub Use()",
            "Dim g As Widgets.Gadget",
            "g.Mode = wgOn",
            "Dim found As Part",
            "Set found = g.Find(\"x\")",
            "End Sub"),
            References(new InMemoryLibrarySource(Widgets), "Widgets"));

        Assert.IsEmpty(errors, string.Join("; ", errors));
    }

    [TestMethod]
    public async Task AMemberOfAClassThatNamesAClassNotReadYet_OrOneTheLibraryDoesNotDeclare_IsLateBound_NotWrong()
    {
        // `Find` returns a Part, which a Gadget names before the Part was read: what is known of the result is not its type, and an operator applied to it is
        // applied to what it turns out to be, as it is to a member that an extensible class has and does not declare.
        var errors = await LoadErrorsAsync([], Program(
            "Public Sub Use()",
            "Dim g As Widgets.Gadget",
            "Debug.Print g.Find(\"x\").Owner.Mode + 1",
            "Debug.Print g.Undeclared & \"!\"",
            "Debug.Print -g.Find(\"x\").Owner.Mode",
            "End Sub"),
            References(new InMemoryLibrarySource(Widgets), "Widgets"));

        Assert.IsEmpty(errors, string.Join("; ", errors));
    }

    [TestMethod]
    public async Task ALibraryThatIsDependedOn_IsLoadedWithTheOneThatDependsOnIt()
    {
        var errors = await LoadErrorsAsync(
            [], Program("Public A As Panels.Panel", "Public B As Widgets.Gadget"), References(new InMemoryLibrarySource(Widgets, Panels), "Panels"));

        Assert.IsEmpty(errors, string.Join("; ", errors));
    }

    [TestMethod]
    public async Task ATypeOfALibraryTheProjectDoesNotReference_IsNotFound_AndTheErrorSaysAReferenceMayBeMissing()
    {
        var errors = await LoadErrorsAsync([], Program("Public A As Widgets.Gadget", "Public B As Gadget"), References(new InMemoryLibrarySource(Widgets)));

        Assert.HasCount(2, errors);
        StringAssert.Contains(errors[0], string.Format(RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeQualifierNotResolved_Verbose, "Widgets.Gadget", "Widgets"));
        StringAssert.Contains(errors[1], string.Format(RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeNotResolved_Verbose, "Gadget"));
    }

    [TestMethod]
    public async Task ALibraryNothingDescribes_IsAReferenceThatCannotBeResolved_AndItsTypesAreNotFound()
    {
        var errors = await LoadErrorsAsync([], Program("Public App As Excel.Application"), References(new InMemoryLibrarySource(Widgets), "Excel", "Widgets"));

        StringAssert.Contains(errors.Single(), string.Format(RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeQualifierNotResolved_Verbose, "Excel.Application", "Excel"));
    }

    [TestMethod]
    public async Task ALibraryThatIsNotReferenced_IsNotMadeAvailableByTheOnesThatAre()
    {
        var errors = await LoadErrorsAsync([], Program("Public A As Panels.Panel"), References(new InMemoryLibrarySource(Widgets, Panels), "Widgets"));

        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public async Task LibrariesThatDependOnOneAnother_AreRejected_AndTheirTypesAreNotFound()
    {
        var cyclic = new InMemoryLibrarySource(
            new LibraryDescription { Name = "Alpha", DependsOn = ["Beta"], Classes = [new ClassDescription { Name = "Thing" }] },
            new LibraryDescription { Name = "Beta", DependsOn = ["Alpha"], Classes = [new ClassDescription { Name = "Other" }] });

        var errors = await LoadErrorsAsync([], Program("Public A As Alpha.Thing", "Public B As Beta.Other"), References(cyclic, "Alpha"));

        Assert.HasCount(2, errors, "neither is loaded, though both are described");
    }

    [TestMethod]
    public async Task AWorkspaceClass_IsNotAClassOfALibrary_AndTheLibraryQualifiesItsOwn()
    {
        var errors = await LoadErrorsAsync(
            [("Local", ClassModule("Local", "Public Owner As Widgets.Gadget"))],
            Program("Public A As Local", "Public B As Widgets.Gadget", "Public C As Widgets.Local"),
            References(new InMemoryLibrarySource(Widgets), "Widgets"));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "Widgets.Local");
    }
}
