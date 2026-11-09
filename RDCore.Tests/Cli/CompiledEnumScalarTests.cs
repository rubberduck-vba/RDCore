using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A variable, a parameter, a function result and a field declared as an enumeration, in compiled code: each is a <c>Long</c> (MS-VBAL §5.2.3.4), held as
/// compiled code holds one, and given to the interpreter as the <c>Long</c> it holds. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledEnumScalarTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static readonly string[] States =
    [
        "Public Enum GridState",
        "    Unknown = 0",
        "    PreviousHit = 1",
        "    PreviousMiss = 2",
        "    Sunk = 40",
        "End Enum",
    ];

    // an enumeration's members assigned to a local declared as the enumeration, compared with it, and added to a Long.
    [TestMethod]
    public async Task ALocalOfAnEnumeration_IsALong_AssignedComparedAndAdded()
        => await AssertAlikeAsync(Program([.. States,
            "Public Sub Main()",
            "    Debug.Print Tally(9)",
            "End Sub",
            "Private Function Tally(ByVal N As Long) As Long",
            "    Dim S As GridState",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        If I Mod 3 = 0 Then S = PreviousMiss Else S = PreviousHit",
            "        If S = PreviousHit Then Tally = Tally + I Else Tally = Tally + S * 100",
            "    Next",
            "    S = Sunk",
            "    Tally = Tally * 10 + S",
            "End Function"]), "Tally", recoveries: 0, expected: "6310");

    // an enumeration given by value and returned: a compiled caller calls the compiled callee through its typed entry.
    [TestMethod]
    public async Task AnEnumerationParameter_AndResult_AreLongs_CalledDirectly()
    {
        var result = await AssertAlikeAsync(Program([.. States,
            "Public Sub Main()",
            "    Debug.Print Walk(7)",
            "End Sub",
            "Private Function Walk(ByVal N As Long) As Long",
            "    Dim S As GridState",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        S = Following(S)",
            "        Walk = Walk * 3 + S",
            "    Next",
            "End Function",
            "Private Function Following(ByVal S As GridState) As GridState",
            "    If S = PreviousMiss Then Following = Unknown Else Following = S + 1",
            "End Function"]), "Walk", recoveries: 0, expected: "1261");
        Assert.IsGreaterThan(0L, result.Body!.DirectCalls, "Following is called through its typed entry");
    }

    // an enumeration passed by reference: the callee assigns the caller's own variable, in its cell.
    [TestMethod]
    public async Task AnEnumerationPassedByReference_IsTheCallersCell()
        => await AssertAlikeAsync(Program([.. States,
            "Public Sub Main()",
            "    Debug.Print Marks(5)",
            "End Sub",
            "Private Function Marks(ByVal N As Long) As Long",
            "    Dim S As GridState",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Mark S, I",
            "        Marks = Marks * 10 + S",
            "    Next",
            "End Function",
            "Private Sub Mark(ByRef S As GridState, ByVal I As Long)",
            "    If I Mod 2 = 0 Then S = PreviousMiss Else S = PreviousHit",
            "End Sub"]), "Marks", recoveries: 0, expected: "12121");

    // a field of the module declared as an enumeration, read and written by compiled code in the cell the interpreter holds it in.
    [TestMethod]
    public async Task AFieldOfAnEnumeration_IsReadAndWrittenInItsCell()
        => await AssertAlikeAsync(Program([.. States,
            "Private mState As GridState",
            "Public Sub Main()",
            "    mState = PreviousMiss",
            "    Debug.Print Flip(6)",
            "    Debug.Print mState",
            "End Sub",
            "Private Function Flip(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        If mState = PreviousHit Then mState = PreviousMiss Else mState = PreviousHit",
            "        Flip = Flip * 10 + mState",
            "    Next",
            "End Function"]), "Flip", recoveries: 0, expected: "121212");
}
