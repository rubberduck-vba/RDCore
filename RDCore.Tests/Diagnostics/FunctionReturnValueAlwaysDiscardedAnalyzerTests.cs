using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Workspace;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00217</c>: a <c>Function</c> whose calls the host counts (a <c>Friend</c> one, a <c>Public</c> one of a program) that every call of the project
/// calls as a statement, so that no caller uses the value it returns.
/// </summary>
[TestClass]
public sealed class FunctionReturnValueAlwaysDiscardedAnalyzerTests
{
    private static readonly string Counter = ModuleWorkspace.ClassModule("Counter",
        "Private mTotal As Long",
        "Friend Function Add(ByVal n As Long) As Long", "mTotal = mTotal + n", "Add = mTotal", "End Function",
        "Friend Function Total() As Long", "Total = mTotal", "End Function",
        "Friend Sub Reset()", "mTotal = 0", "End Sub",
        "Friend Function Unused() As Long", "End Function");

    // the names the findings in the Counter module are about, and the lines they are on.
    private static async Task<(string Name, int Line)[]> FoundAsync(params string[] main)
    {
        var program = ModuleWorkspace.StandardModule("Program", ["Public Sub Main()", "Dim c As New Counter", .. main, "End Sub"]);
        var payload = await ModuleWorkspace.SemanticsAsync([("Counter", Counter)], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Counter");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Counter.cls"));

        var findings = new FunctionReturnValueAlwaysDiscardedAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, Counter), model)).ToArray();
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.FunctionReturnValueAlwaysDiscarded && finding.Severity == DiagnosticSeverity.Information));
        return [.. findings.Select(finding => (finding.Message.Split('\'')[1], finding.Range.Start.Line))];
    }

    [TestMethod]
    public async Task AFunctionEveryCallDiscards_IsFound_WhereItIsDeclared()
        => CollectionAssert.AreEqual(new[] { ("Add", 6) }, await FoundAsync("c.Add 1", "Call c.Add(2)", "Debug.Print c.Total", "c.Reset"));

    [TestMethod]
    public async Task AFunctionWhoseValueOneCallUses_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("c.Add 1", "Debug.Print c.Add(2)", "Debug.Print c.Total"));

    [TestMethod]
    public async Task AFunctionNoCodeCalls_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("Debug.Print c.Add(1)", "Debug.Print c.Total"));

    // the names the findings in the named module are about, and the lines they are on: a standard module of the project, or a class module.
    private static async Task<(string Name, int Line)[]> FoundInAsync(
        string name, string program, IReadOnlyList<(string Name, string Source)> classes, IReadOnlyList<(string Name, string Source)>? modules = null,
        ProjectBuild? build = null)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(classes, program, modules: modules, build: build);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == name);
        var isClass = classes.Any(module => module.Name == name);
        var source = (isClass ? classes : modules!).Single(module => module.Name == name).Source;
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, $"{name}.{(isClass ? "cls" : "bas")}"));

        var findings = new FunctionReturnValueAlwaysDiscardedAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model));
        return [.. findings.Select(finding => (finding.Message.Split('\'')[1], finding.Range.Start.Line))];
    }

    private static readonly (string, string)[] Journal =
    [
        ("Journal", ModuleWorkspace.StandardModule("Journal",
            "Option Explicit",
            "Public Function Note(ByVal s As String) As Boolean", "Note = True", "End Function",
            "Function Flush() As Boolean", "Flush = True", "End Function",
            "Public Function Count() As Long", "End Function",
            "Private Function Save() As Boolean", "Save = True", "End Function",
            "Public Sub Finish()", "Save", "End Sub")),
    ];

    private static readonly string NotesAndCounts = ModuleWorkspace.StandardModule("Program",
        "Public Sub Main()", "Note \"x\"", "Call Flush", "Debug.Print Count", "Journal.Note \"y\"", "Finish", "End Sub");

    // a program starts at its entry point: no other project, nor any host, calls its Public functions, and the code of its modules is all that can.
    [TestMethod]
    public async Task APublicFunctionOfAProgram_EveryCallDiscards_IsFound()
        => CollectionAssert.AreEqual(new[] { ("Note", 2), ("Flush", 5) }, await FoundInAsync("Journal", NotesAndCounts, [], Journal),
            "Note, called twice, and Flush, a function with no access modifier, which is Public; not Count, whose value Main prints");

    [TestMethod]
    public async Task APublicFunctionOfAProgramsClass_EveryCallDiscards_IsFound()
    {
        (string, string) document = ("Document", ModuleWorkspace.ClassModule("Document",
            "Public Function Save() As Boolean", "Save = True", "End Function", "Public Function Title() As String", "End Function"));
        var program = ModuleWorkspace.StandardModule("Program",
            "Public Sub Main()", "Dim d As New Document", "d.Save", "Debug.Print d.Title", "End Sub");

        CollectionAssert.AreEqual(new[] { ("Save", 5) }, await FoundInAsync("Document", program, [document]));
    }

    [TestMethod]
    public async Task APublicFunctionOfALibrary_OrOfAProjectWithNoEntryPoint_IsNotFound()
    {
        var noEntryPoint = ModuleWorkspace.StandardModule("Program", "Public Sub Run()", "Note \"x\"", "Call Flush", "End Sub");

        Assert.IsEmpty(await FoundInAsync("Journal", noEntryPoint, [], Journal), "the host that runs the project can call it");
        Assert.IsEmpty(await FoundInAsync("Journal", NotesAndCounts, [], Journal, new ProjectBuild { OutputType = ProjectOutputType.Library }),
            "a referencing project can call it");
    }

    [TestMethod]
    public async Task AFunctionOfAProgramsClass_ThatAnAccessBoundLateNames_IsNotFound()
    {
        (string, string) document = ("Document", ModuleWorkspace.ClassModule("Document", "Public Function Save() As Boolean", "Save = True", "End Function"));
        var program = ModuleWorkspace.StandardModule("Program",
            "Public Sub Main()", "Dim d As New Document", "d.Save", "Dim o As Object", "Set o = d", "Debug.Print o.Save", "End Sub");

        Assert.IsEmpty(await FoundInAsync("Document", program, [document]), "the access bound late may use its value");
    }

    [TestMethod]
    [Ignore("The host does not count the calls of a Private procedure: ReferencedFrom is not stated for one.")]
    public async Task APrivateFunctionEveryCallDiscards_IsFound()
        => CollectionAssert.Contains(await FoundInAsync("Journal", NotesAndCounts, [], Journal), ("Save", 10));
}
