using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The <c>Let</c> of an index expression whose callee is a function of an object, called with no argument and its result indexed
/// (<strong>MS-VBAL §5.6.13.1</strong>): the interpreter runs the statement for compiled code as a support call. The function is called once, its result
/// indexed as the interpreter indexes it (an array for its element, an object through its default member) and let-coerced to the variable's type
/// (<strong>MS-VBAL §5.5.1.2</strong>); an error it raises is the activation's, at the statement's line.
/// </summary>
[TestClass]
public sealed class CompiledIndexedCallResultTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Kinds", ModuleWorkspace.ClassModule("Kinds",
            "Attribute VB_PredeclaredId = True",
            "Option Explicit",
            "Public Calls As Long",
            "Public Shape As Long",
            "Public Function Names() As Variant",
            "    Calls = Calls + 1",
            "    If Shape = 0 Then Names = Array(10, 20, 30)",
            "    If Shape = 1 Then Names = Array(\"10\", \"20\", \"30\")",
            "    If Shape = 2 Then Names = Array(\"abc\")",
            "    If Shape = 3 Then Names = 5",
            "    If Shape = 4 Then",
            "        Dim C As Collection",
            "        Set C = New Collection",
            "        C.Add 10",
            "        C.Add 20",
            "        C.Add 30",
            "        Set Names = C",
            "    End If",
            "End Function")),
    ];

    // Picked(index) of what Names returns in shape, then how many times Names was called; the Let is on line 9, the line an error it raises is at.
    private static string Program(int shape, int index) => string.Join("\r\n",
        "Public Sub Main()",
        $"    Kinds.Shape = {shape}",
        $"    Debug.Print Picked({index})",
        "    Debug.Print Kinds.Calls",
        "End Sub",
        "Private Function Picked(ByVal N As Long) As String",
        "    On Error GoTo Failed",
        "    Dim Kind As Long",
        "    Kind = Kinds.Names(N)",
        "    Picked = Kind",
        "    Exit Function",
        "Failed:",
        "    Picked = Err.Number & \" \" & Erl",
        "End Function") + "\r\n";

    // the program run both ways, printing expected; Picked compiled, its Let of the indexed result of Names a support call, which the interpreter runs,
    // and none of its statements stepped for it. The handler's line, of Err.Number and Erl, is the interpreter's.
    private static async Task AssertSupportCallAsync(int shape, int index, string expected)
    {
        var compiled = await AssertAlikeAsync(Program(shape, index), Classes, expected);
        Assert.IsTrue(compiled.TryGetValue("Picked", out var result), "Picked was not called");
        Assert.IsNotNull(result.Body, $"Picked: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        Assert.IsFalse(result.Stepped.Any(reason => reason.Detail == "Names"), $"Picked: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        Assert.IsTrue(result.SupportCalls.Any(reason => reason.Detail == "Names"), "the Let of Names(N) is not a support call");
    }

    // an array of Long values, indexed for its element.
    [TestMethod]
    public async Task TheIndexedArrayAFunctionReturns_IsItsElement_AndTheFunctionIsCalledOnce()
        => await AssertSupportCallAsync(shape: 0, index: 2, expected: "30 ; 1");

    // an object, indexed through its default member: the Item of a Collection.
    [TestMethod]
    public async Task TheIndexedObjectAFunctionReturns_IsGivenTheIndexByItsDefaultMember()
        => await AssertSupportCallAsync(shape: 4, index: 2, expected: "20 ; 1");

    // an element of the String type, let-coerced to the Long variable.
    [TestMethod]
    public async Task AStringElementOfDigits_IsLetCoercedToTheVariable()
        => await AssertSupportCallAsync(shape: 1, index: 0, expected: "10 ; 1");

    // an element of the String type that does not read as a number: error 13, at the Let's line.
    [TestMethod]
    public async Task AStringElementOfLetters_RaisesError13AtTheLetsLine()
        => await AssertSupportCallAsync(shape: 2, index: 0, expected: "13 9 ; 1");

    // a scalar indexed: error 13, at the Let's line.
    [TestMethod]
    public async Task AScalarIndexed_RaisesError13AtTheLetsLine()
        => await AssertSupportCallAsync(shape: 3, index: 0, expected: "13 9 ; 1");

    // an index past the array's upper bound: error 9, at the Let's line.
    [TestMethod]
    public async Task AnIndexOutOfTheArraysBounds_RaisesError9AtTheLetsLine()
        => await AssertSupportCallAsync(shape: 0, index: 3, expected: "9 9 ; 1");
}
