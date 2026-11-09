using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A function of the <c>String</c> type, compiled: its result is assigned the text it is given where its frame holds it, as the interpreter assigns it
/// (<strong>MS-VBAL §5.3.1</strong>), and a <c>Byte</c>, an <c>Integer</c> or a <c>Long</c> joined to a <c>String</c> is written as its let-coercion
/// writes it (<strong>MS-VBAL §5.5.1.2.4</strong>). A program runs the same either way.
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
