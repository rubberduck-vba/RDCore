using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A Let of an element whose subscripts call a function (<c>this.State(Position.X, Position.Y) = Hit</c>) is compiled (MS-VBAL §5.4.3.8): each call made
/// once, in the order the interpreter makes them, a value that calls nothing read after the subscripts. Where the interpreter runs the Let after a call
/// (a subscript out of range), it is given what was evaluated, and the call is not made again; and so is a Variant's Let whose value reads an element at
/// a subscript that calls.
/// </summary>
[TestClass]
public sealed class CompiledNestedSubscriptCallTests
{
    private const string Counted = "Public Calls As Long";

    private static readonly string[] Twice =
        ["Public Function Twice(ByVal x As Long) As Long", "    Calls = Calls + 1", "    Twice = x * 2", "End Function"];

    // a grid of Me's record marked at a position given as a class, or as an interface the class implements: each Property Get of the position made once.
    private static (string Name, string Source)[] Grids(string position) =>
    [
        ("ICoord", ModuleWorkspace.ClassModule("ICoord", "Option Explicit",
            "Public Property Get X() As Long", "End Property", "Public Property Get Y() As Long", "End Property")),
        ("Coord", ModuleWorkspace.ClassModule("Coord", "Option Explicit", "Implements ICoord",
            "Public Property Get X() As Long", "    Calls = Calls + 1", "    X = 2", "End Property",
            "Public Property Get Y() As Long", "    Calls = Calls + 1", "    Y = 3", "End Property",
            "Private Property Get ICoord_X() As Long", "    ICoord_X = X", "End Property",
            "Private Property Get ICoord_Y() As Long", "    ICoord_Y = Y", "End Property")),
        ("Grid", ModuleWorkspace.ClassModule("Grid", "Option Explicit",
            "Private Type TGrid", "    State(1 To 3, 1 To 3) As Long", "End Type",
            "Private this As TGrid",
            $"Public Sub Mark(ByVal position As {position})",
            "    this.State(position.X, position.Y) = 7",
            "End Sub",
            "Public Function At(ByVal X As Long, ByVal Y As Long) As Long",
            "    At = this.State(X, Y)",
            "End Function")),
    ];

    [TestMethod]
    [DataRow("Coord", DisplayName = "a position of a class")]
    [DataRow("ICoord", DisplayName = "a position of an interface")]
    public async Task AnElementOfThisAtSubscriptsThatCall_IsCompiled(string position)
        => await AssertCompiledAsync(Program(
            Counted,
            "Public Sub Main()",
            "    Dim G As Grid, C As Coord",
            "    Set G = New Grid",
            "    Set C = New Coord",
            "    G.Mark C",
            "    Debug.Print G.At(2, 3); Calls",
            "End Sub"), Grids(position), "| 7  2 |", ("Mark", 0));

    // an element of an array of Longs and of an array of Variants, at a subscript that calls.
    [TestMethod]
    [DataRow("Long", DisplayName = "an array of Longs")]
    [DataRow("Variant", DisplayName = "an array of Variants")]
    public async Task AnElementAtASubscriptThatCalls_IsCompiled(string type)
        => await AssertCompiledAsync(Program([
            Counted,
            "Public Sub Main()",
            "    Debug.Print Outer(); Calls",
            "End Sub",
            "Private Function Outer() As Long",
            $"    Dim a(0 To 3) As {type}",
            "    a(Twice(1)) = 3",
            "    Outer = a(2)",
            "End Function",
            .. Twice]), [], "| 3  1 |", ("Outer", 0));

    // a value that calls nothing is read after the subscript's call, which changes what it reads, as the interpreter reads it.
    [TestMethod]
    public async Task AValueThatCallsNothing_IsReadAfterTheSubscriptCalls()
        => await AssertCompiledAsync(Program(
            Counted,
            "Public k As Long",
            "Public Sub Main()",
            "    k = 5",
            "    Debug.Print Outer(); k; Calls",
            "End Sub",
            "Private Function Outer() As Long",
            "    Dim a(0 To 3) As Long",
            "    a(Bump()) = k",
            "    Outer = a(1)",
            "End Function",
            "Public Function Bump() As Long",
            "    Calls = Calls + 1",
            "    k = k + 1",
            "    Bump = 1",
            "End Function"), [], "| 6  6  1 |", ("Outer", 0));

    // a subscript out of range after its call raises error 9: the interpreter is given what was evaluated, and the call is made once.
    [TestMethod]
    public async Task ASubscriptOutOfRangeAfterItsCall_RaisesError9_TheCallMadeOnce()
        => await AssertCompiledAsync(Program([
            Counted,
            "Public Sub Main()",
            "    Debug.Print Outer(); Calls",
            "End Sub",
            "Private Function Outer() As Long",
            "    Dim a(0 To 3) As Long",
            "    On Error Resume Next",
            "    a(Twice(5)) = 1",
            "    Outer = Err.Number",
            "End Function",
            .. Twice]), [], "| 9  1 |", ("Outer", 1));

    // a Variant that holds an object, given a value that reads an element at a subscript that calls: the interpreter gives it the value evaluated once.
    [TestMethod]
    public async Task AVariantGivenAnElementAtASubscriptThatCalls_TheCallMadeOnce()
        => await AssertCompiledAsync(Program([
            Counted,
            "Public Sub Main()",
            "    Debug.Print Outer(); Calls",
            "End Sub",
            "Private Function Outer() As Long",
            "    Dim a(0 To 3) As Long, v As Variant",
            "    a(2) = 5",
            "    Set v = New Coord",
            "    v = 1 + a(Twice(1))",
            "    Outer = v",
            "End Function",
            .. Twice]), Grids("Coord"), "| 6  1 |", ("Outer", 1));
}
