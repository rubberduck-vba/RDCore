using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// An array field of a record, compiled: <c>this.Cells(X, Y)</c> of a class's <c>Private this As TGrid</c>, a field of a record variable or of a standard
/// module's record. The array is found through its record each time an element or a bound is read, as an array variable's is found in its cell, so compiled
/// code sees what the interpreter did to it: a <c>ReDim</c>, a copy of the whole record. An element of an enumeration is a <c>Long</c> (MS-VBAL §5.2.3.4),
/// read and written as one. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledArrayFieldTests
{
    // a class whose state is one record of Me's, with a grid of an enumeration: `this.Cells`, as a game's grid is.
    private static (string Name, string Source)[] Grids()
        =>
        [
            ("Grid", ModuleWorkspace.ClassModule("Grid", ["Option Explicit",
                "Private Enum CellState", "    Unseen = -1", "    Water", "    Ship", "End Enum",
                "Private Type TGrid", "    Cells(1 To 4, 1 To 3) As CellState", "    Marks As Long", "End Type",
                "Private this As TGrid",
                "Private Sub Class_Initialize()",
                "    Dim X As Long, Y As Long",
                "    For X = LBound(this.Cells, 1) To UBound(this.Cells, 1)",
                "        For Y = LBound(this.Cells, 2) To UBound(this.Cells, 2)",
                "            this.Cells(X, Y) = CellState.Unseen",
                "        Next",
                "    Next",
                "End Sub",
                "Public Sub Mark(ByVal X As Long, ByVal Y As Long)",
                "    this.Cells(X, Y) = Ship",
                "    this.Marks = this.Marks + 1",
                "End Sub",
                "Public Sub Sweep(ByVal X As Long)",
                "    Dim Y As Long",
                "    For Y = 1 To UBound(this.Cells, 2)",
                "        If this.Cells(X, Y) = Unseen Then this.Cells(X, Y) = Water",
                "    Next",
                "End Sub",
                "Public Function Count(ByVal State As Long) As Long",
                "    Dim X As Long, Y As Long",
                "    For X = 1 To UBound(this.Cells)",
                "        For Y = 1 To 3",
                "            If this.Cells(X, Y) = State Then Count = Count + 1",
                "        Next",
                "    Next",
                "End Function"])),
        ];

    // each procedure named compiled, none of its statements stepped by the interpreter, and handed to it as often as its recoveries say, never for good.
    internal static Task AssertCompiledAsync(string program, IReadOnlyList<(string Name, string Source)> classes, string expected,
        params (string Name, long Recoveries)[] compiled)
        => AssertCompiledAsync(program, classes, expected, [], [.. compiled.Select(each => (each.Name, each.Recoveries, 0L))]);

    // the same, the interpreter stepping only the statements of stepped (each "Kind Detail"), and handed each activation for good as often as its walk-backs say.
    internal static async Task AssertCompiledAsync(string program, IReadOnlyList<(string Name, string Source)> classes, string expected, string[] stepped,
        params (string Name, long Recoveries, long WalkBacks)[] compiled)
    {
        var results = await AssertAlikeAsync(program, classes, expected);
        foreach (var (name, recoveries, walkBacks) in compiled)
        {
            Assert.IsTrue(results.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            var steppedHere = result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}").ToArray();
            CollectionAssert.AreEquivalent(stepped, steppedHere, $"{name}'s stepped statements: {string.Join("; ", steppedHere)}");
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(recoveries, result.Body.Recoveries, $"{name}'s recoveries");
                Assert.AreEqual(walkBacks, result.Body.WalkBacks, $"{name}'s walk-backs");
            }
        }
    }

    // each object's grid is its own, filled through its bounds and read and written element by element: none of it is the interpreter's.
    [TestMethod]
    public async Task TheGridOfThis_IsEachObjectsOwn()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Dim G As Grid, H As Grid",
            "    Set G = New Grid",
            "    Set H = New Grid",
            "    G.Mark 1, 1",
            "    G.Mark 2, 3",
            "    G.Sweep 1",
            "    H.Mark 4, 3",
            "    Debug.Print G.Count(-1); G.Count(0); G.Count(1); H.Count(-1); H.Count(1)",
            "End Sub"), Grids(), "| 8  2  2  11  1 |",
            ("Class_Initialize", 0), ("Mark", 0), ("Sweep", 0), ("Count", 0));

    // a subscript outside the field's dimension is the interpreter's, which raises error 9 (MS-VBAL §5.6.13), and nothing is written.
    [TestMethod]
    public async Task ASubscriptOutsideTheFieldsDimension_RaisesError9()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Dim G As Grid",
            "    Set G = New Grid",
            "    On Error Resume Next",
            "    G.Mark 5, 1",
            "    Debug.Print Err.Number; G.Count(1)",
            "End Sub"), Grids(), "| 9  0 |", [],
            ("Mark", 0, 1), ("Count", 0, 0));

    // a ReDim of the field is the interpreter's, and compiled code finds the array the field holds after it: its bounds, its elements kept by Preserve.
    // Before the first, the field holds no bounds, which is error 9 (MS-VBAL §3.3.5.2).
    [TestMethod]
    public async Task AnArrayFieldReDimmed_IsFoundAgainThroughItsRecord()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Dim B As Bag",
            "    Set B = New Bag",
            "    On Error Resume Next",
            "    Debug.Print B.Total",
            "    Debug.Print Err.Number",
            "    On Error GoTo 0",
            "    B.Grow 3",
            "    B.Fill 1",
            "    B.Grow 5",
            "    B.Fill 10",
            "    Debug.Print B.Total",
            "End Sub"),
            [("Bag", ModuleWorkspace.ClassModule("Bag", ["Option Explicit",
                "Private Type TBag", "    Items() As Long", "End Type",
                "Private this As TBag",
                "Public Sub Grow(ByVal N As Long)", "    ReDim Preserve this.Items(1 To N)", "End Sub",
                "Public Sub Fill(ByVal Start As Long)",
                "    Dim I As Long",
                "    For I = LBound(this.Items) To UBound(this.Items)",
                "        this.Items(I) = this.Items(I) + Start * I",
                "    Next",
                "End Sub",
                "Public Function Total() As Long",
                "    Dim I As Long",
                "    For I = LBound(this.Items) To UBound(this.Items)",
                "        Total = Total + this.Items(I)",
                "    Next",
                "End Function"]))],
            "| 9 ; 156 |", [],
            ("Fill", 0, 0), ("Total", 0, 1));

    private static readonly string[] Rows =
    [
        "Private Enum Shade", "    Light = 1", "    Dark", "End Enum",
        "Private Type TRow", "    Cells(1 To 3) As Shade", "End Type",
        "Private Type TBoard", "    Row As TRow", "    Moves As Long", "End Type",
    ];

    // a record copied whole by a Let (MS-VBAL §5.4.3.8), which the interpreter runs, copies its array field: each record's elements are its own after it.
    [TestMethod]
    public async Task ARecordCopiedByValue_HasAnArrayOfItsOwn()
        => await AssertCompiledAsync(Program([.. Rows,
            "Public Sub Main()",
            "    Debug.Print Copied()",
            "End Sub",
            "Private Function Copied() As Long",
            "    Dim A As TRow, B As TRow",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        A.Cells(I) = Light",
            "    Next",
            "    B = A",
            "    B.Cells(2) = Dark",
            "    A.Cells(3) = Dark",
            "    For I = 1 To 3",
            "        Copied = Copied * 100 + A.Cells(I) * 10 + B.Cells(I)",
            "    Next",
            "End Function"]), [], "| 111221 |", ["AssignmentTarget B"], ("Copied", 1, 0));

    // a standard module's record holds its array field in the session, through a record in it: what one procedure writes, another reads.
    [TestMethod]
    public async Task AnArrayFieldOfAModulesRecord_IsHeldInTheSession()
        => await AssertCompiledAsync(Program([.. Rows,
            "Private Board As TBoard",
            "Public Sub Main()",
            "    Play 2",
            "    Play 3",
            "    Debug.Print Score(); Board.Moves",
            "End Sub",
            "Private Sub Play(ByVal At As Long)",
            "    Dim I As Long",
            "    For I = LBound(Board.Row.Cells) To At",
            "        Board.Row.Cells(I) = Board.Row.Cells(I) + 1",
            "    Next",
            "    Board.Moves = Board.Moves + 1",
            "End Sub",
            "Private Function Score() As Long",
            "    Dim I As Long",
            "    For I = 1 To UBound(Board.Row.Cells)",
            "        Score = Score * 10 + Board.Row.Cells(I)",
            "    Next",
            "End Function"]), [], "| 221  2 |",
            ("Play", 0), ("Score", 0));

    // an array variable of an enumeration: its elements are Longs, held in cells of the Long type, as a variable declared as the enumeration is, and so are
    // those of one a ReDim sizes.
    [TestMethod]
    public async Task AnArrayOfAnEnumeration_IsReadAndWrittenInCompiledCode()
        => await AssertCompiledAsync(Program([.. Rows,
            "Public Sub Main()",
            "    Debug.Print Shades()",
            "End Sub",
            "Private Function Shades() As Long",
            "    Dim S(1 To 4) As Shade, D() As Shade",
            "    Dim I As Long",
            "    For I = 1 To 4",
            "        If I Mod 2 = 0 Then S(I) = Dark Else S(I) = Light",
            "    Next",
            "    For I = 1 To 4",
            "        Shades = Shades * 10 + S(I)",
            "    Next",
            "    ReDim D(0 To 2)",
            "    D(1) = Dark",
            "    Shades = Shades * 10 + D(0) + D(1)",
            "End Function"]), [], "| 12122 |", ("Shades", 0));
}
