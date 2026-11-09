using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Function</c> or a <c>Property Get</c> of the object of a <c>With</c> block (MS-VBAL §5.4.2.21) read by compiled code inside an expression: as an
/// operand, in a condition, given an argument, in nested blocks. Compiled code calls the member on the object the interpreter holds for the block, as the
/// interpreter does; the <c>With</c> statement and its <c>End With</c> are the interpreter's, and so is a read of a member of a block whose object is
/// <c>Nothing</c>, which raises error 91. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledWithMemberReadTests
{
    // each row folds what each read gives into the number it prints, in order: a read of the wrong object, of the wrong member, or with the wrong argument
    // is another number.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Ship", ModuleWorkspace.ClassModule("Ship", "Option Explicit",
            "Private mSize As Long",
            "Private mHits As Long",
            "Public Sub Init(ByVal Size As Long)",
            "    mSize = Size",
            "End Sub",
            "Public Sub Hit()",
            "    mHits = mHits + 1",
            "End Sub",
            "Public Property Get Hits() As Long",
            "    Hits = mHits",
            "End Property",
            "Public Property Get IsSunken() As Boolean",
            "    IsSunken = mHits >= mSize",
            "End Property",
            "Public Function Left(ByVal Extra As Long) As Long",
            "    Left = mSize - mHits + Extra",
            "End Function")),
    ];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the program run both ways, as AssertAlikeAsync runs it, with as many statements handed to the interpreter as recoveries says; and each call compiled
    // code made, made through the member's typed entry.
    private static async Task AssertWithAsync(string program, string compiled, long direct, long recoveries, string? expected = null)
    {
        var result = await AssertAlikeAsync(program, compiled, recoveries, classes: Classes, expected: expected);
        CompiledDirectCallTests.AssertCalls(direct, (result.Body!.DirectCalls, result.Body.IndirectCalls));
    }

    // a Property Get of the block's object in a condition, and another as an operand.
    [TestMethod]
    public async Task AMemberOfAWithBlocksObject_InAConditionOrAsAnOperand_IsCompiled()
        => await AssertWithAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Sunk(5)",
            "End Sub",
            "Private Function Sunk(ByVal N As Long) As Long",
            "    Dim S As Ship",
            "    Dim I As Long",
            "    Set S = New Ship",
            "    S.Init 3",
            "    With S",
            "        For I = 1 To N",
            "            .Hit",
            "            If .IsSunken Then Sunk = Sunk + 1",
            "            Sunk = Sunk * 2 + .Hits",
            "        Next",
            "    End With",
            "End Function"), "Sunk", direct: 16, recoveries: 2, expected: "71");

    // a Function of the block's object given an argument, as an operand.
    [TestMethod]
    public async Task AMemberOfAWithBlocksObject_GivenAnArgument_IsCompiled()
        => await AssertWithAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Rest(3)",
            "End Sub",
            "Private Function Rest(ByVal N As Long) As Long",
            "    Dim S As Ship",
            "    Dim I As Long",
            "    Set S = New Ship",
            "    S.Init 3",
            "    With S",
            "        For I = 1 To N",
            "            Rest = Rest * 10 + .Left(I)",
            "        Next",
            "    End With",
            "End Function"), "Rest", direct: 4, recoveries: 2, expected: "456");

    // nested blocks: a member read in the inner block is of the inner block's object, and one read after its End With of the outer block's.
    [TestMethod]
    public async Task AMemberOfANestedWithBlocksObject_IsOfTheInnerBlocksObject()
        => await AssertWithAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Both()",
            "End Sub",
            "Private Function Both() As Long",
            "    Dim A As Ship",
            "    Dim B As Ship",
            "    Set A = New Ship",
            "    A.Init 2",
            "    A.Hit",
            "    Set B = New Ship",
            "    B.Init 5",
            "    With A",
            "        Both = .Hits",
            "        With B",
            "            .Hit",
            "            .Hit",
            "            Both = Both * 10 + .Hits",
            "        End With",
            "        Both = Both * 10 + .Left(0)",
            "    End With",
            "End Function"), "Both", direct: 8, recoveries: 4, expected: "121");

    // a member of a block whose object is Nothing, inside an expression: the interpreter's, which raises error 91, to the handler, which resumes after it.
    [TestMethod]
    public async Task AMemberOfAWithBlocksObjectThatIsNothing_RaisesError91()
        => await AssertWithAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Guarded()",
            "End Sub",
            "Private Function Guarded() As Long",
            "    Dim S As Ship",
            "    On Error Resume Next",
            "    With S",
            "        Guarded = 1 + .Hits",
            "    End With",
            "    Guarded = Guarded + 7",
            "End Function"), "Guarded", direct: 0, recoveries: 3);
}
