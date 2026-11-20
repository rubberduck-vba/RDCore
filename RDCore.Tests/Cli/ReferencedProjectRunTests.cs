using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.CLI;
using RDCore.CLI.App.Bench;
using RDCore.CLI.App.Commands;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Build;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using RDCore.Tests.Cli.Repl;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>RD-VBAL §2.2.3.2</strong>, <strong>MS-VBAL §4.1</strong>: <c>rdc run</c> loads the projects of the workspace that the project references, as the
/// language server defines them in its session, and runs their code. A reference that names no project of the workspace, or more than one, refuses the run,
/// naming the reference.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.2.3.2 RDCoreReference")]
public sealed class ReferencedProjectRunTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-refrun-ws");

    private static readonly string Greetings = ModuleWorkspace.StandardModule("Greetings", "Option Explicit",
        "Public Function Greeting(ByVal Name As String) As String", "    Greeting = \"ahoy \" & Name", "End Function");

    private static readonly string GridCoord = ModuleWorkspace.ClassModule("GridCoord", "Attribute VB_Exposed = True", "Option Explicit",
        "Public X As Long", "Public Function Moved(ByVal By As Long) As Long", "    Moved = X + By", "End Function");

    private static readonly string Program = ModuleWorkspace.StandardModule("Program", "Option Explicit", "Public Sub Main()",
        "    Debug.Print Greeting(\"crew\")", "    Dim c As GridCoord", "    Set c = New GridCoord", "    c.X = 4", "    Debug.Print c.Moved(3)", "End Sub");

    // what the program prints: the referenced project's function, then its class's.
    internal static readonly string[] Printed = ["ahoy crew", "7"];

    // a workspace under root of the folders the .rdworkspace lists (none: no .rdworkspace), each folder a project: its name, the projects it references,
    // and its modules.
    internal static void WriteWorkspace(MockFileSystem fileSystem, string root, IReadOnlyList<string>? listed,
        params (string Folder, string Name, string[] References, (string File, string Text)[] Modules)[] projects)
    {
        fileSystem.Directory.CreateDirectory(root);
        if (listed is not null)
        {
            fileSystem.File.WriteAllText(Path.Combine(root, WorkspaceFile.FileName),
                $$"""{ "Folders": [ {{string.Join(", ", listed.Select(folder => $"\"{folder}\""))}} ] }""");
        }

        foreach (var (folder, name, references, modules) in projects)
        {
            var path = Path.Combine(root, folder);
            fileSystem.Directory.CreateDirectory(path);
            foreach (var (file, text) in modules)
            {
                fileSystem.File.WriteAllText(Path.Combine(path, file), text);
            }

            var listedModules = string.Join(", ", modules.Select(module => $$"""{ "RelativeUri": "{{module.File}}" }"""));
            var listedReferences = string.Join(", ", references.Select(reference => $$"""{ "Name": "{{reference}}", "IsProject": true }"""));
            fileSystem.File.WriteAllText(Path.Combine(path, ProjectFile.FileName), $$"""
                { "Version": "1.3", "ProjectInfo": { "Name": "{{name}}", "Modules": [ {{listedModules}} ], "References": [ {{listedReferences}} ] } }
                """);
        }
    }

    // NavalConsole, whose Program calls into NavalLib, which it references; and the folders of more projects. The project that runs is under root's
    // NavalConsole.
    internal static void WriteNavalWorkspace(MockFileSystem fileSystem, string root,
        params (string Folder, string Name, string[] References, (string File, string Text)[] Modules)[] more)
        => WriteWorkspace(fileSystem, root, ["NavalConsole", "NavalLib", .. more.Select(each => each.Folder)],
        [
            ("NavalConsole", "NavalConsole", ["NavalLib"], [("Program.bas", Program)]),
            ("NavalLib", "NavalLib", [], [("Greetings.bas", Greetings), ("GridCoord.cls", GridCoord)]),
            .. more,
        ]);

    private static MockFileSystem Naval(params (string Folder, string Name, string[] References, (string File, string Text)[] Modules)[] more)
    {
        var fileSystem = new MockFileSystem();
        WriteNavalWorkspace(fileSystem, Root, more);
        return fileSystem;
    }

    private static async Task<(int Exit, string[] Output, string Error)> RunAsync(MockFileSystem fileSystem)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var runner = new BenchRunner(RuntimeEnvironmentProfile.Default, fileSystem, Substitute.For<IErrorDetailsRecorder>(), NullLoggerFactory.Instance,
            new DirectoryLibrarySource(fileSystem, string.Empty));
        var command = new RunCommand(fileSystem, new ProjectFileLoader(fileSystem), runner, Substitute.For<IErrorDetailsRecorder>(), output, error,
            new ScriptedKeys(interactive: false), Options.Create(new AppOptions()));
        var exit = await command.ExecuteAsync([Path.Combine(Root, "NavalConsole")], CancellationToken.None);
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToArray();
        return (exit, lines, error.ToString());
    }

    [TestMethod]
    public async Task ARunOfAProjectThatReferencesAProjectOfTheWorkspace_RunsTheReferencedProjectsCode()
    {
        var (exit, output, error) = await RunAsync(Naval());

        Assert.AreEqual(0, exit, error);
        CollectionAssert.AreEqual(Printed, output, string.Join("|", output));
    }

    [TestMethod]
    public async Task AReferenceThatNoProjectOfTheWorkspaceHas_RefusesTheRun_NamingTheReference()
    {
        var fileSystem = new MockFileSystem();
        WriteWorkspace(fileSystem, Root, ["NavalConsole"], ("NavalConsole", "NavalConsole", ["NavalLib"], [("Program.bas", Program)]));

        var (exit, output, error) = await RunAsync(fileSystem);

        Assert.AreEqual(ProgramRunner.Refused, exit);
        Assert.IsEmpty(output);
        StringAssert.Contains(error, "The project NavalConsole references the project NavalLib: no project of the workspace has that name.");
    }

    [TestMethod]
    public async Task AReferenceToANameTwoProjectsOfTheWorkspaceHave_RefusesTheRun_NamingTheReference()
    {
        var (exit, output, error) = await RunAsync(Naval(("OldNavalLib", "navallib", [], [("Greetings.bas", Greetings)])));

        Assert.AreEqual(ProgramRunner.Refused, exit);
        Assert.IsEmpty(output);
        StringAssert.Contains(error, "The project NavalConsole references the project NavalLib: more than one project of the workspace has that name");
    }

    // with no .rdworkspace, the folder is a workspace of its one project: a reference to another project names none.
    [TestMethod]
    public async Task AProjectWithNoWorkspaceFile_ThatReferencesAProject_IsRefused()
    {
        var fileSystem = new MockFileSystem();
        WriteWorkspace(fileSystem, Root, listed: null,
            ("NavalConsole", "NavalConsole", ["NavalLib"], [("Program.bas", Program)]),
            ("NavalLib", "NavalLib", [], [("Greetings.bas", Greetings), ("GridCoord.cls", GridCoord)]));

        var (exit, _, error) = await RunAsync(fileSystem);

        Assert.AreEqual(ProgramRunner.Refused, exit);
        StringAssert.Contains(error, "no project of the workspace has that name");
    }

    // RD-VBAL §2.6.1: nothing of the project runs while a module of a project it references does not load; the module is named as of its project.
    [TestMethod]
    public async Task AModuleOfAReferencedProjectThatDoesNotLoad_RefusesTheRun_NamedAsOfItsProject()
    {
        var broken = ModuleWorkspace.StandardModule("Broken", "Option Explicit", "Public Sub Go()", "    Undeclared = 1", "End Sub");
        var fileSystem = new MockFileSystem();
        WriteWorkspace(fileSystem, Root, ["NavalConsole", "NavalLib"],
            ("NavalConsole", "NavalConsole", ["NavalLib"], [("Program.bas", Program)]),
            ("NavalLib", "NavalLib", [], [("Greetings.bas", Greetings), ("GridCoord.cls", GridCoord), ("Broken.bas", broken)]));

        var (exit, output, error) = await RunAsync(fileSystem);

        Assert.AreEqual(ProgramRunner.Refused, exit);
        Assert.IsEmpty(output);
        StringAssert.Contains(error, "NavalLib.Broken: ");
    }

    // MS-VBAL §4.2: a module's name is not the project name of a project its project references.
    [TestMethod]
    public async Task AModuleNamedAsAReferencedProject_DoesNotLoad_AndRefusesTheRun()
    {
        var named = ModuleWorkspace.StandardModule("NavalLib", "Option Explicit", "Public Sub Go()", "End Sub");
        var fileSystem = new MockFileSystem();
        WriteWorkspace(fileSystem, Root, ["NavalConsole", "NavalLib"],
            ("NavalConsole", "NavalConsole", ["NavalLib"], [("Program.bas", Program), ("NavalLib.bas", named)]),
            ("NavalLib", "NavalLib", [], [("Greetings.bas", Greetings), ("GridCoord.cls", GridCoord)]));

        var (exit, output, error) = await RunAsync(fileSystem);

        Assert.AreEqual(ProgramRunner.Refused, exit);
        Assert.IsEmpty(output);
        StringAssert.Contains(error, NameConflicts);
    }

    // the standard library is referenced by every project, a referenced one too.
    [TestMethod]
    public async Task AModuleOfAReferencedProject_NamedAsTheStandardLibrary_RefusesTheRun_NamedAsOfItsProject()
    {
        var named = ModuleWorkspace.StandardModule("VBA", "Option Explicit", "Public Sub Go()", "End Sub");
        var fileSystem = new MockFileSystem();
        WriteWorkspace(fileSystem, Root, ["NavalConsole", "NavalLib"],
            ("NavalConsole", "NavalConsole", ["NavalLib"], [("Program.bas", Program)]),
            ("NavalLib", "NavalLib", [], [("Greetings.bas", Greetings), ("GridCoord.cls", GridCoord), ("VBA.bas", named)]));

        var (exit, output, error) = await RunAsync(fileSystem);

        Assert.AreEqual(ProgramRunner.Refused, exit);
        Assert.IsEmpty(output);
        StringAssert.Contains(error, "NavalLib.VBA: ");
        StringAssert.Contains(error, NameConflicts);
    }

    private const string NameConflicts = "Name conflicts with existing module, project, or object library";
}
