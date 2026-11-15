using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A Let of a <c>Variant</c> variable whose value calls a function inside an expression (<c>V = 1 + F(2)</c>, <c>V = 1 + G.Size</c>) is compiled: the value
/// is evaluated once, and the <c>Variant</c> is given what it evaluates to (MS-VBAL §5.4.3.8, §5.5.1.2.12). Where the interpreter runs the Let after the
/// call (an error the operation raises, a <c>Variant</c> that holds an object), it is given what was evaluated, and the call is not made again.
/// </summary>
[TestClass]
public sealed class CompiledNestedVariantLetTests
{
    private const string Counted = "Public Calls As Long";

    private static readonly string[] Twice =
        ["Private Function Twice(ByVal x As Long) As Long", "    Calls = Calls + 1", "    Twice = x * 2", "End Function"];

    private static readonly (string Name, string Source)[] Sized =
        [("Grid", ModuleWorkspace.ClassModule("Grid", "Option Explicit",
            "Public Property Get Size() As Long", "    Calls = Calls + 1", "    Size = 3", "End Property",
            "Private Sub Class_Terminate()", "    Debug.Print \"T\";", "End Sub"))];

    // each value calls inside an expression, and the Variant is given it: the calls are made once each.
    [TestMethod]
    [DataRow("v = 1 + Twice(i)", "| 120  10 |", DisplayName = "a call and a constant")]
    [DataRow("v = Twice(i) * Twice(i)", "| 1540  20 |", DisplayName = "two calls of one operation")]
    [DataRow("v = Twice(i) \\ 3", "| 33  10 |", DisplayName = "an integer division of a call")]
    [DataRow("v = -Twice(i) + i", "| -55  10 |", DisplayName = "a call negated, and a variable")]
    public async Task AVariantGivenACallInsideItsValue_IsCompiled(string statement, string printed)
        => await AssertCompiledAsync(Program([
            Counted,
            "Public Sub Main()",
            "    Debug.Print Outer(10); Calls",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, v As Variant, t As Long",
            "    For i = 1 To n",
            $"        {statement}",
            "        t = t + v",
            "    Next",
            "    Outer = t",
            "End Function",
            .. Twice]), [], printed, ("Outer", 0));

    // a Property Get of an object inside the value, the object a variable of the procedure.
    [TestMethod]
    public async Task AVariantGivenAMemberOfAnObjectInsideItsValue_IsCompiled()
        => await AssertCompiledAsync(Program(
            Counted,
            "Public Sub Main()",
            "    Debug.Print Outer(10); Calls",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim G As Grid, i As Long, v As Variant, t As Long",
            "    Set G = New Grid",
            "    For i = 1 To n",
            "        v = 1 + G.Size",
            "        t = t + v",
            "    Next",
            "    Outer = t",
            "End Function"), Sized, "T 40  10 |", ("Outer", 0));

    // an operation of two Integers that overflows after the call raises error 6, as it does whatever it is assigned to: the Variant keeps what it held, and
    // the call is made once.
    [TestMethod]
    public async Task AnOperationThatOverflowsAfterTheCall_RaisesError6_TheCallMadeOnce()
        => await AssertCompiledAsync(Program([
            Counted,
            "Public Sub Main()",
            "    Debug.Print Outer(32000); Calls",
            "End Sub",
            "Private Function Outer(ByVal k As Integer) As Long",
            "    Dim v As Variant",
            "    On Error Resume Next",
            "    v = 5",
            "    v = Big() + k",
            "    Outer = v * 10 + Err.Number",
            "End Function",
            "Private Function Big() As Integer",
            "    Calls = Calls + 1",
            "    Big = 1000",
            "End Function"]), [], "| 56  1 |", ("Outer", 1));

    // a Variant that holds an object is given the value by the interpreter, which lets go of the object: the call is made once, before it.
    [TestMethod]
    public async Task AVariantHoldingAnObject_IsGivenTheValueEvaluatedOnce()
        => await AssertCompiledAsync(Program([
            Counted,
            "Public Sub Main()",
            "    Debug.Print Replaced(); Calls",
            "End Sub",
            "Private Function Replaced() As Long",
            "    Dim v As Variant",
            "    Set v = New Grid",
            "    Debug.Print \"a\";",
            "    v = 1 + Twice(1)",
            "    Debug.Print \"b\";",
            "    Replaced = v",
            "End Function",
            .. Twice]), Sized, "aTb 3  1", ("Replaced", 1));
}
