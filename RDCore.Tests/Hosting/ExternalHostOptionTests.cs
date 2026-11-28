using RDCore.External.Client;
using RDCore.Runtime.Composer;
using RDCore.Runtime.Hosting;
using RDCore.SDK.Runtime.Hosting;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Libraries;
using static RDCore.Tests.Composer.ComposedProject;

namespace RDCore.Tests.Hosting;

/// <summary>
/// The external host a program's machines make their forms in, which the host names: for a program composed from its files
/// (<see cref="ProgramComposer.ComposeAsync"/>) and for one loaded from its composition (<see cref="ComposedProgramFile.LoadAsync"/>). An RDForms form is
/// made with its window, which a desktop with no display cannot open: the rows run on Windows and macOS.
/// </summary>
[TestClass]
[TestCategory("External host")]
[OSCondition(OperatingSystems.Windows | OperatingSystems.OSX)]
public sealed class ExternalHostOptionTests
{
    private const string ComposedFromItsFiles = "composed from its files";
    private const string LoadedFromItsComposition = "loaded from its composition";

    private static readonly string Form = string.Join("\r\n",
        "VERSION 5.00",
        "Begin VB.Form Form1 ",
        "   Caption         =   \"Form1\"",
        "   ClientHeight    =   3195",
        "End",
        "Attribute VB_Name = \"Form1\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = False",
        "Attribute VB_PredeclaredId = True",
        "Attribute VB_Exposed = False",
        "Option Explicit",
        string.Empty);

    // the caption of a form the machine made, or the number of the error that kept it from making one.
    private static readonly string Program = ModuleWorkspace.StandardModule("Program",
        "Option Explicit",
        "Public Function Made() As String",
        "On Error Resume Next",
        "Dim f As Form1",
        "Set f = New Form1",
        "Load f",
        "If Err.Number <> 0 Then",
        "Made = CStr(Err.Number)",
        "Exit Function",
        "End If",
        "Made = f.Caption",
        "Unload f",
        "End Function");

    private string _root = string.Empty;
    private string _host = string.Empty;

    private static IEnumerable<object[]> Programs => [[ComposedFromItsFiles], [LoadedFromItsComposition]];

    [TestInitialize]
    public void CreateFolders() => (_root, _host) = (NewFolder(), NewFolder());

    [TestCleanup]
    public void DeleteFolders()
    {
        Directory.Delete(_root, recursive: true);
        Directory.Delete(_host, recursive: true);
    }

    // the project of a form and the module that makes it, whose libraries are the repository's; a program of it, given the external host.
    private async Task<IComposedProgram> ProgramAsync(string how, string externalHost)
    {
        File.WriteAllText(Path.Combine(_root, "Program.bas"), Program);
        File.WriteAllText(Path.Combine(_root, "Form1.frm"), Form);
        var project = Path.Combine(_root, ".rdproj");
        File.WriteAllText(project, """
            { "Version": "0.1.0", "ProjectInfo": { "Name": "Forms", "Modules": [ { "RelativeUri": "Program.bas" }, { "RelativeUri": "Form1.frm" } ],
              "References": [ { "Name": "VB" }, { "Name": "VBRUN" } ] } }
            """);

        if (how is ComposedFromItsFiles)
        {
            return await ProgramComposer.ComposeAsync(
                project, new ProgramCompositionOptions { LibrariesFolder = ShippedLibrariesTests.Symbols, ExternalHost = externalHost });
        }

        var composed = await ProgramComposer.ComposeImageAsync(
            project, new ProgramCompositionOptions { LibrariesFolder = ShippedLibrariesTests.Symbols }, default);
        composed.Loaded.Dispose();
        var file = Path.Combine(_host, "Forms" + ComposedProgramFile.Extension);
        await File.WriteAllTextAsync(file, composed.Image.ToJson());
        return await ComposedProgramFile.LoadAsync(
            file, new ComposedProgramOptions { LibrariesFolder = ShippedLibrariesTests.Symbols, ExternalHost = externalHost });
    }

    // rdc, run in external mode, makes the machine's form.
    [TestMethod]
    [DynamicData(nameof(Programs))]
    public async Task AMachine_MakesItsFormInTheExternalHostItIsGiven(string how)
    {
        await using var program = await ProgramAsync(how, ExternalHost.DefaultExecutable);
        await using var machine = await program.CreateMachineAsync(default);

        AssertReturned("\"Form1\"", await Invoke(machine, "Made"));
    }

    // an external host that cannot be started makes no form, as where there is no form host (429), though rdc is beside the host's assemblies.
    [TestMethod]
    [DynamicData(nameof(Programs))]
    public async Task AMachineWhoseExternalHostCannotStart_MakesNoForm(string how)
    {
        Assert.IsTrue(File.Exists(ExternalHost.DefaultExecutable), "rdc is beside the tests");

        await using var program = await ProgramAsync(how, Path.Combine(_host, OperatingSystem.IsWindows() ? "missing.exe" : "missing"));
        await using var machine = await program.CreateMachineAsync(default);

        AssertReturned("\"429\"", await Invoke(machine, "Made"));
    }
}
