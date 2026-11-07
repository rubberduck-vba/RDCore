using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>RD-VBAL §2.3.1.3</strong> a VB6 project references the Visual Basic runtime objects and procedures (<c>VBRUN</c>) without its project file listing them,
/// as every VB6 project does: the constants of its forms and controls, and its <c>PropertyBag</c>, are names of the project. So are the Visual Basic objects and
/// procedures (<c>VB</c>): its forms and controls. A VBA project has neither reference.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.3.1.3 Name Resolution")]
public sealed class VisualBasicRuntimeLibraryTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n";

    private static string Symbols
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

    // the libraries that ship (Symbols/*.json), of which the project lists none.
    private static WorkspaceLibraries NoneListed => new([], new DirectoryLibrarySource(new FileSystem(), Symbols));

    // names only the runtime declares, none of them the standard library's.
    private static readonly string[] UsesTheRuntime =
    [
        "Dim bag As PropertyBag",
        "Debug.Print vbFixedSingle; vbTwips; vbHourglass; vbChecked; vbShiftMask; vbLeftButton; VBRUN.vbModal",
        "Debug.Print bag Is Nothing",
    ];

    private static async Task<ExecuteResult> RunVB6Async(params string[] lines)
    {
        ExecuteResult? ran = null;
        await InspectAsync([], Program(lines), (_, result, _) =>
        {
            ran = result;
            return Task.CompletedTask;
        }, libraries: NoneListed, language: SupportedLanguages.VB6);

        Assert.AreEqual(ExecutionOutcome.Completed, ran!.Outcome, $"{ran.ErrorMessage} {string.Join("; ", ran.Diagnostics)}");
        return ran;
    }

    [TestMethod]
    public async Task AVB6Project_ThatListsNoReference_RunsWithTheNamesOfTheVisualBasicRuntime()
    {
        var ran = await RunVB6Async(UsesTheRuntime);

        CollectionAssert.AreEqual(new[] { "1  1  11  1  1  1  1", "True" }, ran.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task AVB6Project_ANameOfBothTheStandardLibraryAndTheRuntime_IsTheStandardLibrarys()
        // VBRUN's DragConstants.vbCancel is 0 and its vbKeyEscape a Long; VBA comes first, as the VB6 References dialog lists it.
    {
        var ran = await RunVB6Async("Debug.Print vbCancel; TypeName(vbKeyEscape)");

        CollectionAssert.AreEqual(new[] { "2 Integer" }, ran.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task AVBAProject_ThatListsNoReference_DoesNotHaveTheVisualBasicRuntime()
    {
        var errors = await LoadErrorsAsync([], Program(UsesTheRuntime), NoneListed);

        Assert.IsTrue(errors.Any(error => error.Contains("PropertyBag")), $"PropertyBag is a name of VB6's runtime, which a VBA project does not reference: {string.Join("; ", errors)}");
    }

    [TestMethod]
    public void TheVisualBasicRuntime_ComesAfterTheStandardLibrary_AndBeforeTheReferencesTheProjectLists()
    {
        var source = new DirectoryLibrarySource(new FileSystem(), Symbols);

        var libraries = LibrarySymbolProvider.Load(source, [new RDCoreReference { Name = "Scripting" }], SupportedLanguages.VB6);

        Assert.IsEmpty(libraries.Problems, string.Join("; ", libraries.Problems.Select(problem => $"{problem.Name}: {problem.Kind}")));
        Assert.AreEqual(1, libraries.Priorities["VBRUN"], "the first library after the standard library, which is not in the map");
        Assert.IsLessThan(libraries.Priorities["Scripting"], libraries.Priorities["VBRUN"], "the language's own reference ranks above the ones the project lists");
        Assert.IsGreaterThan(libraries.Priorities["Scripting"], libraries.Priorities["MSDATASRC"], "a library the project references ranks above one it depends on");
    }

    [TestMethod]
    public void AVBAProject_LoadsOnlyTheReferencesItLists()
    {
        var source = new DirectoryLibrarySource(new FileSystem(), Symbols);

        var libraries = LibrarySymbolProvider.Load(source, [new RDCoreReference { Name = "Scripting" }], SupportedLanguages.RDVBA);

        Assert.IsFalse(libraries.Priorities.ContainsKey("VBRUN"));
        Assert.IsFalse(libraries.Priorities.ContainsKey("VB"));
    }

    // the forms and controls of VB6.OLB.
    private static readonly string[] UsesTheObjects =
    [
        "Dim button As VB.CommandButton",
        "Dim box As TextBox",
        "Dim f As Form",
        "Dim d As Data",
        "Debug.Print TypeName(button); \",\"; TypeName(box); \",\"; TypeName(f); \",\"; TypeName(d)",
    ];

    [TestMethod]
    public async Task AVB6Project_ThatListsNoReference_HasTheFormsAndControlsOfVisualBasic()
    {
        var ran = await RunVB6Async(UsesTheObjects);

        CollectionAssert.AreEqual(new[] { "Nothing,Nothing,Nothing,Nothing" }, ran.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task AVBAProject_ThatListsNoReference_DoesNotHaveTheFormsAndControlsOfVisualBasic()
    {
        var errors = await LoadErrorsAsync([], Program(UsesTheObjects), NoneListed);

        Assert.IsTrue(errors.Any(error => error.Contains("CommandButton")), string.Join("; ", errors));
    }

    private static string[] RecordsetMembers(ReferencedLibraries libraries)
        => [.. libraries.Libraries.Single(library => library.Name == "DAO").Classes.Single(declared => declared.Name == "Recordset").Members.Select(member => member.Name)];

    // VB6.OLB is built against the DAO 2.5/3.51 compatibility library: its Data control's Recordset is that library's, which still has ListFields.
    [TestMethod]
    public void TheDataControlsRecordset_IsTheDaoLibraryVisualBasicIsBuiltAgainst_WhenTheProjectReferencesNoDao()
    {
        var source = new DirectoryLibrarySource(new FileSystem(), Symbols);

        var libraries = LibrarySymbolProvider.Load(source, [], SupportedLanguages.VB6);

        Assert.IsEmpty(libraries.Problems, string.Join("; ", libraries.Problems.Select(problem => $"{problem.Name}: {problem.Kind} {string.Join(", ", problem.Related)}")));
        Assert.AreEqual("{00025E04-0000-0000-C000-000000000046}", libraries.Libraries.Single(library => library.Name == "DAO").Origin?.Guid);
        CollectionAssert.Contains(RecordsetMembers(libraries), "ListFields");
    }

    // one library of a name: the project's DAO 3.6 reference is the DAO the Data control's Recordset is of.
    [TestMethod]
    public void TheDataControlsRecordset_IsTheDaoLibraryTheProjectReferences()
    {
        var source = new DirectoryLibrarySource(new FileSystem(), Symbols);
        var dao = new RDCoreReference { Name = "DAO", Guid = new Guid("00025E01-0000-0000-C000-000000000046"), Major = 5, Minor = 0 };

        var libraries = LibrarySymbolProvider.Load(source, [dao], SupportedLanguages.VB6);

        Assert.IsEmpty(libraries.Problems, string.Join("; ", libraries.Problems.Select(problem => $"{problem.Name}: {problem.Kind} {string.Join(", ", problem.Related)}")));
        var loaded = libraries.Libraries.Where(library => library.Name == "DAO").ToArray();
        Assert.HasCount(1, loaded);
        Assert.AreEqual("5.0", loaded[0].Origin?.Version);
        CollectionAssert.DoesNotContain(RecordsetMembers(libraries), "ListFields");
    }

    // a library is loaded with what it depends on or not at all, and the project's reference is the DAO that VB depends on: one that cannot be found
    // takes VB with it.
    [TestMethod]
    public void AReferenceToADaoTheSourceDoesNotHave_IsADaoVisualBasicCannotBeLoadedWith()
    {
        var source = new DirectoryLibrarySource(new FileSystem(), Symbols);
        var dao = new RDCoreReference { Name = "DAO", Guid = new Guid("00025E01-0000-0000-C000-000000000046"), Major = 6, Minor = 0 };

        var libraries = LibrarySymbolProvider.Load(source, [dao], SupportedLanguages.VB6);

        CollectionAssert.AreEquivalent(
            new[] { "DAO NotFound", "VB DependencyFailed" },
            libraries.Problems.Select(problem => $"{problem.Name} {problem.Kind}").ToArray());
        Assert.IsFalse(libraries.Priorities.ContainsKey("VB"));
        Assert.IsTrue(libraries.Priorities.ContainsKey("VBRUN"));
    }
}
