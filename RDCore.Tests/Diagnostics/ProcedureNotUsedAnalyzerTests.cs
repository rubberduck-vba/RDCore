using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC01025</c>: a procedure or a property that no code calls, its own calls aside, as the host counts the references over the project.
/// </summary>
[TestClass]
public sealed class ProcedureNotUsedAnalyzerTests
{
    // a program whose Main calls Helpers' Used and Worker's Work. A row's lines go first in its module (Helpers or Worker), and its main lines at the end of
    // Main, after Dim w As New Worker. TheFixture_ReportsNothing shows the fixture alone has no finding.
    private sealed record Project(
        string[] Helpers, string[] Worker, string Program, (string Name, string Source)[] Classes, (string Name, string Source)[] Modules);

    private static Project ProjectOf(string module = "", string lines = "", string main = "", bool hasEntryPoint = true)
    {
        string[] Rows(string name) => name == module ? lines.Split('|', StringSplitOptions.RemoveEmptyEntries) : [];
        string[] helpers = [.. Rows("Helpers"), "Public Sub Used()", "End Sub"];
        string[] worker = [.. Rows("Worker"), "Public Sub Work()", "End Sub"];
        var program = ModuleWorkspace.StandardModule("Program",
            [hasEntryPoint ? "Public Sub Main()" : "Public Sub Run()", "Used", "Dim w As New Worker", "w.Work",
                .. main.Split('|', StringSplitOptions.RemoveEmptyEntries), "End Sub"]);
        return new Project(helpers, worker, program,
            [
                ("IShape", ModuleWorkspace.ClassModule("IShape", "Public Sub Draw()", "End Sub")),
                ("Button", ModuleWorkspace.ClassModule("Button", "Public Event Click()", "Public Sub Press()", "RaiseEvent Click", "End Sub")),
                ("Worker", ModuleWorkspace.ClassModule("Worker", worker)),
            ],
            [("Helpers", ModuleWorkspace.StandardModule("Helpers", helpers))]);
    }

    private static Task<(AnalyzerFinding[] Findings, string Source)> FindingsAsync(Project project, string module, ProjectBuild? build = null)
        => FindingsAsync(new ProcedureNotUsedAnalyzer(), RDCoreDiagnosticId.ProcedureNotUsed, project, module, build);

    private static async Task<(AnalyzerFinding[] Findings, string Source)> FindingsAsync(
        IModuleAnalyzer analyzer, RDCoreDiagnosticId id, Project project, string module, ProjectBuild? build = null)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(project.Classes, project.Program, modules: project.Modules, build: build);
        var model = payload.Modules.Single(semantics => semantics.Module.Fragment.TrimStart('#') == module);
        var isClass = module == "Worker";
        var source = module == "Program" ? project.Program : (isClass ? project.Classes : project.Modules).Single(entry => entry.Name == module).Source;
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, $"{module}.{(isClass ? "cls" : "bas")}"));
        var parse = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parse.SyntaxErrors, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var code = id.ToCode();
        return ([.. analyzer.Analyze(new ModuleAnalysisContext(uri, parse, model)).Where(finding => finding.Code == code)], source);
    }

    // the text each finding's range covers: the name of the procedure it reports.
    private static string[] NamesAt(AnalyzerFinding[] findings, string source)
    {
        var lines = source.Split("\r\n");
        return [.. findings.Select(finding => finding.Range.Start.Line == finding.Range.End.Line
            ? lines[finding.Range.Start.Line][finding.Range.Start.Character..finding.Range.End.Character]
            : $"(a range over lines {finding.Range.Start.Line}-{finding.Range.End.Line})")];
    }

    [TestMethod]
    public async Task TheFixture_ReportsNothing()
    {
        foreach (var module in new[] { "Helpers", "Worker", "Program" })
        {
            var (findings, _) = await FindingsAsync(ProjectOf(), module);
            Assert.IsEmpty(findings, $"{module}: Main, Used and Work are called, or are the entry point");
        }
    }

    [TestMethod]
    [DataRow("Helpers", "Private Sub Unused()|End Sub", "Unused", DisplayName = "a Private Sub")]
    [DataRow("Helpers", "Private Function Fact(ByVal n As Long) As Long|If n > 1 Then Fact = n * Fact(n - 1) Else Fact = 1|End Function", "Fact",
        DisplayName = "a function that only calls itself")]
    [DataRow("Helpers", "Public Sub Refresh()|End Sub", "Refresh", DisplayName = "a Public Sub of a program")]
    [DataRow("Helpers", "Private Property Get Size() As Long|End Property|Private Property Let Size(ByVal v As Long)|End Property", "Size",
        DisplayName = "a property, once for its accessors")]
    [DataRow("Worker", "Friend Sub Reset()|End Sub", "Reset", DisplayName = "a Friend Sub of a class")]
    [DataRow("Worker", "Public Sub Reset()|End Sub", "Reset", DisplayName = "a Public Sub of a class of a program")]
    public async Task AProcedureNoCodeCalls_IsReportedAtItsName(string module, string lines, string name)
    {
        var (findings, source) = await FindingsAsync(ProjectOf(module, lines), module);

        CollectionAssert.AreEqual(new[] { name }, NamesAt(findings, source));
        Assert.AreEqual(DiagnosticSeverity.Information, findings[0].Severity);
    }

    [TestMethod]
    [DataRow("Helpers", "Public Function Fact(ByVal n As Long) As Long|If n > 1 Then Fact = n * Fact(n - 1) Else Fact = 1|End Function", "Debug.Print Fact(3)",
        DisplayName = "a recursion that another procedure calls")]
    [DataRow("Helpers", "Private Sub Hook()|End Sub|Public Sub Arm()|Dim s As String: s = \"Hook\"|End Sub", "Arm",
        DisplayName = "a Private Sub a string names, which a host can call by its name")]
    [DataRow("Worker", "Private Sub Hook()|End Sub|Public Sub Arm()|CallByName Me, \"Ho\" & \"ok\", VbMethod|End Sub", "w.Arm",
        DisplayName = "a Private Sub of a module that calls CallByName with a name no string gives")]
    [DataRow("Worker", "Public Sub Refresh()|End Sub", "Dim o As Object: Set o = w: o.Refresh", DisplayName = "a Public Sub called late")]
    [DataRow("Worker", "Public Property Get Value() As Long|Attribute Value.VB_UserMemId = 0|End Property", "", DisplayName = "the default member")]
    [DataRow("Worker", "Private Sub Class_Initialize()|End Sub", "", DisplayName = "Class_Initialize")]
    [DataRow("Worker", "Private WithEvents mButton As Button|Private Sub mButton_Click()|End Sub", "", DisplayName = "the handler of an event")]
    [DataRow("Worker", "Implements IShape|Private Sub IShape_Draw()|End Sub", "", DisplayName = "the implementation of an interface member")]
    public async Task AProcedureSomethingElseCanCall_IsNotReported(string module, string lines, string main)
    {
        var (findings, source) = await FindingsAsync(ProjectOf(module, lines, main), module);

        Assert.IsEmpty(findings, string.Join(", ", NamesAt(findings, source)));
    }

    // RDC00217 says a function's callers all discard its value; one that only calls itself has no caller, and is RDC01025's alone.
    [TestMethod]
    [DataRow("", "Loop1", "", DisplayName = "only its own calls: not used")]
    [DataRow("Loop1 3", "", "Loop1", DisplayName = "a caller that discards the value, too: always discarded")]
    public async Task AFunctionThatOnlyCallsItself_IsNotUsed_AndOnceCalledAsAStatement_IsAlwaysDiscarded(string go, string notUsed, string discarded)
    {
        var project = ProjectOf("Helpers",
            $"Private Function Loop1(ByVal n As Long) As Long|If n > 0 Then Call Loop1(n - 1)|End Function|Public Sub Go()|{go}|End Sub", "Go");
        var payload = await ModuleWorkspace.SemanticsAsync(project.Classes, project.Program, modules: project.Modules);
        var loop = payload.Modules.Single(model => model.Module.Fragment.TrimStart('#') == "Helpers").Declarations.Single(fact => fact.Name == "Loop1");
        Assert.IsTrue(loop.ReferencedFrom is { IsDefault: false } sites && sites.Any(site => ProcedureNotUsedAnalyzer.IsInItsOwnCode(site, loop.Symbol)),
            "the premise: the host counts Loop1's call of itself; rewrite the test if it no longer does");

        var (unused, source) = await FindingsAsync(project, "Helpers");
        var (alwaysDiscarded, _) = await FindingsAsync(
            new FunctionReturnValueAlwaysDiscardedAnalyzer(), RDCoreDiagnosticId.FunctionReturnValueAlwaysDiscarded, project, "Helpers");

        CollectionAssert.AreEqual(notUsed.Split(',', StringSplitOptions.RemoveEmptyEntries), NamesAt(unused, source));
        CollectionAssert.AreEqual(discarded.Split(',', StringSplitOptions.RemoveEmptyEntries), NamesAt(alwaysDiscarded, source));
    }

    // MS-VBA's host calls a handler of its document's event; the host's class, a library's, declares the event.
    [TestMethod]
    public async Task AHandlerOfAnEventALibraryDeclares_IsNotReported_WhereAProcedureOfItsModuleIs()
    {
        const string source = "Option Explicit\r\nPublic Sub Workbook_Open()\r\nEnd Sub\r\nPrivate Sub Unused()\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "End Sub"),
            libraries: DocumentModuleTests.Excel, documents: [("ThisWorkbook", source, DocClassType.ExcelWorkbook)]);
        var model = payload.Modules.Single(candidate => SDK.Model.Symbols.SymbolUri.ModuleNameOf(candidate.Module) == "ThisWorkbook");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "ThisWorkbook.doccls"));

        var findings = new ProcedureNotUsedAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model)).ToArray();

        CollectionAssert.AreEqual(new[] { "Unused" }, NamesAt(findings, source), "the module's procedures are counted, and Workbook_Open handles an event");
    }

    // the same Public Sub that AProcedureNoCodeCalls_IsReportedAtItsName reports in a program.
    [TestMethod]
    [DataRow(false, false, DisplayName = "a project with no entry point, whose host can call it")]
    [DataRow(true, true, DisplayName = "a library, whose referencing projects can call it")]
    public async Task APublicSubOfAProjectThatIsNotAProgram_IsNotReported(bool hasEntryPoint, bool isLibrary)
    {
        var build = isLibrary ? new ProjectBuild { OutputType = ProjectOutputType.Library } : null;
        var (findings, _) = await FindingsAsync(ProjectOf("Helpers", "Public Sub Refresh()|End Sub", hasEntryPoint: hasEntryPoint), "Helpers", build);

        Assert.IsEmpty(findings);
    }

    [TestMethod]
    public async Task TheFix_RemovesTheProcedure_AndTheProgramRunsAsBefore()
    {
        var project = ProjectOf("Helpers", "Private Sub Unused()|Debug.Print \"never\"|End Sub", "Debug.Print \"ran\"");
        var (findings, source) = await FindingsAsync(project, "Helpers");

        var fix = Assert.ContainsSingle(Assert.ContainsSingle(findings).Fixes!.Edits);
        StringAssert.Contains(fix.Title, "'Unused'");
        var removed = fix.Edits.ApplyTo(source);
        Assert.AreEqual(ModuleWorkspace.StandardModule("Helpers", project.Helpers[3..]), removed);

        (string, string)[] modules = [("Helpers", removed)];
        Assert.IsEmpty(await ModuleWorkspace.LoadErrorsAsync(project.Classes, modules, project.Program));
        var before = await ModuleWorkspace.RunAsync(project.Classes, project.Modules, project.Program);
        CollectionAssert.Contains(before, "ran");
        CollectionAssert.AreEqual(before, await ModuleWorkspace.RunAsync(project.Classes, modules, project.Program));
    }

    [TestMethod]
    public async Task TheFix_RemovesEveryAccessorOfAProperty()
    {
        var project = ProjectOf("Helpers", "Private Property Get Size() As Long|End Property|Private Property Let Size(ByVal v As Long)|End Property");
        var (findings, source) = await FindingsAsync(project, "Helpers");

        var fix = Assert.ContainsSingle(Assert.ContainsSingle(findings).Fixes!.Edits);
        Assert.AreEqual(ModuleWorkspace.StandardModule("Helpers", project.Helpers[4..]), fix.Edits.ApplyTo(source));
    }

    [TestMethod]
    public async Task TheFix_IsNotOffered_WhenALineOfTheProcedureHoldsSomethingElse()
    {
        var (findings, _) = await FindingsAsync(ProjectOf("Helpers", "Private Sub Unused()|End Sub ' kept"), "Helpers");

        Assert.IsNull(Assert.ContainsSingle(findings).Fixes, "removing the lines would remove the comment");
    }
}
