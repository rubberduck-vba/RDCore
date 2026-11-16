using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Select Case</c> of a <c>String</c> selector of variable length, compiled: the selector evaluated once to its text, and each value of a <c>Case</c>
/// let-coerced to String and compared with it under the module's comparison mode (MS-VBAL §5.4.2.10, §5.6.9.5; RD-VBAL §5.4.2.10). A value compiled code
/// does not write as text is compared by the interpreter, given the selector. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledTextSelectCaseTests
{
    private static void AssertNoneStepped(CompilationResult result)
        => Assert.IsEmpty(result.Stepped, string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));

    private static string Sorting(string mode) => mode + "\r\n" + Program(
        "Public Sub Main()",
        "    Debug.Print Sorted(\"abc\"); Sorted(\"ABC\"); Sorted(\"b\"); Sorted(\"zz\"); Sorted(\"5\"); Sorted(\"\")",
        "End Sub",
        "Private Function Sorted(ByVal S As String) As Long",
        "    Dim I As Long",
        "    For I = 1 To 2",
        "        Select Case S",
        "            Case \"abc\"",
        "                Sorted = Sorted + 1",
        "            Case \"b\" To \"d\", 5",
        "                Sorted = Sorted + 10",
        "            Case Is > \"y\"",
        "                Sorted = Sorted + 100",
        "            Case vbNullString",
        "                Sorted = Sorted + 1000",
        "            Case Else",
        "                Sorted = Sorted + 10000",
        "        End Select",
        "    Next",
        "End Function");

    // every kind of clause, of String values and of a whole number, which is its digits: "ABC" is "abc" under Option Compare Text, and goes to the Case Else
    // under Option Compare Binary.
    [TestMethod]
    [DataRow("Option Compare Text", " 2  2  20  200  20  2000 ", DisplayName = "Option Compare Text")]
    [DataRow("Option Compare Binary", " 2  20000  20  200  20  2000 ", DisplayName = "Option Compare Binary")]
    public async Task EveryKindOfClause_IsComparedAsText_UnderTheModulesComparisonMode(string mode, string expected)
    {
        var result = await AssertAlikeAsync(Sorting(mode), "Sorted", recoveries: 0, expected: expected);
        AssertNoneStepped(result);
    }

    // the selector is evaluated each time its Select runs, to the text of the variable then: a Case block that assigns it changes what the next Select
    // compares.
    [TestMethod]
    public async Task TheSelector_IsEvaluatedEachTimeItsSelectRuns()
        => AssertNoneStepped(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Twice(\"a\"); Twice(\"b\")",
            "End Sub",
            "Private Function Twice(ByVal S As String) As String",
            "    Dim I As Long",
            "    Dim R As String",
            "    For I = 1 To 2",
            "        Select Case S",
            "            Case \"a\"",
            "                S = \"b\"",
            "                R = R & \"A\"",
            "            Case \"b\"",
            "                R = R & \"B\"",
            "        End Select",
            "    Next",
            "    Twice = R & S",
            "End Function"), "Twice", recoveries: 0, expected: "ABbBBb"));

    // a value that is neither a String nor a whole number (a Double, which the let-coercion writes in the regional settings of the session) is compared by
    // the interpreter, each time its Case is reached ("1.5", "q" and "w": three recoveries), given the selector compiled code evaluated; the Case after it is
    // compiled code's again, comparing the selector it reads back.
    [TestMethod]
    public async Task AValueCompiledCodeDoesNotWriteAsText_IsComparedByTheInterpreter()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Picked(\"1.5\"); Picked(\"x\"); Picked(\"q\"); Picked(\"w\")",
            "End Sub",
            "Private Function Picked(ByVal S As String) As Long",
            "    Select Case S",
            "        Case \"x\"",
            "            Picked = 1",
            "        Case 1.5",
            "            Picked = 2",
            "        Case \"q\"",
            "            Picked = 3",
            "        Case Else",
            "            Picked = 4",
            "    End Select",
            "End Function"), "Picked", recoveries: 3);

    // the end of a To range is a String variable, whose text compiled code reads: the range is compiled, and the comparison of the start is held while
    // the end's is made.
    [TestMethod]
    public async Task ARangeWhoseEndIsAVariable_IsCompiled()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Ranged(\"c\"); Ranged(\"e\"); Ranged(\"a\")",
            "End Sub",
            "Private Function Ranged(ByVal S As String) As Long",
            "    Dim Last As String",
            "    Last = \"d\"",
            "    Select Case S",
            "        Case \"b\" To Last",
            "            Ranged = 1",
            "        Case Else",
            "            Ranged = 2",
            "    End Select",
            "End Function"), "Ranged", recoveries: 0, expected: " 1  2  2 ");
        AssertNoneStepped(result);
    }

    // two Strings compared are a Boolean selector, compared on payloads as any scalar one is.
    [TestMethod]
    public async Task TwoStringsCompared_AreABooleanSelector()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Same(\"a\", \"a\"); Same(\"a\", \"b\")",
            "End Sub",
            "Private Function Same(ByVal L As String, ByVal R As String) As Long",
            "    Select Case L = R",
            "        Case True",
            "            Same = 1",
            "        Case Else",
            "            Same = 2",
            "    End Select",
            "End Function"), "Same", recoveries: 0, expected: " 1  2 ");
        AssertNoneStepped(result);
    }
}
