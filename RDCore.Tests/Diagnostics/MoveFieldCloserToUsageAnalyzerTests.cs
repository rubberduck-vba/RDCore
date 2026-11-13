using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Workspace;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00413</c>: a variable of the module whose every reference is in one procedure: a <c>Private</c> one, and a <c>Public</c> one of a program.
/// </summary>
[TestClass]
public sealed class MoveFieldCloserToUsageAnalyzerTests
{
    private static readonly string Source = ModuleWorkspace.ClassModule("Source", "Public Event Changed()", "Public Sub Raise()", "RaiseEvent Changed", "End Sub");

    // the names of the variables found in the Counter class, and the procedures named in the messages.
    private static async Task<(string Name, string Procedure)[]> FoundAsync(params string[] body)
    {
        var counter = ModuleWorkspace.ClassModule("Counter", body);
        var program = ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "Dim c As New Counter", "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([("Source", Source), ("Counter", counter)], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Counter");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Counter.cls"));

        var findings = new MoveFieldCloserToUsageAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, counter), model)).ToArray();
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.MoveFieldCloserToUsage && finding.Severity == DiagnosticSeverity.Information));
        return [.. findings.Select(finding => finding.Message.Split('\'')).Select(parts => (parts[1], parts[3]))];
    }

    [TestMethod]
    public async Task AVariableOneProcedureUses_IsFound()
        => CollectionAssert.AreEqual(
            new[] { ("mTotal", "Sum") },
            await FoundAsync("Private mTotal As Long", "Private mShared As Long",
                "Public Function Sum() As Long", "mTotal = 0", "mTotal = mTotal + mShared", "Sum = mTotal", "End Function",
                "Public Sub Bump()", "mShared = mShared + 1", "End Sub"));

    [TestMethod]
    public async Task ABackingVariableOfAProperty_UsedByItsGetAndItsLet_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("Private mValue As Long",
            "Public Property Get Value() As Long", "Value = mValue", "End Property",
            "Public Property Let Value(ByVal v As Long)", "mValue = v", "End Property"));

    [TestMethod]
    public async Task AWithEventsVariable_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("Private WithEvents mSource As Source",
            "Private Sub Class_Initialize()", "Set mSource = New Source", "End Sub",
            "Private Sub mSource_Changed()", "End Sub"));

    [TestMethod]
    public async Task AVariableNoCodeUses_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("Private mUnused As Long", "Public Sub Work()", "End Sub"));

    // the names of the variables found in the named module, and the procedures named in the messages: a standard module of the project, or a class module.
    private static async Task<(string Name, string Procedure)[]> FoundInAsync(
        string name, string program, IReadOnlyList<(string Name, string Source)> classes, IReadOnlyList<(string Name, string Source)>? modules = null,
        ProjectBuild? build = null)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(classes, program, modules: modules, build: build);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == name);
        var isClass = classes.Any(module => module.Name == name);
        var source = (isClass ? classes : modules!).Single(module => module.Name == name).Source;
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, $"{name}.{(isClass ? "cls" : "bas")}"));

        var findings = new MoveFieldCloserToUsageAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model));
        return [.. findings.Select(finding => finding.Message.Split('\'')).Select(parts => (parts[1], parts[3]))];
    }

    private static readonly (string, string)[] Globals =
    [
        ("Globals", ModuleWorkspace.StandardModule("Globals",
            "Option Explicit",
            "Public gCount As Long",
            "Public gTotal As Long",
            "Global gShared As Long",
            "Public Sub Add()", "gTotal = gTotal + 1", "End Sub",
            "Public Sub Bump()", "gShared = 1", "End Sub")),
    ];

    private static readonly string Tallies = ModuleWorkspace.StandardModule("Program",
        "Public Sub Main()", "Tally", "Add", "Bump", "Debug.Print gShared", "End Sub",
        "Public Sub Tally()", "gCount = gCount + 1", "End Sub");

    // a program starts at its entry point: no other project, nor any host, uses its Public variables, and the code of its modules is all that can.
    [TestMethod]
    public async Task APublicVariableOfAProgramsModule_ThatOneProcedureUses_IsFound()
        => CollectionAssert.AreEqual(new[] { ("gCount", "Program.Tally"), ("gTotal", "Add") }, await FoundInAsync("Globals", Tallies, [], Globals),
            "one used by a procedure of another module, named with its module, and one used by a procedure of its own; not one two procedures use");

    [TestMethod]
    public async Task APublicVariableOfAProgramsClass_IsFound_OnlyWhereTheClassUsesIt()
    {
        (string, string) item = ("Item", ModuleWorkspace.ClassModule("Item",
            "Public Tag As String", "Public Count As Long", "Public Sub Touch()", "Count = Count + 1", "End Sub"));
        var program = ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "Dim i As New Item", "i.Tag = \"a\"", "i.Touch", "End Sub");

        CollectionAssert.AreEqual(new[] { ("Count", "Touch") }, await FoundInAsync("Item", program, [item]),
            "not Tag, which Main uses as a member of an object");
    }

    [TestMethod]
    public async Task APublicVariableOfALibrary_OrOfAProjectWithNoEntryPoint_IsNotFound()
    {
        var noEntryPoint = ModuleWorkspace.StandardModule("Program", "Public Sub Run()", "Add", "End Sub");

        Assert.IsEmpty(await FoundInAsync("Globals", noEntryPoint, [], Globals), "the host that runs the project can use it");
        Assert.IsEmpty(await FoundInAsync("Globals", Tallies, [], Globals, new ProjectBuild { OutputType = ProjectOutputType.Library }),
            "a referencing project can use it");
    }
}
