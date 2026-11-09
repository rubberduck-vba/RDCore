using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A public variable of an object read by compiled code (MS-VBAL §5.6.12): of an object variable declared as a class, of the default instance of a class,
/// or of the object of a <c>With</c> block; as an operand, in a condition, for the whole value a variable is given; a <c>Long</c>, an enumeration, a
/// <c>Double</c>. Compiled code reads the variable the object's class has for it, in its cell, where it is written in its expression: before a call right of
/// it, after a call left of it. A read of a variable of <c>Nothing</c> is the interpreter's, which raises error 91. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledMemberFieldReadTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Cell", ModuleWorkspace.ClassModule("Cell", "Option Explicit",
            "Public X As Long",
            "Public Kind As GridState",
            "Public Ratio As Double",
            "Public Function Bump() As Long",
            "    X = X + 1",
            "    Bump = X",
            "End Function")),
        ("Game", ModuleWorkspace.ClassModule("Game", "Option Explicit",
            "Private Type TGame",
            "    Board As Cell",
            "End Type",
            "Private this As TGame",
            "Public Sub Init()",
            "    Set this.Board = New Cell",
            "End Sub",
            "Public Function Play(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Play = Play + this.Board.Bump() * this.Board.X",
            "    Next",
            "End Function")),
        ("Tally", ModuleWorkspace.ClassModule("Tally",
            "Attribute VB_PredeclaredId = True",
            "Option Explicit",
            "Public Count As Long",
            "Public Sub Add(ByVal By As Long)",
            "    Count = Count + By",
            "End Sub")),
    ];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static readonly string[] States =
    [
        "Public Enum GridState",
        "    Unknown = 0",
        "    PreviousHit = 1",
        "    PreviousMiss = 2",
        "End Enum",
    ];

    // the program run both ways, as AssertAlikeAsync runs it, with as many statements handed to the interpreter as recoveries says; and each call compiled
    // code made, made through the member's typed entry.
    private static async Task AssertFieldAsync(string program, string compiled, long direct, long recoveries, string? expected)
    {
        var result = await AssertAlikeAsync(program, compiled, recoveries, classes: Classes, expected: expected);
        CompiledDirectCallTests.AssertCalls(direct, (result.Body!.DirectCalls, result.Body.IndirectCalls));
    }

    // a Long variable of an object variable's object, in a condition and as an operand.
    [TestMethod]
    public async Task AVariableOfAnObject_InAConditionOrAsAnOperand_IsReadInItsCell()
        => await AssertFieldAsync(Program([.. States,
            "Public Sub Main()",
            "    Debug.Print Sum(4)",
            "End Sub",
            "Private Function Sum(ByVal N As Long) As Long",
            "    Dim C As Cell",
            "    Dim I As Long",
            "    Set C = New Cell",
            "    For I = 1 To N",
            "        C.X = I * 2",
            "        If C.X > 4 Then Sum = Sum + 100",
            "        Sum = Sum + C.X",
            "    Next",
            "End Function"]), "Sum", direct: 0, recoveries: 0, expected: "220");

    // an enumeration variable and a Double one, each read in its own kind.
    [TestMethod]
    public async Task AVariableOfAnObject_OfAnEnumerationOrADouble_IsReadInItsKind()
        => await AssertFieldAsync(Program([.. States,
            "Public Sub Main()",
            "    Debug.Print Mixed(4)",
            "End Sub",
            "Private Function Mixed(ByVal N As Long) As Long",
            "    Dim C As Cell",
            "    Dim I As Long",
            "    Dim Total As Double",
            "    Set C = New Cell",
            "    For I = 1 To N",
            "        C.Ratio = I / 4",
            "        If I Mod 2 = 0 Then C.Kind = PreviousMiss Else C.Kind = PreviousHit",
            "        Mixed = Mixed * 10 + C.Kind",
            "        Total = Total + C.Ratio",
            "    Next",
            "    Mixed = Mixed * 100 + Total * 4",
            "End Function"]), "Mixed", direct: 0, recoveries: 0, expected: "121210");

    // a variable of the With block's object, read before a call right of it, then after a call left of it, which changes it: each read where it is written.
    [TestMethod]
    public async Task AVariableOfAWithBlocksObject_IsReadWhereItIsWritten_AroundACall()
        => await AssertFieldAsync(Program([.. States,
            "Public Sub Main()",
            "    Debug.Print Bumps(3)",
            "End Sub",
            "Private Function Bumps(ByVal N As Long) As Long",
            "    Dim C As Cell",
            "    Dim I As Long",
            "    Set C = New Cell",
            "    With C",
            "        For I = 1 To N",
            "            Bumps = Bumps * 100 + .X * 10 + .Bump()",
            "        Next",
            "        Bumps = Bumps * 100 + .Bump() * 10 + .X",
            "    End With",
            "End Function"]), "Bumps", direct: 4, recoveries: 2, expected: "1122344");

    // a variable of the default instance of a class.
    [TestMethod]
    public async Task AVariableOfTheDefaultInstance_IsReadInItsCell()
        => await AssertFieldAsync(Program([.. States,
            "Public Sub Main()",
            "    Tally.Add 5",
            "    Debug.Print Twice(3)",
            "End Sub",
            "Private Function Twice(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Twice = Twice + Tally.Count * I",
            "        Tally.Add 1",
            "    Next",
            "End Function"]), "Twice", direct: 3, recoveries: 0, expected: "38");

    // a member called, and a variable read, of the object an object field of a record of the procedure holds: as a statement, as an operand, in a condition.
    [TestMethod]
    public async Task AMemberOfAnObjectFieldOfARecord_IsCalledAndReadOnTheObjectItHolds()
        => await AssertFieldAsync(Program([.. States,
            "Private Type Pair",
            "    Left As Cell",
            "    Count As Long",
            "End Type",
            "Public Sub Main()",
            "    Debug.Print Pairs(3)",
            "End Sub",
            "Private Function Pairs(ByVal N As Long) As Long",
            "    Dim P As Pair",
            "    Dim I As Long",
            "    Set P.Left = New Cell",
            "    For I = 1 To N",
            "        P.Left.Bump",
            "        Pairs = Pairs * 10 + P.Left.X",
            "        If P.Left.Bump() > 3 Then Pairs = Pairs + 1",
            "    Next",
            "End Function"]), "Pairs", direct: 6, recoveries: 0, expected: "146");

    // a member of the object a field of Me's record holds (this.Board), called and read inside one expression: the variable read after the call, which changed it.
    [TestMethod]
    public async Task AMemberOfAnObjectFieldOfMesRecord_IsCalledThenRead()
        => await AssertFieldAsync(Program([.. States,
            "Public Sub Main()",
            "    Dim G As Game",
            "    Set G = New Game",
            "    G.Init",
            "    Debug.Print G.Play(3)",
            "End Sub"]), "Play", direct: 3, recoveries: 0, expected: "14");

    // a variable of Nothing: the interpreter's, which raises error 91, to the handler, which resumes after it.
    [TestMethod]
    public async Task AVariableOfNothing_RaisesError91()
        => await AssertFieldAsync(Program([.. States,
            "Public Sub Main()",
            "    Debug.Print Guarded()",
            "End Sub",
            "Private Function Guarded() As Long",
            "    Dim C As Cell",
            "    On Error Resume Next",
            "    Guarded = 1 + C.X",
            "    Guarded = Guarded + 7",
            "End Function"]), "Guarded", direct: 0, recoveries: 1, expected: "7");
}
