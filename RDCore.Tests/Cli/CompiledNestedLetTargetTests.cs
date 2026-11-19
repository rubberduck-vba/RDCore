using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A Let of a field of a record, of a member of an object, or of an element of an array, whose value calls a function inside an expression
/// (<c>this.N = F(2) + 1</c>, <c>P.X = F(2) + 1</c>, <c>A(1) = F(2) + 1</c>) is compiled (MS-VBAL §5.4.3.8): the value is evaluated once, then the target
/// found, as the interpreter finds it. Where the interpreter runs the Let after the call (a subscript out of range, an object variable that holds
/// <c>Nothing</c>), it is given what was evaluated, and the call is not made again.
/// </summary>
[TestClass]
public sealed class CompiledNestedLetTargetTests
{
    private const string Counted = "Public Calls As Long";

    private static readonly string[] Twice =
        ["Public Function Twice(ByVal x As Long) As Long", "    Calls = Calls + 1", "    Twice = x * 2", "End Function"];

    private static readonly (string Name, string Source)[] Members =
    [
        ("Point", ModuleWorkspace.ClassModule("Point", "Option Explicit", "Public X As Long")),
        ("Box", ModuleWorkspace.ClassModule("Box", "Option Explicit",
            "Private mV As Long",
            "Public Property Let V(ByVal Value As Long)", "    mV = Value", "End Property",
            "Public Property Get V() As Long", "    V = mV", "End Property")),
    ];

    private static readonly (string Name, string Source)[] States =
    [
        ("Tally", ModuleWorkspace.ClassModule("Tally", "Option Explicit",
            "Private Type TState", "    N As Long", "    Cells(1 To 10) As Long", "End Type",
            "Private this As TState",
            "Public Function Run(ByVal n As Long) As Long",
            "    Dim i As Long",
            "    For i = 1 To n",
            "        this.N = Twice(i) + this.N",
            "    Next",
            "    Run = this.N",
            "End Function",
            "Public Function Fill(ByVal n As Long) As Long",
            "    Dim i As Long",
            "    For i = 1 To n",
            "        this.Cells(i) = Twice(i) + 1",
            "        Fill = Fill + this.Cells(i)",
            "    Next",
            "End Function")),
    ];

    // a field of a record variable, of a scalar type and a Variant, and of the record a standard module holds, given a call and a constant: the call made
    // once each time.
    [TestMethod]
    [DataRow("r.N", DisplayName = "a Long field")]
    [DataRow("r.V", DisplayName = "a Variant field")]
    [DataRow("this.N", DisplayName = "a field of the module's record")]
    public async Task AFieldOfARecordGivenACallInsideItsValue_IsCompiled(string field)
        => await AssertCompiledAsync(Program([
            "Private Type TPair", "    N As Long", "    V As Variant", "End Type",
            "Private this As TPair",
            Counted,
            "Public Sub Main()",
            "    Debug.Print Outer(10); Calls",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim r As TPair, i As Long, t As Long",
            "    For i = 1 To n",
            $"        {field} = Twice(i) + 1",
            $"        t = t + {field}",
            "    Next",
            "    Outer = t",
            "End Function",
            .. Twice]), [], "| 120  10 |", ("Outer", 0));

    // a field of this, the record of Me's class, and an element of an array field of it.
    [TestMethod]
    public async Task AFieldOfThisAndAnElementOfIt_GivenACallInsideTheirValue_AreCompiled()
        => await AssertCompiledAsync(Program([
            Counted,
            "Public Sub Main()",
            "    Dim C As Tally",
            "    Set C = New Tally",
            "    Debug.Print C.Run(10); C.Fill(10); Calls",
            "End Sub",
            .. Twice]), States, "| 110  120  20 |", ("Run", 0), ("Fill", 0));

    // an element of an array of Longs and of an array of Variants.
    [TestMethod]
    [DataRow("Long", DisplayName = "an array of Longs")]
    [DataRow("Variant", DisplayName = "an array of Variants")]
    public async Task AnElementGivenACallInsideItsValue_IsCompiled(string type)
        => await AssertCompiledAsync(Program([
            Counted,
            "Public Sub Main()",
            "    Debug.Print Outer(10); Calls",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            $"    Dim a(1 To 10) As {type}, i As Long, t As Long",
            "    For i = 1 To n",
            "        a(i) = Twice(i) + 1",
            "        t = t + a(i)",
            "    Next",
            "    Outer = t",
            "End Function",
            .. Twice]), [], "| 120  10 |", ("Outer", 0));

    // the element is found once the value is evaluated, as the interpreter finds it: its subscript is read as the call left it.
    [TestMethod]
    public async Task TheSubscriptOfTheElement_IsReadAfterTheCall()
        => await AssertCompiledAsync(Program(
            Counted,
            "Public k As Long",
            "Public Sub Main()",
            "    Debug.Print Outer(); Calls",
            "End Sub",
            "Private Function Outer() As Long",
            "    Dim a(0 To 3) As Long",
            "    a(k) = Bump() * 10",
            "    Outer = a(0) * 100 + a(1)",
            "End Function",
            "Public Function Bump() As Long",
            "    Calls = Calls + 1",
            "    k = k + 1",
            "    Bump = k",
            "End Function"), [], "| 10  1 |", ("Outer", 0));

    // a public variable of an object, and a Property Let of one, each of an object variable of the procedure.
    [TestMethod]
    [DataRow("P.X", DisplayName = "a public variable")]
    [DataRow("B.V", DisplayName = "a Property Let")]
    public async Task AMemberOfAnObjectGivenACallInsideItsValue_IsCompiled(string member)
        => await AssertCompiledAsync(Program([
            Counted,
            "Public Sub Main()",
            "    Debug.Print Outer(10); Calls",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim P As Point, B As Box, i As Long, t As Long",
            "    Set P = New Point",
            "    Set B = New Box",
            "    For i = 1 To n",
            $"        {member} = Twice(i) + 1",
            $"        t = t + {member}",
            "    Next",
            "    Outer = t",
            "End Function",
            .. Twice]), Members, "| 120  10 |", ("Outer", 0));

    // a target the interpreter finds after the call raises its error, as it does whatever the value is: it is given what was evaluated, and the call is
    // made once.
    [TestMethod]
    [DataRow("a(9) = Twice(1) + 1", "| 9  1 |", DisplayName = "an element out of range")]
    [DataRow("v(9) = Twice(1) + 1", "| 9  1 |", DisplayName = "a Variant element out of range")]
    [DataRow("x = Twice(1) + a(9)", "| 9  1 |", DisplayName = "an element read out of range after the call")]
    [DataRow("P.X = Twice(1) + 1", "| 91  1 |", DisplayName = "a member of Nothing")]
    public async Task ATargetThatFailsAfterTheCall_RaisesItsError_TheCallMadeOnce(string statement, string printed)
        => await AssertCompiledAsync(Program([
            Counted,
            "Public Sub Main()",
            "    Debug.Print Outer(); Calls",
            "End Sub",
            "Private Function Outer() As Long",
            "    Dim a(0 To 3) As Long, v(0 To 3) As Variant, P As Point, x As Long",
            "    On Error Resume Next",
            $"    {statement}",
            "    Outer = Err.Number",
            "End Function",
            .. Twice]), Members, printed, ("Outer", 1));
}
