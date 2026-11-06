using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>RD-VBAL §2.3.1.2</strong> a VB6 project references the Visual Basic runtime objects and procedures (<c>VBRUN</c>) without its project file listing them,
/// as every VB6 project does: the constants of its forms and controls, and its <c>PropertyBag</c>, are names of the project. A VBA project has no such reference.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.3.1.2 References")]
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

    private static readonly string[] UsesTheRuntime =
    [
        "Dim bag As PropertyBag",
        "Debug.Print vbModal; vbKeyEscape; vbFormControlMenu; VBRUN.vbModal",
        "Debug.Print bag Is Nothing",
    ];

    [TestMethod]
    public async Task AVB6Project_ThatListsNoReference_RunsWithTheNamesOfTheVisualBasicRuntime()
    {
        ExecuteResult? ran = null;
        await InspectAsync([], Program(UsesTheRuntime), (_, result, _) =>
        {
            ran = result;
            return Task.CompletedTask;
        }, libraries: NoneListed, language: SupportedLanguages.VB6);

        Assert.AreEqual(ExecutionOutcome.Completed, ran!.Outcome, $"{ran.ErrorMessage} {string.Join("; ", ran.Diagnostics)}");
        CollectionAssert.AreEqual(new[] { "1  27  0  1", "True" }, ran.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task AVBAProject_ThatListsNoReference_DoesNotHaveTheVisualBasicRuntime()
    {
        var errors = await LoadErrorsAsync([], Program(UsesTheRuntime), NoneListed);

        Assert.IsNotEmpty(errors, "PropertyBag and vbModal are names of VB6's runtime, which a VBA project does not reference");
    }

    [TestMethod]
    public void TheVisualBasicRuntime_ComesAfterTheStandardLibrary_AndBeforeTheReferencesTheProjectLists()
    {
        var source = new DirectoryLibrarySource(new FileSystem(), Symbols);

        var libraries = LibrarySymbolProvider.Load(source, [new RDCoreReference { Name = "Scripting" }], SupportedLanguages.VB6);

        Assert.IsEmpty(libraries.Problems, string.Join("; ", libraries.Problems.Select(problem => $"{problem.Name}: {problem.Kind}")));
        Assert.IsGreaterThan(libraries.Priorities["VBRUN"], libraries.Priorities["Scripting"], "a reference the project lists ranks above the language's own");
        Assert.IsGreaterThan(libraries.Priorities["MSDATASRC"], libraries.Priorities["VBRUN"], "a library the project references ranks above one it depends on");
    }

    [TestMethod]
    public void AVBAProject_LoadsOnlyTheReferencesItLists()
    {
        var source = new DirectoryLibrarySource(new FileSystem(), Symbols);

        var libraries = LibrarySymbolProvider.Load(source, [new RDCoreReference { Name = "Scripting" }], SupportedLanguages.RDVBA);

        Assert.IsFalse(libraries.Priorities.ContainsKey("VBRUN"));
    }
}
