using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.3.1.11: what a member access given to a <c>ByRef</c> parameter is - the variable it names, which the callee writes, or a copy of its value.
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA printed for the same code in Excel (64-bit, en-US): the callee adds 10 to a value of 1, so
/// 11 is the variable, and 1 a copy.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Procedure invocation argument processing")]
public sealed class MemberAccessByRefArgumentTests
{
    private static readonly string Box = ClassModule("Box",
        "Public V As Variant",
        "Public Inner As Box",
        "Public Sub SelfMe()",
        "    BumpV Me.V(0)",
        "End Sub",
        "Public Sub SelfBare()",
        "    BumpV V(0)",
        "End Sub");

    private static string Program(string body) => StandardModule("Program",
        "Private Type TPair",
        "    X As Long",
        "    Y As Long",
        "End Type",
        "Private Type TInner",
        "    X As Long",
        "End Type",
        "Private Type TOuter",
        "    Inner As TInner",
        "    A(1) As Long",
        "    B() As Long",
        "    V As Variant",
        "End Type",
        "Public Sub BumpV(ByRef x As Variant)",
        "    x = x + 10",
        "End Sub",
        "Public Sub BumpLong(ByRef n As Long)",
        "    n = n + 10",
        "End Sub",
        "Public Sub Main()",
        "    " + string.Join("\r\n    ", body.Split(" ;; ")),
        "End Sub");

    // an element of an array an object's public variable holds is a copy, through the object as through Me; in the class, the variable it is.
    [TestMethod]
    [DataRow("Dim b As New Box ;; b.V = Array(1, 2) ;; BumpV b.V(0) ;; Debug.Print b.V(0)", "1", DisplayName = "B01 b.V(0)")]
    [DataRow("Dim b As New Box ;; b.V = Array(1, 2) ;; With b ;; BumpV .V(0) ;; End With ;; Debug.Print b.V(0)", "1", DisplayName = "B01 .V(0) in With b")]
    [DataRow("Dim b As New Box ;; b.V = Array(1, 2) ;; b.SelfMe ;; Debug.Print b.V(0)", "1", DisplayName = "B01 Me.V(0)")]
    [DataRow("Dim b As New Box ;; b.V = Array(1, 2) ;; b.SelfBare ;; Debug.Print b.V(0)", "11", DisplayName = "B01 V(0) in the class")]
    [DataRow("Dim w As Variant ;; w = Array(1, 2) ;; BumpV w(0) ;; Debug.Print w(0)", "11", DisplayName = "B01 w(0) of a local Variant")]
    // through a With block whose target is an object, or one nested in a With block of an object: a copy (observed in Excel).
    [DataRow("Dim b As New Box ;; Set b.Inner = New Box ;; b.Inner.V = Array(1, 2) ;; With b ;; BumpV .Inner.V(0) ;; End With ;; Debug.Print b.Inner.V(0)", "1",
        DisplayName = "M04 .Inner.V(0) in With b")]
    [DataRow("Dim b As New Box ;; Set b.Inner = New Box ;; b.Inner.V = Array(1, 2) ;; With b.Inner ;; BumpV .V(0) ;; End With ;; Debug.Print b.Inner.V(0)", "1",
        DisplayName = "M04 .V(0) in With b.Inner")]
    [DataRow("Dim b As New Box ;; Set b.Inner = New Box ;; b.Inner.V = Array(1, 2) ;; With b ;; With .Inner ;; BumpV .V(0) ;; End With ;; End With ;; Debug.Print b.Inner.V(0)", "1",
        DisplayName = "M04 .V(0) in With .Inner in With b")]
    public async Task AnElementOfAnObjectsVariable_IsACopy_WhereTheClassesOwnIsTheVariable(string body, string expected)
        => await AssertPrintedInEitherEngineAsync(body, expected);

    // a field of a variable of a user-defined type is the variable it is, in a With block as without it.
    [TestMethod]
    [DataRow("Dim r As TPair ;; r.X = 1 ;; With r ;; BumpLong .X ;; End With ;; Debug.Print r.X", "11", DisplayName = "W01 .X in With r")]
    [DataRow("Dim r As TPair ;; r.X = 1 ;; BumpLong r.X ;; Debug.Print r.X", "11", DisplayName = "W01 r.X")]
    [DataRow("Dim t(1) As TPair ;; t(0).X = 1 ;; With t(0) ;; BumpLong .X ;; End With ;; Debug.Print t(0).X", "11", DisplayName = "W01 .X in With t(0)")]
    [DataRow("Dim t(1) As TPair ;; t(0).X = 1 ;; BumpLong t(0).X ;; Debug.Print t(0).X", "11", DisplayName = "W01 t(0).X")]
    // a field of a field, an element of an array field, and a field in a nested With block of a record (observed in Excel).
    [DataRow("Dim r As TOuter ;; r.A(0) = 1 ;; With r ;; BumpLong .A(0) ;; End With ;; Debug.Print r.A(0)", "11", DisplayName = "M01 .A(0) in With r")]
    [DataRow("Dim r As TOuter ;; r.A(0) = 1 ;; BumpLong r.A(0) ;; Debug.Print r.A(0)", "11", DisplayName = "M01 r.A(0)")]
    [DataRow("Dim r As TOuter ;; r.Inner.X = 1 ;; With r ;; BumpLong .Inner.X ;; End With ;; Debug.Print r.Inner.X", "11", DisplayName = "M01 .Inner.X in With r")]
    [DataRow("Dim r As TOuter ;; r.Inner.X = 1 ;; BumpLong r.Inner.X ;; Debug.Print r.Inner.X", "11", DisplayName = "M01 r.Inner.X")]
    [DataRow("Dim r As TOuter ;; r.Inner.X = 1 ;; With r ;; With .Inner ;; BumpLong .X ;; End With ;; End With ;; Debug.Print r.Inner.X", "11",
        DisplayName = "M01 .X in With .Inner in With r")]
    [DataRow("Dim t(1) As TOuter ;; t(0).Inner.X = 1 ;; With t(0) ;; BumpLong .Inner.X ;; End With ;; Debug.Print t(0).Inner.X", "11", DisplayName = "M02 .Inner.X in With t(0)")]
    [DataRow("Dim t(1) As TOuter ;; t(0).Inner.X = 1 ;; With t(0) ;; With .Inner ;; BumpLong .X ;; End With ;; End With ;; Debug.Print t(0).Inner.X", "11",
        DisplayName = "M02 .X in With .Inner in With t(0)")]
    [DataRow("Dim t(1) As TOuter ;; t(0).A(0) = 1 ;; With t(0) ;; BumpLong .A(0) ;; End With ;; Debug.Print t(0).A(0)", "11", DisplayName = "M02 .A(0) in With t(0)")]
    [DataRow("Dim r As TOuter ;; ReDim r.B(1) ;; r.B(0) = 1 ;; With r ;; BumpLong .B(0) ;; End With ;; Debug.Print r.B(0)", "11", DisplayName = "M03 .B(0) in With r")]
    [DataRow("Dim r As TOuter ;; ReDim r.B(1) ;; r.B(0) = 1 ;; BumpLong r.B(0) ;; Debug.Print r.B(0)", "11", DisplayName = "M03 r.B(0)")]
    [DataRow("Dim r As TOuter ;; r.V = Array(1, 2) ;; With r ;; BumpV .V(0) ;; End With ;; Debug.Print r.V(0)", "11", DisplayName = "M03 .V(0) in With r")]
    [DataRow("Dim r As TOuter ;; r.V = Array(1, 2) ;; BumpV r.V(0) ;; Debug.Print r.V(0)", "11", DisplayName = "M03 r.V(0)")]
    [DataRow("Dim r As TOuter ;; r.V = 1 ;; With r ;; BumpV .V ;; End With ;; Debug.Print r.V", "11", DisplayName = "M03 .V in With r")]
    // as M03's .V in With r, and r.X, have it.
    [DataRow("Dim r As TOuter ;; r.V = 1 ;; BumpV r.V ;; Debug.Print r.V", "11", DisplayName = "r.V")]
    public async Task AFieldOfAUserDefinedTypeVariable_IsTheVariable(string body, string expected)
        => await AssertPrintedInEitherEngineAsync(body, expected);

    // a compiled procedure takes a ByRef Long the interpreter binds to the field it is given: the callee writes the field, run compiled.
    [TestMethod]
    [DataRow("Dim r As TPair ;; r.X = 1 ;; With r ;; BumpLong .X ;; End With ;; Debug.Print r.X", "11", DisplayName = "W01 .X in With r")]
    [DataRow("Dim r As TOuter ;; r.A(0) = 1 ;; With r ;; BumpLong .A(0) ;; End With ;; Debug.Print r.A(0)", "11", DisplayName = "M01 .A(0) in With r")]
    [DataRow("Dim r As TOuter ;; r.Inner.X = 1 ;; With r ;; With .Inner ;; BumpLong .X ;; End With ;; End With ;; Debug.Print r.Inner.X", "11",
        DisplayName = "M01 .X in With .Inner in With r")]
    [DataRow("Dim t(1) As TOuter ;; t(0).Inner.X = 1 ;; With t(0) ;; BumpLong .Inner.X ;; End With ;; Debug.Print t(0).Inner.X", "11", DisplayName = "M02 .Inner.X in With t(0)")]
    [DataRow("Dim r As TOuter ;; ReDim r.B(1) ;; r.B(0) = 1 ;; With r ;; BumpLong .B(0) ;; End With ;; Debug.Print r.B(0)", "11", DisplayName = "M03 .B(0) in With r")]
    public async Task ACompiledCallee_IsGivenTheField(string body, string expected)
    {
        CompiledExecutionTests.RequirePayloads();
        await InspectAsync([("Box", Box)], Program(body), (provider, run, _) =>
        {
            Assert.AreEqual(expected, string.Join("|", run.Output.Select(line => line.Trim())), run.ErrorMessage);
            Assert.IsTrue(provider.Image.Compiler.Results.Any(result => result.Procedure.Name == "BumpLong" && result.Result.Body is not null), "BumpLong runs compiled");
            return Task.CompletedTask;
        }, engine: VBExecutionEngine.Compiled);
    }

    // what compiled code passes by reference is a variable of its own activation: a call that passes a field or an element is the interpreter's, which
    // binds it as the rows above have it, in a caller that runs compiled around it: compiled code hands it the call as a call it makes, and does not step it.
    [TestMethod]
    public async Task ACallThatPassesAField_IsTheInterpreters()
    {
        CompiledExecutionTests.RequirePayloads();
        await InspectAsync([], StandardModule("Program",
            "Private Type TPair", "    X As Long", "End Type",
            "Private Sub BumpLong(ByRef n As Long)", "    n = n + 10", "End Sub",
            "Private Function Passes() As Long", "    Dim r As TPair", "    r.X = 1", "    BumpLong r.X", "    Passes = r.X", "End Function",
            "Public Sub Main()", "    Debug.Print Passes()", "End Sub"), (provider, run, _) =>
        {
            Assert.AreEqual("11", string.Join("|", run.Output.Select(line => line.Trim())), run.ErrorMessage);
            Assert.IsTrue(provider.Image.Compiler.Results.Any(result => result.Procedure.Name == "BumpLong" && result.Result.Body is not null), "BumpLong runs compiled");
            var passes = provider.Image.Compiler.Results.Single(result => result.Procedure.Name == "Passes").Result;
            Assert.IsNotNull(passes.Body, string.Join("; ", passes.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
            Assert.IsEmpty(passes.Stepped, string.Join("; ", passes.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
            return Task.CompletedTask;
        }, engine: VBExecutionEngine.Compiled);
    }

    private static async Task AssertPrintedInEitherEngineAsync(string body, string expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var observed = string.Join("|", await RunAsync([("Box", Box)], Program(body), engine));
            Assert.AreEqual(expected, observed, $"{engine}, which printed {observed}");
        }
    }
}
