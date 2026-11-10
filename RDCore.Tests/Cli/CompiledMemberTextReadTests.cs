using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>String</c> public variable of an object (MS-VBAL §5.6.12) read by compiled code: of an object variable, of the object of a <c>With</c> block,
/// assigned to a <c>String</c>, joined, compared. The text is read in the cell the object holds it in. A variable of <c>Nothing</c> is the interpreter's,
/// which raises error 91. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledMemberTextReadTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static readonly IReadOnlyList<(string Name, string Source)> Ships =
    [
        ("Ship", ModuleWorkspace.ClassModule("Ship", "Option Explicit",
            "Public Name As String",
            "Public Hits As Long")),
    ];

    private static void AssertSteppedOnlyBlocks(CompilationResult result)
        => Assert.IsTrue(result.Stepped.All(reason => reason.Kind is IneligibilityKind.Instruction),
            string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));

    // the text of a public variable assigned to a String, joined to another, and compared in a condition.
    [TestMethod]
    public async Task ATextVariableOfAnObject_AssignedJoinedAndCompared_IsReadInItsCell()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As New Ship, B As New Ship",
            "    A.Name = \"Hood\"",
            "    B.Name = \"Bismarck\"",
            "    Debug.Print Named(A, B, 4)",
            "End Sub",
            "Private Function Named(ByVal A As Ship, ByVal B As Ship, ByVal N As Long) As Long",
            "    Dim C As Ship",
            "    Dim S As String",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        If I Mod 2 = 0 Then Set C = A Else Set C = B",
            "        S = C.Name & \"!\"",
            "        If S = \"Hood!\" Then Named = Named + 10",
            "        If C.Name = \"Bismarck\" Then Named = Named + 1",
            "    Next",
            "End Function"), "Named", recoveries: 0, classes: Ships, expected: "22");
        Assert.IsEmpty(result.Stepped, string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }

    // the text of a public variable of the object of a With block, compared in a condition and joined.
    [TestMethod]
    public async Task ATextVariableOfAWithBlocksObject_IsReadInItsCell()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As New Ship",
            "    A.Name = \"Hood\"",
            "    Debug.Print Within(A)",
            "End Sub",
            "Private Function Within(ByVal C As Ship) As Long",
            "    Dim S As String",
            "    Dim I As Long",
            "    With C",
            "        For I = 1 To 3",
            "            S = S & .Name",
            "            If .Name = \"Hood\" Then Within = Within + 1",
            "        Next",
            "    End With",
            "    If S = \"HoodHoodHood\" Then Within = Within + 100",
            "End Function"), "Within", recoveries: 1, classes: Ships, expected: "103");
        AssertSteppedOnlyBlocks(result);
    }

    // the text of a public variable of Nothing: error 91, raised by the interpreter, which the handler is given; S keeps what it held.
    [TestMethod]
    public async Task ATextVariableOfNothing_RaisesError91()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Guarded()",
            "End Sub",
            "Private Function Guarded() As Long",
            "    Dim C As Ship",
            "    Dim S As String",
            "    S = \"kept\"",
            "    On Error Resume Next",
            "    S = C.Name",
            "    Guarded = Err.Number",
            "    If S = \"kept\" Then Guarded = Guarded * 10 + 1",
            "End Function"), "Guarded", recoveries: 1, classes: Ships, expected: "911");
}
