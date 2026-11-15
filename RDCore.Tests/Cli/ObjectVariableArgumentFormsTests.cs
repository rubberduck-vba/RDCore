using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.3.1.11</strong> a public variable of an object named as its member, or an element of one, is passed as a copy to whatever takes it by
/// reference: a <c>ParamArray</c>, a procedure called late, the handler of a raised event. Named by itself in its own class, it is the variable. Each
/// expectation is what MS-VBA printed for the same code, observed in Excel.
/// </summary>
/// <remarks>
/// A <c>Declare</c>'s <c>ByRef</c> parameter is given a copy too, which the analyzer tests say; calling into kernel32 is no test that runs everywhere.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
public sealed class ObjectVariableArgumentFormsTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Box", ModuleWorkspace.ClassModule("Box",
            "Option Explicit",
            "Public Field As Long",
            "Public V As Variant",
            "Public Inner As Box",
            "Public Event Changed(ByRef n As Long)",
            "Public Sub Twice(ByRef n As Long)", "    n = n * 2", "End Sub",
            "Public Sub SelfMe()", "    BumpV Me.V(0)", "End Sub",
            "Public Sub SelfBare()", "    BumpV V(0)", "End Sub",
            "Public Sub Fire(ByVal o As Box)", "    RaiseEvent Changed(o.Field)", "End Sub",
            "Public Sub FireOwn()", "    RaiseEvent Changed(Field)", "End Sub",
            "Public Sub FireMe()", "    RaiseEvent Changed(Me.Field)", "End Sub",
            "Public Sub FireLocal()", "    Dim x As Long", "    x = 1", "    RaiseEvent Changed(x)", "    Field = x", "End Sub")),
        ("Sink", ModuleWorkspace.ClassModule("Sink",
            "Option Explicit",
            "Private WithEvents Peer As Box",
            "Public Sub Hook(ByVal src As Box)", "    Set Peer = src", "End Sub",
            "Private Sub Peer_Changed(ByRef n As Long)", "    n = 99", "End Sub")),
    ];

    private static async Task AssertPrintsAsync(string[] expected, string measured, VBExecutionEngine engine, params string[] body)
    {
        var printed = await RunAsync(engine, body);
        CollectionAssert.AreEqual(expected, printed, $"{measured}: {string.Join(" | ", printed)}");
    }

    private static Task<string[]> RunAsync(VBExecutionEngine engine, params string[] body)
        => ModuleWorkspace.RunAsync(Classes, string.Join("\r\n",
        [
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Public Sub BumpV(ByRef x As Variant)", "    x = x + 10", "End Sub",
            "Public Sub SetFirst(ParamArray a() As Variant)", "    a(0) = 99", "End Sub",
            "Public Sub Main()",
            "    Dim b As New Box",
            .. body,
            "End Sub",
        ]), engine);

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AnElementOfAnObjectsVariable_PassedByRef_IsACopy_ButNamedByItself_IsTheElement(VBExecutionEngine engine)
        => await AssertPrintsAsync(["1", "1", "1", "11"], "s58 B01", engine,
            "b.V = Array(1, 2)",
            "BumpV b.V(0)", "Debug.Print b.V(0)",
            "With b", "    BumpV .V(0)", "End With", "Debug.Print b.V(0)",
            "b.SelfMe", "Debug.Print b.V(0)",
            "b.SelfBare", "Debug.Print b.V(0)");

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AnElementOfAnInnerObjectsVariable_PassedByRef_IsACopy(VBExecutionEngine engine)
        => await AssertPrintsAsync(["1", "1"], "s59 M04", engine,
            "Set b.Inner = New Box", "b.Inner.V = Array(1, 2)",
            "With b", "    BumpV .Inner.V(0)", "End With", "Debug.Print b.Inner.V(0)",
            "With b.Inner", "    BumpV .V(0)", "End With", "Debug.Print b.Inner.V(0)");

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AnObjectsVariable_GivenToAParamArray_IsACopy(VBExecutionEngine engine)
        => await AssertPrintsAsync(["1", "99", "1"], "s58 F01", engine,
            "b.Field = 1", "SetFirst b.Field", "Debug.Print b.Field",
            "Dim x As Long", "x = 1", "SetFirst x", "Debug.Print x",
            "With b", "    SetFirst .Field", "End With", "Debug.Print b.Field");

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AnObjectsVariable_GivenToAProcedureCalledLate_IsACopy(VBExecutionEngine engine)
        => await AssertPrintsAsync(["3", "6", "3", "3"], "s58 F04", engine,
            "Dim o As Object", "Set o = New Box",
            "Dim t As New Box", "t.Field = 3", "o.Twice t.Field", "Debug.Print t.Field",
            "Dim x As Long", "x = 3", "o.Twice x", "Debug.Print x",
            "Dim v As Variant", "Set v = New Box", "v.Twice t.Field", "Debug.Print t.Field",
            "Dim u As Object", "Set u = t", "o.Twice u.Field", "Debug.Print t.Field");

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AnObjectsVariable_RaisedWithAnEvent_IsACopy_ButNamedByItself_IsTheVariable(VBExecutionEngine engine)
        => await AssertPrintsAsync(["1", "99", "1", "99"], "s58 F05", engine,
            "Dim h As New Sink", "h.Hook b",
            "Dim t As New Box", "t.Field = 1", "b.Fire t", "Debug.Print t.Field",
            "b.Field = 1", "b.FireOwn", "Debug.Print b.Field",
            "b.Field = 1", "b.FireMe", "Debug.Print b.Field",
            "b.Field = 1", "b.FireLocal", "Debug.Print b.Field");
}
