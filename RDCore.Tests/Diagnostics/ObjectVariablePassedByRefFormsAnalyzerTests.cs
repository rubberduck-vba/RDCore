using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Model;
using RDCore.Parsing;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11026</c> whatever takes the copy: a <c>ParamArray</c>, a <c>Declare</c>d procedure, the handlers of a raised event; and an element of an object's
/// variable. <c>RDC11029</c>, a hint, for a procedure called late. Each is what MS-VBA passes as a copy, observed in Excel.
/// Both analyzers run on every row, so a row has the one finding it should and not the other's.
/// </summary>
[TestClass]
public sealed class ObjectVariablePassedByRefFormsAnalyzerTests
{
    private const string BoxSource = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Box\"\r\nOption Explicit\r\n"
        + "Public Field As Long\r\nPublic C As Currency\r\nPublic V As Variant\r\nPublic Inner As Box\r\nPublic Items As Collection\r\n"
        + "Public Event Changed(ByRef n As Long)\r\nPublic Event Shown(ByVal n As Long)\r\n"
        + "Public Sub Twice(ByRef n As Long)\r\n    n = n * 2\r\nEnd Sub\r\n"
        + "Public Sub Fire(ByVal o As Box)\r\n    RaiseEvent Changed(o.Field)\r\nEnd Sub\r\n"
        + "Public Sub FireMe()\r\n    RaiseEvent Changed(Me.Field)\r\nEnd Sub\r\n"
        + "Public Sub FireOwn()\r\n    RaiseEvent Changed(Field)\r\nEnd Sub\r\n"
        + "Public Sub ShowMe()\r\n    RaiseEvent Shown(Me.Field)\r\nEnd Sub\r\n"
        + "Public Sub SelfMe()\r\n    BumpV Me.V(0)\r\nEnd Sub\r\n"
        + "Public Sub SelfBare()\r\n    BumpV V(0)\r\nEnd Sub\r\n";

    private const string Declarations = "Option Explicit\n"
        + "Private Type TPair\n    X As Long\n    A(1) As Long\nEnd Type\n"
        + "Private Declare PtrSafe Function QueryPerformanceCounter Lib \"kernel32\" (ByRef c As Currency) As Long\n"
        + "Private Declare PtrSafe Function Sleepy Lib \"kernel32\" Alias \"Sleep\" (ByVal ms As Long) As Long\n"
        + "Public Sub BumpV(ByRef x As Variant)\n    x = x + 10\nEnd Sub\n"
        + "Public Sub BumpLong(ByRef n As Long)\n    n = n + 10\nEnd Sub\n"
        + "Public Sub SetFirst(ParamArray a() As Variant)\n    a(0) = 99\nEnd Sub\n"
        + "Public Sub Tail(ByVal n As Long, ParamArray a() As Variant)\nEnd Sub\n";

    private static async Task<AnalyzerFinding[]> RunAsync(string module, string body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\n{(Declarations + $"Public Sub Main()\n    Dim b As Box\n    Set b = New Box\n    Dim o As Object\n    Dim v As Variant\n{body}\nEnd Sub").ReplaceLineEndings("\r\n")}\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([("Box", BoxSource)], program);
        var (uri, source) = module == "Box"
            ? (new Uri(Path.Combine(ModuleWorkspace.Root, "Box.cls")), BoxSource)
            : (new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas")), program);
        var parsed = new ModuleParser().Parse(uri, source);
        var semantics = payload.Modules.Single(candidate => candidate.Module.Fragment.TrimStart('#') == module);
        var analyzed = new ModuleAnalysisContext(uri, parsed, semantics);
        return [.. new ObjectVariablePassedByRefAnalyzer().Analyze(analyzed).Concat(new ObjectVariablePassedLateAnalyzer().Analyze(analyzed))
            .OrderBy(finding => finding.Range.Start)];
    }

    private static void AssertOneFindingAt(
        AnalyzerFinding[] findings, string body, string argument, RDCoreDiagnosticId id, DiagnosticSeverity severity, string message)
    {
        Assert.HasCount(1, findings, string.Join(" | ", findings.Select(finding => $"{finding.Id} {finding.Range}")));
        Assert.AreEqual(id, findings[0].Id);
        Assert.AreEqual(severity, findings[0].Severity);
        Assert.AreEqual(message, findings[0].Message);
        var line = body.Split('\n').Single(text => text.Contains(argument, StringComparison.Ordinal));
        Assert.AreEqual(line.IndexOf(argument, StringComparison.Ordinal), findings[0].Range.Start.Character, "where the argument is written");
    }

    [TestMethod]
    [DataRow("    SetFirst b.Field", "b.Field", DisplayName = "s58 F01 a ParamArray")]
    [DataRow("    With b\n        SetFirst .Field\n    End With", ".Field", DisplayName = "s58 F01 a ParamArray, relative to a With block")]
    [DataRow("    Tail 1, 2, b.Field", "b.Field", DisplayName = "a ParamArray, past a ByVal parameter")]
    [DataRow("    QueryPerformanceCounter b.C", "b.C", DisplayName = "s58 F02 a Declare'd procedure")]
    [DataRow("    BumpV x:=b.Field", "b.Field", DisplayName = "a named argument")]
    [DataRow("    Call BumpV(x:=b.Field)", "b.Field", DisplayName = "a named argument, in a Call statement")]
    [DataRow("    BumpV b.V(0)", "b.V(0)", DisplayName = "s58 B01 an element of an object's variable")]
    [DataRow("    With b\n        BumpV .V(0)\n    End With", ".V(0)", DisplayName = "s58 B01 an element, relative to a With block")]
    [DataRow("    With b\n        BumpV .Inner.V(0)\n    End With", ".Inner.V(0)", DisplayName = "s59 M04 an element of an inner object's variable")]
    public async Task AnObjectsVariable_ForAByRefParameter_IsAWarning(string body, string argument)
        => AssertOneFindingAt(await RunAsync("Program", body), body, argument, RDCoreDiagnosticId.ObjectVariablePassedByRefIsACopy, DiagnosticSeverity.Warning,
            RDCoreDiagnosticsResources.ObjectVariablePassedByRefIsACopy_Message);

    [TestMethod]
    [DataRow("    Set o = New Box\n    o.Twice b.Field", "b.Field", DisplayName = "s58 F04 a member of an Object")]
    [DataRow("    Set v = New Box\n    v.Twice b.Field", "b.Field", DisplayName = "s58 F04 a member of a Variant")]
    [DataRow("    Set o = New Box\n    o.Twice n:=b.Field", "b.Field", DisplayName = "a member of an Object, a named argument")]
    [DataRow("    Set o = New Box\n    With o\n        .Twice b.Field\n    End With", "b.Field", DisplayName = "a member of an Object, relative to a With block")]
    [DataRow("    Set o = New Box\n    Call o.Twice(b.V(0))", "b.V(0)", DisplayName = "an element, to a member of an Object")]
    [DataRow("    Dim d As Object\n    d.Exists b.Field", "b.Field", DisplayName = "a member of an Object that only reads it (a Dictionary's Exists)")]
    public async Task AnObjectsVariable_ForAProcedureCalledLate_IsAHint(string body, string argument)
        => AssertOneFindingAt(await RunAsync("Program", body), body, argument, RDCoreDiagnosticId.ObjectVariablePassedLateIsACopy, DiagnosticSeverity.Hint,
            RDCoreDiagnosticsResources.ObjectVariablePassedLateIsACopy_Message);

    // what MS-VBA passes by reference (a variable of the procedure, an element of its array, a field of its record, however it is named) and what is a value
    // whatever takes it: neither a warning nor a hint.
    [TestMethod]
    [DataRow("    Sleepy b.Field", DisplayName = "a ByVal parameter of a Declare'd procedure")]
    [DataRow("    Tail b.Field", DisplayName = "the ByVal parameter before a ParamArray")]
    [DataRow("    Dim x As Long\n    SetFirst x", DisplayName = "a local, to a ParamArray")]
    [DataRow("    Set o = New Box\n    Dim x As Long\n    o.Twice x", DisplayName = "a local, to a member of an Object")]
    [DataRow("    Dim a(1) As Variant\n    BumpV a(0)", DisplayName = "an element of a local array")]
    [DataRow("    Set o = New Box\n    Dim a(1) As Long\n    o.Twice a(0)", DisplayName = "an element of a local array, to a member of an Object")]
    [DataRow("    Dim r As TPair\n    BumpLong r.X", DisplayName = "a field of a record")]
    [DataRow("    Dim r As TPair\n    BumpLong r.A(0)", DisplayName = "an element of an array field of a record")]
    [DataRow("    Dim r As TPair\n    With r\n        BumpLong .A(0)\n    End With", DisplayName = "an element of an array field of a record, in With r")]
    [DataRow("    Set o = b\n    Set v = New Box\n    v.Twice o.Field", DisplayName = "bound late itself: what it is, is not known")]
    [DataRow("    BumpV (b.V(0))", DisplayName = "an element in parentheses, a value")]
    [DataRow("    BumpV b.Items(1)", DisplayName = "an item of a Collection variable, its default member's value")]
    [DataRow("    Set o = New Box\n    o.Twice b.Items(1)", DisplayName = "an item of a Collection variable, to a member of an Object")]
    public async Task AnythingElse_IsNeitherAWarningNorAHint(string body)
    {
        var findings = await RunAsync("Program", body);
        Assert.IsEmpty(findings, string.Join(" | ", findings.Select(finding => $"{finding.Id} {finding.Range}")));
    }

    [TestMethod]
    public async Task TheArgumentsOfARaisedEvent_ThatAreAnObjectsVariable_AreAWarning_ButNotItsOwnNamedByItself_NorForAByValParameter()
    {
        var findings = await RunAsync("Box", "    b.FireMe");
        var lines = BoxSource.Split("\r\n");

        CollectionAssert.AreEquivalent(
            new[] { "    RaiseEvent Changed(o.Field)", "    RaiseEvent Changed(Me.Field)", "    BumpV Me.V(0)" },
            findings.Select(finding => lines[finding.Range.Start.Line]).ToArray(),
            "s58 F05: o.Field and Me.Field are copies, Field is the variable; s58 B01: Me.V(0) is a copy, V(0) the element; ShowMe's parameter is ByVal");
    }
}
