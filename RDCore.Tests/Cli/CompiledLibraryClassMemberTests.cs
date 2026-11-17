using RDCore.Runtime.Compiled;
using RDCore.Runtime.LanguageCore.Instructions;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The <c>Count</c> and the <c>Item</c> of a <c>VBA.Collection</c> (<strong>MS-VBAL §6.1.3.1.1</strong>), called by compiled code with no statement stepped,
/// on the object an object variable holds, an object field of a record holds, or the <c>With</c> block holds: named, or <c>Item</c> as the default member
/// the arguments of an index expression are given to (<strong>MS-VBAL §5.6.13</strong>). A program runs as it does in the interpreter, the errors
/// <c>Item</c> raises included.
/// </summary>
[TestClass]
public sealed class CompiledLibraryClassMemberTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Sequence", ModuleWorkspace.ClassModule("Sequence", "Option Explicit",
            "Private Type TSequence",
            "    Values As Collection",
            "    Index As Long",
            "End Type",
            "Private this As TSequence",
            "Private Sub Class_Initialize()",
            "    Set this.Values = New Collection",
            "    this.Values.Add 1.5",
            "    this.Values.Add 2.5",
            "    this.Values.Add 4",
            "End Sub",
            "Public Property Get NextValue() As Double",
            "    If this.Index = this.Values.Count Then this.Index = 0",
            "    this.Index = this.Index + 1",
            "    NextValue = this.Values(this.Index)",
            "End Property")),
    ];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the lines that make a Collection of 1.5, 2.5 and 4, keyed "a", "b" and "c", in C.
    private static readonly string[] Filled =
    [
        "    Dim C As Collection",
        "    Set C = New Collection",
        "    C.Add 1.5, \"a\"",
        "    C.Add 2.5, \"b\"",
        "    C.Add 4, \"c\"",
    ];

    // the program run both ways, printing expected; the procedure named compiled with no statement stepped but a With statement, which is the
    // interpreter's (it evaluates and holds the block's object), and, when it raises nothing, with no instruction handed to the interpreter but the With
    // statements it runs, withs of them.
    private static async Task AssertNoneSteppedAsync(string program, string procedure, string expected, bool raises = false, int withs = 0)
    {
        var compiled = await AssertAlikeAsync(program, Classes, expected);
        Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
        Assert.IsNotNull(result.Body, $"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        var stepped = result.Stepped.Where(reason => reason is not { Kind: IneligibilityKind.Instruction, Detail: nameof(InstructionKind.With) }).ToList();
        Assert.IsEmpty(stepped, $"{procedure}: {string.Join("; ", stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        if (!raises)
        {
            Assert.AreEqual(withs, result.Body.Recoveries, $"{procedure}'s recoveries");
        }
    }

    // the Count of a Collection a record field holds, and its Item, the default member, given a field of the record as its index.
    [TestMethod]
    public async Task TheCountAndTheDefaultMemberOfACollectionARecordFieldHolds_AreCompiled()
        => await AssertNoneSteppedAsync(Program(
            "Public Sub Main()",
            "    Dim S As Sequence, I As Long, Total As Double",
            "    Set S = New Sequence",
            "    For I = 1 To 5",
            "        Total = Total + S.NextValue",
            "    Next",
            "    Debug.Print Total",
            "End Sub"), "NextValue", expected: "12");

    // the Count of a Collection an object variable holds, as the bound of a loop, and its Item, the default member, given the loop's counter.
    [TestMethod]
    public async Task TheCountAndTheDefaultMemberOfACollectionAVariableHolds_AreCompiled()
        => await AssertNoneSteppedAsync(Program([
            "Public Sub Main()",
            .. Filled,
            "    Debug.Print Total(C)",
            "End Sub",
            "Private Function Total(ByVal C As Collection) As Double",
            "    Dim I As Long",
            "    For I = 1 To C.Count",
            "        Total = Total + C(I)",
            "    Next",
            "End Function"]), "Total", expected: "8");

    // the Item of a Collection named, given a key, and given a position.
    [TestMethod]
    public async Task TheItemOfACollectionNamed_IsCompiled()
        => await AssertNoneSteppedAsync(Program([
            "Public Sub Main()",
            .. Filled,
            "    Debug.Print Pair(C)",
            "End Sub",
            "Private Function Pair(ByVal C As Collection) As Double",
            "    Pair = C.Item(\"b\") * 10 + C.Item(1)",
            "End Function"]), "Pair", expected: "26.5");

    // the Count and the Item of the Collection of a With block.
    [TestMethod]
    public async Task TheCountAndTheItemOfTheCollectionOfAWithBlock_AreCompiled()
        => await AssertNoneSteppedAsync(Program([
            "Public Sub Main()",
            .. Filled,
            "    Debug.Print Last(C)",
            "End Sub",
            "Private Function Last(ByVal C As Collection) As Long",
            "    With C",
            "        Last = .Item(.Count)",
            "    End With",
            "End Function"]), "Last", expected: "4", withs: 1);

    // the Item at a position no member is at: error 9, which the interpreter raises, and the caller's handler handles.
    [TestMethod]
    public async Task TheItemAtAPositionNoMemberIsAt_RaisesError9()
        => await AssertNoneSteppedAsync(Program([
            "Public Sub Main()",
            .. Filled,
            "    On Error Resume Next",
            "    Debug.Print Picked(C, 4)",
            "    Debug.Print Err.Number",
            "End Sub",
            "Private Function Picked(ByVal C As Collection, ByVal N As Long) As Double",
            "    Picked = C(N)",
            "End Function"]), "Picked", expected: "9", raises: true);

    // the Item of a key no member has: error 5, which the interpreter raises, and the caller's handler handles.
    [TestMethod]
    public async Task TheItemOfAKeyNoMemberHas_RaisesError5()
        => await AssertNoneSteppedAsync(Program([
            "Public Sub Main()",
            .. Filled,
            "    On Error Resume Next",
            "    Debug.Print Keyed(C, \"z\")",
            "    Debug.Print Err.Number",
            "End Sub",
            "Private Function Keyed(ByVal C As Collection, ByVal Key As String) As Double",
            "    Keyed = C(Key)",
            "End Function"]), "Keyed", expected: "5", raises: true);

    // the Count of a Collection a variable holds Nothing of: error 91, which the interpreter raises.
    [TestMethod]
    public async Task TheCountOfNothing_RaisesError91()
        => await AssertNoneSteppedAsync(Program(
            "Public Sub Main()",
            "    Dim C As Collection",
            "    On Error Resume Next",
            "    Debug.Print Counted(C)",
            "    Debug.Print Err.Number",
            "End Sub",
            "Private Function Counted(ByVal C As Collection) As Long",
            "    Counted = C.Count",
            "End Function"), "Counted", expected: "91", raises: true);
}
