using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00201</c>, as the host models the module: the signature of a member that implements an interface or handles an event is not the module's to choose,
/// and the fix changes the type where nothing else has to change with it.
/// </summary>
[TestClass]
public sealed class IntegerDataTypeAnalyzerTests
{
    private const string Fix = "Change the type from Integer to Long";

    private static readonly (string Name, string Source) Shape
        = ("IShape", ModuleWorkspace.ClassModule("IShape", "Public Function Area(ByVal factor As Integer) As Integer", "End Function"));

    private static readonly (string Name, string Source) Button
        = ("Button", ModuleWorkspace.ClassModule("Button", "Public Event Click(ByVal times As Integer)", "Public Sub Press()", "    RaiseEvent Click(1)", "End Sub"));

    private static readonly string[] Program =
    [
        "Option Explicit",
        "Private Const Size As Integer = 3",
        "Public Sub Main()",
        "    Dim count As Integer",
        "    Dim passed As Integer",
        "    count = Twice(Size)",
        "    Fill passed",
        "    Show Size",
        "    Debug.Print count",
        "    Debug.Print passed",
        "End Sub",
        "Private Function Twice(ByVal n As Long) As Integer",
        "    Twice = n * 2",
        "End Function",
        "Private Sub Fill(ByRef value As Integer)",
        "    value = 1",
        "End Sub",
        "Private Sub Show(ByVal n As Integer)",
        "    Debug.Print n",
        "End Sub",
    ];

    private static async Task<AnalyzerFinding[]> AnalyzeAsync(string name, string source, params (string Name, string Source)[] classes)
    {
        var program = name == "Program" ? source : ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync(name == "Program" ? classes : [.. classes, (name, source)], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == name);
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, name == "Program" ? "Program.bas" : $"{name}.cls"));
        return [.. new IntegerDataTypeAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model)).OrderBy(finding => finding.Range.Start)];
    }

    // the lines of the source that say what is reported, and the lines the fix is offered for.
    private static (int[] Reported, int[] Fixed) Lines(AnalyzerFinding[] findings)
        => ([.. findings.Select(finding => finding.Range.Start.Line)], [.. findings.Where(finding => finding.Fixes is { Edits: [_, ..] }).Select(finding => finding.Range.Start.Line)]);

    private static int LineOf(string source, string text)
        => Array.FindIndex(source.Split("\r\n"), line => line.Contains(text, StringComparison.Ordinal));

    // Rubberduck's Integer inspection skips the parameters and the return type of an interface implementation and of an event handler, and reports their locals.
    [TestMethod]
    public async Task TheSignatureOfAnImplementationOrOfAHandler_IsNotReported_AndItsBodyIs()
    {
        var source = ModuleWorkspace.ClassModule("Widget",
            "Option Explicit",
            "Implements IShape",
            "Private WithEvents source As Button",
            "Private Function IShape_Area(ByVal factor As Integer) As Integer",
            "    Dim local As Integer",
            "    IShape_Area = local * factor",
            "End Function",
            "Private Sub source_Click(ByVal times As Integer)",
            "End Sub");

        var (reported, _) = Lines(await AnalyzeAsync("Widget", source, Shape, Button));

        CollectionAssert.AreEqual(new[] { LineOf(source, "Dim local") }, reported);
    }

    [TestMethod]
    public async Task TheFix_IsOffered_WhereNothingElseHasToChangeWithTheType()
    {
        var source = ModuleWorkspace.StandardModule("Program", Program);

        var (reported, fixedLines) = Lines(await AnalyzeAsync("Program", source));

        CollectionAssert.AreEqual(
            new[] { "Const Size", "Dim count", "Dim passed", "Function Twice", "Sub Fill", "Sub Show" }.Select(text => LineOf(source, text)).ToArray(), reported);
        CollectionAssert.AreEqual(
            new[] { "Const Size", "Dim count", "Function Twice", "Sub Show" }.Select(text => LineOf(source, text)).ToArray(), fixedLines,
            "not a variable passed as an argument, nor a ByRef parameter, which an argument of type Integer is passed to");
    }

    [TestMethod]
    [DataRow("Private Const Size As Integer = 3", "Private Const Size As Long = 3")]
    [DataRow("    Dim count As Integer", "    Dim count As Long")]
    [DataRow("Private Function Twice(ByVal n As Long) As Integer", "Private Function Twice(ByVal n As Long) As Long")]
    [DataRow("Private Sub Show(ByVal n As Integer)", "Private Sub Show(ByVal n As Long)")]
    public async Task TheFix_ChangesTheType_AndBringsNothingTheLanguageServerReports(string line, string changed)
    {
        var source = ModuleWorkspace.StandardModule("Program", Program);
        var finding = (await AnalyzeAsync("Program", source)).Single(finding => finding.Range.Start.Line == LineOf(source, line));
        var fix = finding.Fixes?.Edits?.SingleOrDefault();
        Assert.IsNotNull(fix);
        Assert.AreEqual(Fix, fix.Title);

        var fixedSource = EveryAnalyzer.Applied(source, fix);
        Assert.AreEqual(source.Replace(line, changed, StringComparison.Ordinal), fixedSource);
        await EveryAnalyzer.AssertBringsNothingNewAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task TheFix_IsNotOffered_ForAPublicMemberOfAClass_NorForAPropertyWhoseOtherProceduresStateTheType()
    {
        var source = ModuleWorkspace.ClassModule("Widget",
            "Option Explicit",
            "Private Type Pair",
            "    Left As Integer",
            "End Type",
            "Public Size As Integer",
            "Private total As Integer",
            "Public Function Twice() As Integer",
            "    Twice = total * 2",
            "End Function",
            "Private Property Get Count() As Integer",
            "    Count = total",
            "End Property",
            "Private Property Let Count(ByVal value As Integer)",
            "    total = value",
            "End Property");

        var (reported, fixedLines) = Lines(await AnalyzeAsync("Widget", source));

        Assert.HasCount(6, reported);
        CollectionAssert.AreEqual(new[] { LineOf(source, "Private total") }, fixedLines);
    }
}
