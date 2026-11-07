using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>RD-VBAL §2.3.1.3</strong> a VB6 project references the Visual Basic runtime objects and procedures (<c>VBRUN</c>) without its project file listing them,
/// as every VB6 project does: the constants of its forms and controls, and its <c>PropertyBag</c>, are names of the project. A VBA project has no such reference.
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

    // as vb6.exe builds and runs it: the unqualified name is the standard library's, and each library's own is its qualified name.
    [TestMethod]
    public async Task AVB6Project_ANameOfBothLibraries_IsEachLibrarysByItsQualifiedName()
    {
        var ran = await RunVB6Async("Debug.Print vbCancel & \",\" & TypeName(vbCancel) & \",\" & VBA.vbCancel & \",\" & VBRUN.vbCancel");

        CollectionAssert.AreEqual(new[] { "2,Long,2,0" }, ran.Output.ToArray());
    }

    // the enumerations are VBRUN's: VB6's VBA declares its key codes and colors as constants of a module, which is no type.
    [TestMethod]
    public async Task AVB6Project_AVariableOfAnEnumerationOfTheRuntime_ByItsQualifiedName_IsALong()
    {
        var ran = await RunVB6Async("Dim v As VBRUN.KeyCodeConstants", "v = 27", "Debug.Print TypeName(v)");

        CollectionAssert.AreEqual(new[] { "Long" }, ran.Output.ToArray());
    }

    // in the type binding context a procedural module of a referenced library ranks before a type of one (MS-VBAL §5.6.10), so the unqualified name is VBA's
    // module, and no type. vb6.exe builds each of these, the name being VBRUN's enumeration, a Long.
    [TestMethod]
    [DataRow("KeyCodeConstants")]
    [DataRow("ColorConstants")]
    [DataRow("SystemColorConstants")]
    public async Task AVB6Project_AVariableOfTheUnqualifiedNameOfAModuleOfTheStandardLibrary_IsRefused(string type)
    {
        var errors = await LoadErrorsAsync([], Program($"Dim v As {type}", "v = 5"), NoneListed, SupportedLanguages.VB6);

        Assert.IsTrue(errors.Any(error => error.Contains($"'{type}' is a procedural module of a library, not a type")), string.Join("; ", errors));
    }

    [TestMethod]
    public async Task TheKeyCodesOfTheStandardLibrary_AreConstantsOfAModule_AndNoType()
    {
        var errors = await LoadErrorsAsync([], Program("Dim k As VBA.KeyCodeConstants", "k = 27"), NoneListed);

        Assert.IsTrue(errors.Any(error => error.Contains("KeyCodeConstants")), string.Join("; ", errors));
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
    }
}
