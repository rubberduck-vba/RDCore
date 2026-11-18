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
        ("Holder", ModuleWorkspace.ClassModule("Holder", "Option Explicit",
            "Public Property Get Value() As Long",
            "Attribute Value.VB_UserMemId = 0",
            "    Value = 42",
            "End Property")),
    ];

    // the lines that make a Collection, in C, of items that are not scalars, but the last: an array of Variants, an array of Longs, a Variant holding an
    // array of Variants that hold Strings, an object whose default member is a Long, and an Integer.
    private static readonly string[] Unscalar =
    [
        "    Dim C As Collection",
        "    Set C = New Collection",
        "    C.Add Array(1, 2, 3)",
        "    Dim L(1 To 2) As Long",
        "    L(1) = 10",
        "    L(2) = 20",
        "    C.Add L",
        "    Dim A As Variant",
        "    A = Array(\"x\", \"y\")",
        "    C.Add A",
        "    C.Add New Holder",
        "    C.Add 7",
    ];

    // the program run both ways, printing expected; the procedure named compiled, its activations handed to the interpreter walkBacks times: once for
    // each that reads an item that is not a scalar, which the interpreter assigns, and then runs the rest of the activation.
    private static async Task AssertHandedOverAsync(string program, string procedure, string expected, int walkBacks)
    {
        var compiled = await AssertAlikeAsync(program, Classes, expected);
        Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
        Assert.IsNotNull(result.Body, $"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(walkBacks, result.Body.WalkBacks, $"{procedure}'s walk-backs");
        }
    }

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

    // the Count of a Collection variable that holds Nothing: error 91, which the interpreter raises.
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

    // an item that is not a scalar, let to a Variant: an array, of Variants or of Longs, is copied into it as it is (MS-VBAL §5.5.1.2.12), and an
    // object is let as its default member's value (MS-VBAL §5.6.2.2). The interpreter assigns each; the Integer is compiled code's.
    [TestMethod]
    public async Task AnItemNoScalarIs_LetToAVariant_IsAssignedByTheInterpreter()
        => await AssertHandedOverAsync(Program([
            "Public Sub Main()",
            .. Unscalar,
            "    Debug.Print Kind(C, 1) & \" \" & Kind(C, 2) & \" \" & Kind(C, 3) & \" \" & Kind(C, 4) & \" \" & Kind(C, 5)",
            "End Sub",
            "Private Function Kind(ByVal C As Collection, ByVal N As Long) As Long",
            "    Dim V As Variant",
            "    V = C(N)",
            "    Kind = VarType(V)",
            "End Function"]), "Kind", expected: "8204 8195 8204 3 2", walkBacks: 4);

    // an element of an array item, read once the item is let to a Variant.
    [TestMethod]
    public async Task AnElementOfAnArrayItem_IsReadOnceTheItemIsLet()
        => await AssertHandedOverAsync(Program([
            "Public Sub Main()",
            .. Unscalar,
            "    Debug.Print Second(C, 1) & Second(C, 3)",
            "End Sub",
            "Private Function Second(ByVal C As Collection, ByVal N As Long) As Variant",
            "    Dim V As Variant",
            "    V = C.Item(N)",
            "    Second = V(1)",
            "End Function"]), "Second", expected: "2y", walkBacks: 2);

    // an item that is not a scalar, let to a Double: an array is error 13, which the interpreter raises, and the caller's handler handles; an object is its
    // default member's value.
    [TestMethod]
    public async Task AnArrayItem_LetToADouble_RaisesError13()
        => await AssertHandedOverAsync(Program([
            "Public Sub Main()",
            .. Unscalar,
            "    On Error Resume Next",
            "    Debug.Print Number(C, 5) + Number(C, 4)",
            "    Debug.Print Number(C, 1)",
            "    Debug.Print Err.Number",
            "End Sub",
            "Private Function Number(ByVal C As Collection, ByVal N As Long) As Double",
            "    Number = C(N)",
            "End Function"]), "Number", expected: "49 ; 13", walkBacks: 2);

    // an item Set to a Variant: an object is the object, and an array is error 13, which the interpreter raises (MS-VBAL §5.4.3.9, §5.5.2.2.2): the one
    // activation handed to it.
    [TestMethod]
    public async Task AnItemSetToAVariant_IsTheObject_AndAnArrayRaisesError13()
        => await AssertHandedOverAsync(Program([
            "Public Sub Main()",
            .. Unscalar,
            "    On Error Resume Next",
            "    Debug.Print Held(C, 4)",
            "    Debug.Print Held(C, 1)",
            "    Debug.Print Err.Number",
            "End Sub",
            "Private Function Held(ByVal C As Collection, ByVal N As Long) As String",
            "    Dim V As Variant",
            "    Set V = C(N)",
            "    Held = TypeName(V)",
            "End Function"]), "Held", expected: "Holder ; 13", walkBacks: 1);

    // an Item of a type other than a scalar one, let and set (MS-VBAL §5.4.3.8, §5.4.3.9): an array a Variant held as it was added, a Long array
    // copied into one, a Collection, and a scalar; each assigned as the interpreter assigns it, the error of a let or a set it cannot make included.
    [TestMethod]
    [DataRow("Dim Found As Variant ;; Found = C(\"a\") ;; Probe = TypeName(Found) & UBound(Found)", DisplayName = "a Variant let a Variant's array")]
    [DataRow("Dim Found As Variant, K As Variant ;; K = \"a\" ;; Found = C(K) ;; Probe = CStr(Found(1))",
        DisplayName = "a Variant let an array by a Variant key")]
    [DataRow("Dim Found As Variant ;; Found = C.Item(\"l\") ;; Probe = TypeName(Found) & Found(2)", DisplayName = "a Variant let a Long array, named")]
    [DataRow("Dim Found As Double ;; Found = C(\"a\") ;; Probe = CStr(Found)", DisplayName = "a Double let an array")]
    [DataRow("Dim Found As String ;; Found = C(\"l\") ;; Probe = Found", DisplayName = "a String let a Long array")]
    [DataRow("Dim Found As Variant ;; Found = C(\"o\") ;; Probe = TypeName(Found)", DisplayName = "a Variant let a Collection")]
    [DataRow("Dim Found As Variant ;; Set Found = C(\"o\") ;; Probe = TypeName(Found) & Found.Count", DisplayName = "a Variant set a Collection")]
    [DataRow("Dim Found As Object ;; Set Found = C(\"o\") ;; Probe = CStr(Found.Count)", DisplayName = "an Object set a Collection")]
    [DataRow("Dim Found As Variant ;; Set Found = C(\"a\") ;; Probe = TypeName(Found)", DisplayName = "a Variant set an array")]
    [DataRow("Dim Found As Long ;; Found = C(\"n\") ;; Probe = CStr(Found)", DisplayName = "a Long let a scalar")]
    public async Task AnItemThatIsNoScalar_IsAssignedAsTheInterpreterAssignsIt(string body)
        => _ = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As Collection, Inner As Collection, V As Variant, L(1 To 2) As Long",
            "    Set C = New Collection",
            "    Set Inner = New Collection",
            "    Inner.Add 1",
            "    Inner.Add 2",
            "    V = Array(10, 20, 30)",
            "    L(1) = 5",
            "    L(2) = 6",
            "    C.Add V, \"a\"",
            "    C.Add Inner, \"o\"",
            "    C.Add 7, \"n\"",
            "    C.Add L, \"l\"",
            "    On Error Resume Next",
            "    Debug.Print Probe(C)",
            "    Debug.Print Err.Number",
            "End Sub",
            "Private Function Probe(ByVal C As Collection) As String",
            "    " + string.Join("\r\n    ", body.Split(" ;; ")),
            "End Function"), Classes);
}
