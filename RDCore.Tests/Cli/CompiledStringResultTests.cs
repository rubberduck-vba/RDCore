using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A function of the <c>String</c> type, compiled: its result is assigned the text it is given where its frame holds it, as the interpreter assigns it
/// (<strong>MS-VBAL §5.3.1</strong>), and a <c>Byte</c>, an <c>Integer</c> or a <c>Long</c> joined to a <c>String</c> is written as its let-coercion
/// writes it (<strong>MS-VBAL §5.5.1.2.4</strong>), and <c>Chr$</c> of one is the character the library makes of it
/// (<strong>MS-VBAL §6.1.2.11.1.4</strong>). A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledStringResultTests
{
    private static readonly (string Name, string Source)[] Coordinates =
    [
        ("GridCoord", ModuleWorkspace.ClassModule("GridCoord", "Option Explicit",
            "Private Type TCoord",
            "    X As Long",
            "    Y As Long",
            "End Type",
            "Private this As TCoord",
            "Public Sub Init(ByVal X As Long, ByVal Y As Long)",
            "    this.X = X",
            "    this.Y = Y",
            "End Sub",
            "Public Function ToString() As String",
            "    ToString = \"(\" & this.X & \",\" & this.Y & \")\"",
            "End Function",
            "Public Function ToA1String() As String",
            "    ToA1String = Chr$(64 + this.X) & this.Y",
            "End Function")),
    ];

    // GridCoord through an interface, whose member returns what the class's own member does, called on Me.
    private static readonly (string Name, string Source)[] Interfaced =
    [
        ("IGridCoord", ModuleWorkspace.ClassModule("IGridCoord", "Option Explicit",
            "Public Function ToString() As String",
            "End Function")),
        ("GridCoord", ModuleWorkspace.ClassModule("GridCoord", "Option Explicit",
            "Implements IGridCoord",
            "Private Type TCoord",
            "    X As Long",
            "    Y As Long",
            "End Type",
            "Private this As TCoord",
            "Public Sub Init(ByVal X As Long, ByVal Y As Long)",
            "    this.X = X",
            "    this.Y = Y",
            "End Sub",
            "Public Function ToString() As String",
            "    ToString = \"(\" & this.X & \",\" & this.Y & \")\"",
            "End Function",
            "Private Function IGridCoord_ToString() As String",
            "    IGridCoord_ToString = Me.ToString",
            "End Function")),
    ];

    private static void AssertCompiled(IReadOnlyDictionary<string, RDCore.Runtime.Compiled.CompilationResult> compiled, params string[] names)
    {
        foreach (var name in names)
        {
            Assert.IsTrue(compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            Assert.IsEmpty(result.Stepped, $"{name} steps: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        }
    }

    // a String function of a class joins the Long fields of its record into the text it returns, a negative one with its minus sign.
    [TestMethod]
    public async Task AStringResultOfLongFieldsJoined_IsTheTextTheyMake()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As New GridCoord",
            "    Dim N As Long",
            "    For N = 1 To 3",
            "        C.Init N, -2 * N",
            "        Debug.Print C.ToString",
            "    Next",
            "End Sub"), classes: Coordinates, expected: "(3,-6)"), "ToString");

    // a Byte, an Integer and a Long joined to Strings are their digits; a String result never assigned is the zero-length string, and one assigned twice
    // holds the second.
    [TestMethod]
    public async Task WholeNumbersJoined_AndAResultAssignedTwiceOrNever_AreAsTheInterpreterHasThem()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Label(\"b\", 255, -32768, 2147483647); Len(Unassigned(1)); Twice(7)",
            "End Sub",
            "Private Function Label(ByVal Name As String, ByVal B As Byte, ByVal I As Integer, ByVal L As Long) As String",
            "    Label = Name & \":\" & B & \"/\" & I & \"/\" & L & \"/\" & 12",
            "End Function",
            "Private Function Unassigned(ByVal N As Long) As String",
            "    N = N + 1",
            "End Function",
            "Private Function Twice(ByVal N As Long) As String",
            "    Twice = \"once\"",
            "    Twice = N & \"x\"",
            "End Function"), expected: "b:255/-32768/2147483647/12 0 7x"), "Label", "Twice");

    // a String variable assigned a whole number holds its digits.
    [TestMethod]
    public async Task AStringVariableAssignedAWholeNumber_HoldsItsDigits()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Digits(-45)",
            "End Sub",
            "Private Function Digits(ByVal N As Integer) As String",
            "    Dim S As String",
            "    S = N",
            "    Digits = S & \"|\" & S",
            "End Function"), expected: "-45|-45"), "Digits");

    // a String result assigned what a member of Me returns: the interface's member returns the class's own, called on the object it is.
    [TestMethod]
    public async Task AStringResultAssignedAMemberOfMe_IsWhatTheMemberReturns()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As New GridCoord",
            "    Dim G As IGridCoord",
            "    Dim N As Long",
            "    For N = 1 To 3",
            "        C.Init N, N * N",
            "        Set G = C",
            "        Debug.Print G.ToString",
            "    Next",
            "End Sub"), classes: Interfaced, expected: "(3,9)"), "ToString", "IGridCoord_ToString");

    // a String result, and a Variant, assigned what a String function returns: the text it is, and a Variant that holds a String.
    [TestMethod]
    public async Task AResultAssignedWhatAStringFunctionReturns_IsItsText()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(4); \"|\"; Kind(5)",
            "End Sub",
            "Private Function Outer(ByVal N As Long) As String",
            "    Outer = Inner(N)",
            "End Function",
            "Private Function Kind(ByVal N As Long) As Long",
            "    Dim V As Variant",
            "    V = Inner(N)",
            "    Kind = VarType(V) * 10 + N",
            "End Function",
            "Private Function Inner(ByVal N As Long) As String",
            "    Inner = \"#\" & N",
            "End Function"), expected: "#4| 85"), "Outer", "Inner");

    // a String variable assigned what a String function returns, and what a Long function returns, which is its digits.
    [TestMethod]
    public async Task AStringVariableAssignedWhatAFunctionReturns_HoldsItsText()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Twice(4)",
            "End Sub",
            "Private Function Twice(ByVal N As Long) As String",
            "    Dim S As String",
            "    Dim T As String",
            "    S = Inner(N)",
            "    T = Squared(N)",
            "    Twice = S & S & \"/\" & T",
            "End Function",
            "Private Function Inner(ByVal N As Long) As String",
            "    Inner = \"#\" & N",
            "End Function",
            "Private Function Squared(ByVal N As Long) As Long",
            "    Squared = N * N",
            "End Function"), expected: "#4#4/16"), "Twice", "Inner");

    // a whole number given by value to a String parameter is given its digits.
    [TestMethod]
    public async Task AWholeNumberGivenToAStringParameter_IsGivenItsDigits()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Measure(-12); Measure(7)",
            "End Sub",
            "Private Function Measure(ByVal N As Long) As Long",
            "    Measure = Width(N * 10)",
            "End Function",
            "Private Function Width(ByVal S As String) As Long",
            "    Width = Len(S)",
            "End Function"), expected: " 4  2"), "Measure");

    // Chr$ of a whole number is the character the code names, joined to the text of a field.
    [TestMethod]
    public async Task ChrOfAWholeNumber_IsTheCharacterItNames()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As New GridCoord",
            "    Dim N As Long",
            "    For N = 1 To 3",
            "        C.Init N, 10 - N",
            "        Debug.Print C.ToA1String",
            "    Next",
            "End Sub"), classes: Coordinates, expected: "C7"), "ToA1String");

    // Chr$ of an Integer and of a Byte, joined in a loop: a String variable that grows, then the result.
    [TestMethod]
    public async Task ChrJoinedInALoop_IsTheTextItMakes()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Letters(5, 97)",
            "End Sub",
            "Private Function Letters(ByVal Count As Integer, ByVal First As Byte) As String",
            "    Dim S As String",
            "    Dim I As Integer",
            "    For I = 0 To Count - 1",
            "        S = S & Chr$(65 + I)",
            "    Next",
            "    Letters = S & Chr$(First)",
            "End Function"), expected: "ABCDEa"), "Letters");

    // a code that names no character is error 5, raised where the interpreter raises it.
    [TestMethod]
    public async Task ChrOfACodeThatNamesNoCharacter_IsError5()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    On Error Resume Next",
            "    Debug.Print Named(70000)",
            "    Debug.Print \"err\"; Err.Number",
            "End Sub",
            "Private Function Named(ByVal Code As Long) As String",
            "    Named = \"[\" & Chr$(Code) & \"]\"",
            "End Function"), expected: "err 5"), "Named");

    // a Double, a Currency, a Boolean and a Date are written in the regional settings: the interpreter assigns them, and the program runs the same.
    [TestMethod]
    public async Task OtherScalarsJoined_AreTheInterpretersToWrite()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Mixed(1.5)",
            "End Sub",
            "Private Function Mixed(ByVal D As Double) As String",
            "    Dim C As Currency",
            "    C = 2.25",
            "    Mixed = D & \"/\" & C & \"/\" & (D > 1)",
            "End Function"), expected: "1.5/2.25/True");
}
