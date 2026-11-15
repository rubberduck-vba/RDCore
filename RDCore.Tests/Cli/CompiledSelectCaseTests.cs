using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Select Case</c> of a scalar selector, compiled: the selector evaluated once into a local, and each <c>Case</c> compared on
/// payloads as the interpreter compares it (MS-VBAL §5.4.2.10, RD-VBAL §5.4.2.10). A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledSelectCaseTests
{
    [TestMethod]
    public async Task EveryKindOfClause_IsComparedInCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Sorted()",
            "End Sub",
            "Private Function Sorted() As Long",
            "    Dim I As Long",
            "    For I = -2 To 12",
            "        Select Case I",
            "            Case 0",
            "                Sorted = Sorted + 1",
            "            Case 1, 2",
            "                Sorted = Sorted + 10",
            "            Case 3 To 5, 7",
            "                Sorted = Sorted + 100",
            "            Case Is > 9, Is < -1",
            "                Sorted = Sorted + 1000",
            "            Case Else",
            "                Sorted = Sorted + 10000",
            "        End Select",
            "    Next",
            "End Function"), "Sorted", recoveries: 0);

    // the selector is evaluated once, at its Select: what the Case blocks do to what it was read from changes nothing it is compared with.
    [TestMethod]
    public async Task TheSelector_IsEvaluatedOnce()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Once(1); Once(2)",
            "End Sub",
            "Private Function Once(ByVal N As Long) As Long",
            "    Select Case N",
            "        Case 1",
            "            N = 2",
            "            Once = 10",
            "        Case 2",
            "            Once = 20",
            "    End Select",
            "    Once = Once + N",
            "End Function"), "Once", recoveries: 0);

    // Select Case True: each Case is a condition, compared with True.
    [TestMethod]
    public async Task SelectCaseTrue_ComparesEachCondition()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Graded(3); Graded(55); Graded(95)",
            "End Sub",
            "Private Function Graded(ByVal Score As Long) As Long",
            "    Select Case True",
            "        Case Score >= 90",
            "            Graded = 1",
            "        Case Score >= 50, Score = 3",
            "            Graded = 2",
            "        Case Else",
            "            Graded = 3",
            "    End Select",
            "End Function"), "Graded", recoveries: 0);

    // a value is converted to the type of the selector (RD-VBAL §5.4.2.10).
    [TestMethod]
    public async Task AValue_IsComparedInTheTypeOfTheSelector()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Matched(2.5); Matched(3); Matched(4.75); Matched(1)",
            "End Sub",
            "Private Function Matched(ByVal X As Double) As Long",
            "    Select Case X",
            "        Case 3",
            "            Matched = 1",
            "        Case 2.5",
            "            Matched = 2",
            "        Case 4.5 To 5",
            "            Matched = 3",
            "    End Select",
            "End Function"), "Matched", recoveries: 0);

    // an integer is compared with a Single selector at its exact value, and not as a Single (RD-VBAL §5.4.2.10).
    [TestMethod]
    public async Task AnInteger_IsComparedWithASingleSelector_AtItsExactValue()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Matched(3); Matched(4.5); Matched(16777217)",
            "End Sub",
            "Private Function Matched(ByVal X As Single) As Long",
            "    Select Case X",
            "        Case 3",
            "            Matched = 1",
            "        Case 4 To 5",
            "            Matched = 2",
            "        Case 16777217",
            "            Matched = 3",
            "        Case Else",
            "            Matched = 4",
            "    End Select",
            "End Function"), "Matched", recoveries: 0);

    // a Double value is no Single that compiled code makes - that would round - so the Case is the interpreter's, as its own payload comparison declines it.
    [TestMethod]
    public async Task AFractionComparedWithASingleSelector_IsComparedByTheInterpreter()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Matched(2.5); Matched(3)",
            "End Sub",
            "Private Function Matched(ByVal X As Single) As Long",
            "    Select Case X",
            "        Case 3",
            "            Matched = 1",
            "        Case 2.5",
            "            Matched = 2",
            "    End Select",
            "End Function"));

        Assert.IsNotNull(compiled["Matched"].Body);
    }

    [TestMethod]
    public async Task ACurrencySelector_IsComparedAsACurrency()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Priced(1.5); Priced(2); Priced(1.25)",
            "End Sub",
            "Private Function Priced(ByVal C As Currency) As Long",
            "    Select Case C",
            "        Case 1.5",
            "            Priced = 1",
            "        Case Is >= 2",
            "            Priced = 2",
            "        Case Else",
            "            Priced = 3",
            "    End Select",
            "End Function"), "Priced", recoveries: 0);

    [TestMethod]
    public async Task ASelectInsideASelect_ComparesItsOwnSelector()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Nested(1, 2); Nested(2, 1); Nested(1, 1)",
            "End Sub",
            "Private Function Nested(ByVal A As Long, ByVal B As Long) As Long",
            "    Select Case A",
            "        Case 1",
            "            Select Case B",
            "                Case 1",
            "                    Nested = 11",
            "                Case Else",
            "                    Nested = 12",
            "            End Select",
            "            Nested = Nested + A",
            "        Case Else",
            "            Nested = 20",
            "    End Select",
            "End Function"), "Nested", recoveries: 0);

    // a value whose conversion to the selector's type is not exact fails the Case in compiled code: the interpreter compares it boxed, as it does when its own
    // payload comparison declines - and raises the overflow the conversion is.
    [TestMethod]
    public async Task AValueNotExactInTheSelectorsType_IsComparedByTheInterpreter()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    On Error GoTo Caught",
            "    Debug.Print Narrow(1)",
            "    Exit Sub",
            "Caught:",
            "    Debug.Print \"caught \" & Err.Number",
            "End Sub",
            "Private Function Narrow(ByVal I As Integer) As Long",
            "    Select Case I",
            "        Case 100000",
            "            Narrow = 1",
            "        Case Else",
            "            Narrow = 2",
            "    End Select",
            "End Function"));

        Assert.IsNotNull(compiled["Narrow"].Body);
    }

    // an error in a Case, gone past: the interpreter compares the Case alone, with the selector compiled code gave it, and compiled code goes on where it
    // leaves the activation, the selector read back.
    [TestMethod]
    public async Task AnErrorInACase_GonePast_GoesOnCompiled_WithTheSelector()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Tolerant(0); Tolerant(1)",
            "End Sub",
            "Private Function Tolerant(ByVal Z As Long) As Long",
            "    On Error Resume Next",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        Select Case I",
            "            Case 10 \\ Z",
            "                Tolerant = Tolerant + 100",
            "            Case 2",
            "                Tolerant = Tolerant + 10",
            "            Case Else",
            "                Tolerant = Tolerant + 1",
            "        End Select",
            "    Next",
            "End Function"), "Tolerant", recoveries: 3);

    // a selector compiled code does not evaluate - a Double rounded to a Long, which only the boxed coercion does - the interpreter evaluates; it is compared
    // where it is, the interpreter's from there on when it evaluated it boxed.
    [TestMethod]
    public async Task ASelectorTheInterpreterEvaluates_IsComparedWhereItIs()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Rounded(2.6); Rounded(1)",
            "End Sub",
            "Private Function Rounded(ByVal D As Double) As Long",
            "    On Error Resume Next",
            "    Select Case D \\ 1",
            "        Case 3",
            "            Rounded = 3",
            "        Case Else",
            "            Rounded = 1",
            "    End Select",
            "End Function"));

        Assert.IsNotNull(compiled["Rounded"].Body);
    }

    // an error in the selector, gone past: the program goes on past the Select, as MS-VBA's does.
    [TestMethod]
    public async Task AnErrorInTheSelector_GonePast_GoesOnPastTheSelect()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Skipped(0); Skipped(2)",
            "End Sub",
            "Private Function Skipped(ByVal Z As Long) As Long",
            "    On Error Resume Next",
            "    Select Case 10 \\ Z",
            "        Case 5",
            "            Skipped = 5",
            "        Case Else",
            "            Skipped = 1",
            "    End Select",
            "    Skipped = Skipped + 100",
            "End Function"), "Skipped", recoveries: 1);

    // a Variant selector is compared as a Variant, its values not converted to its type: the interpreter steps the Select and its Cases, and compiled code
    // does the rest.
    [TestMethod]
    public async Task AVariantSelector_IsTheInterpreters()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Varied(\"1\")",
            "End Sub",
            "Private Function Varied(ByVal V As Variant) As Long",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        Select Case V",
            "            Case 1",
            "                Varied = Varied + 1",
            "        End Select",
            "    Next",
            "End Function"), "Varied", recoveries: 4);

        Assert.IsTrue(compiled.Stepped.Any(reason => reason is { Kind: IneligibilityKind.VariantOperand, Detail: "Select Case" }),
            string.Join("; ", compiled.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }
}
