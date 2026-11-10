using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>String</c> parameter or variable of variable length, compiled: held in the cell its frame holds it in, whose text compiled
/// code reads, joins (MS-VBAL §5.6.9.4, §5.6.5), compares under the module's comparison mode (MS-VBAL §5.6.9.5) and writes as it is assigned, as the
/// interpreter's evaluation of Strings does. A String it reads no text of is the interpreter's. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledStringTests
{
    // each answer a comparison row gives is a bit of the number it prints, in the order they are given: any wrong answer is another number.

    // Strings joined in a loop, by & and by + of two Strings, into a parameter passed by reference: the caller's variable, which it prints.
    [TestMethod]
    public async Task StringsJoinedInALoop_AreWrittenInTheCellOfTheVariable()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim S As String",
            "    Build S",
            "    Debug.Print S; Len(S)",
            "End Sub",
            "Private Sub Build(ByRef S As String)",
            "    Dim T As String",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        T = T + \"ab\"",
            "        S = S & \"[\" & T & \"]\"",
            "    Next",
            "    S = (\"<\" & S) & \">\"",
            "End Sub"), "Build", recoveries: 0);

    // =, <>, <, <=, > and >= of Strings that differ in case, and in length, under Option Compare Text: "abc" is "ABC" (100101), "a" is before "B" and
    // "ab" before "abc" (011111), and "" is "" (100101).
    [TestMethod]
    public async Task StringsCompared_UnderOptionCompareText_IgnoreCase()
        => await AssertAlikeAsync(Comparisons("Option Compare Text"), "Compared", recoveries: 0, expected: " 37  31  31  37 ");

    // the same comparisons under the default, Option Compare Binary: "abc" is after "ABC" and "a" after "B" (010000), as their codes are.
    [TestMethod]
    public async Task StringsCompared_UnderOptionCompareBinary_AreComparedByCode()
        => await AssertAlikeAsync(Comparisons("Option Compare Binary"), "Compared", recoveries: 0, expected: " 16  16  31  37 ");

    // a comparison is made under the comparison mode of the module of the procedure that makes it (MS-VBAL §5.6.9.5), the callee's: "a" = "A" is True in
    // the Text one, and False in the Binary one, whichever module calls the other.
    [TestMethod]
    [DataRow("Option Compare Binary", "Option Compare Text", " 1  0 ", DisplayName = "a Text class called from a Binary module")]
    [DataRow("Option Compare Text", "Option Compare Binary", " 0  1 ", DisplayName = "a Binary class called from a Text module")]
    public async Task StringsCompared_InAnotherModule_AreComparedUnderItsMode(string callerMode, string calleeMode, string expected)
    {
        var program = callerMode + "\r\n" + Program(
            "Public Sub Main()",
            "    Dim C As New Comparer",
            "    Debug.Print C.Matched(\"a\", \"A\"); Equal(\"a\", \"A\")",
            "End Sub",
            "Private Function Equal(ByVal L As String, ByVal R As String) As Long",
            "    If L = R Then Equal = 1",
            "End Function");
        (string Name, string Source)[] classes =
        [
            ("Comparer", ModuleWorkspace.ClassModule("Comparer", "Option Explicit", calleeMode,
                "Public Function Matched(ByVal L As String, ByVal R As String) As Long", "    If L = R Then Matched = 1", "End Function")),
        ];

        await AssertAlikeAsync(program, "Matched", recoveries: 0, classes: classes, expected: expected);
        await AssertAlikeAsync(program, "Equal", recoveries: 0, classes: classes, expected: expected);
    }

    private static string Comparisons(string mode) => mode + "\r\n" + Program(
        "Public Sub Main()",
        "    Debug.Print Compared(\"abc\", \"ABC\"); Compared(\"a\", \"B\"); Compared(\"ab\", \"abc\"); Compared(\"\", \"\")",
        "End Sub",
        "Private Function Compared(ByVal L As String, ByVal R As String) As Long",
        "    If L = R Then Compared = Compared * 2 + 1 Else Compared = Compared * 2",
        "    If L <> R Then Compared = Compared * 2 + 1 Else Compared = Compared * 2",
        "    If L < R Then Compared = Compared * 2 + 1 Else Compared = Compared * 2",
        "    If L <= R Then Compared = Compared * 2 + 1 Else Compared = Compared * 2",
        "    If R > L Then Compared = Compared * 2 + 1 Else Compared = Compared * 2",
        "    If R >= L & \"\" Then Compared = Compared * 2 + 1 Else Compared = Compared * 2",
        "End Function");

    // vbNullString is a constant, which compiled code assigns, reads as the zero-length string it compares equal to, joins as one, and copies as the null
    // pointer it is. The two Ifs that call StrPtr of a String are the interpreter's: 2.
    [TestMethod]
    public async Task VbNullString_IsReadAsTheZeroLengthString_AndCopiedAsItIs()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Nulls()",
            "End Sub",
            "Private Function Nulls() As Long",
            "    Dim S As String",
            "    Dim T As String",
            "    Dim U As String",
            "    S = vbNullString",
            "    T = S",
            "    U = S & \"\"",
            "    If S = \"\" Then Nulls = Nulls * 2 + 1 Else Nulls = Nulls * 2",
            "    If StrPtr(T) = 0 Then Nulls = Nulls * 2 + 1 Else Nulls = Nulls * 2",
            "    If StrPtr(U) = 0 Then Nulls = Nulls * 2 + 1 Else Nulls = Nulls * 2",
            "End Function"), "Nulls", recoveries: 2);

    // a String that ends with a lone byte is joined byte by byte, by the interpreter: compiled code reads no text of it, and hands it each statement that does.
    // S = ChrB(65) and the two Lets that call LenB call a function of a String; T = S & "x" joins the lone byte; T = S & "!" is compiled: 4.
    [TestMethod]
    public async Task AStringEndingWithALoneByte_IsJoinedByTheInterpreter()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Odd()",
            "End Sub",
            "Private Function Odd() As Long",
            "    Dim S As String",
            "    Dim T As String",
            "    S = ChrB(65)",
            "    T = S & \"x\"",
            "    Odd = LenB(T)",
            "    S = \"even\"",
            "    T = S & \"!\"",
            "    Odd = Odd * 100 + LenB(T)",
            "End Function"), "Odd", recoveries: 4);

    // a fixed-length String pads or truncates what it is assigned: it is the interpreter's, and so is each statement that reads it. F is assigned twice
    // and read twice; T = "ab" and the If of T are compiled: 4.
    [TestMethod]
    public async Task AFixedLengthString_IsTheInterpretersToAssign()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Fixed()",
            "End Sub",
            "Private Function Fixed() As Long",
            "    Dim F As String * 3",
            "    Dim T As String",
            "    F = \"abcdef\"",
            "    T = F & \"!\"",
            "    If T = \"abc!\" Then Fixed = 1",
            "    T = \"ab\"",
            "    F = T",
            "    If F & \"\" = \"ab \" Then Fixed = Fixed + 2",
            "End Function"), "Fixed", recoveries: 4);

    // a parameter passed by value is a String of its own, which the callee assigns without the caller seeing it.
    [TestMethod]
    public async Task AStringPassedByValue_IsTheCalleesOwn()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim S As String",
            "    Dim I As Long",
            "    S = \"x\"",
            "    For I = 1 To 3",
            "        Debug.Print Scored(S); S",
            "        S = S & \"x\"",
            "    Next",
            "End Sub",
            "Private Function Scored(ByVal S As String) As Long",
            "    If S = \"xx\" Then Scored = 10",
            "    S = S & S",
            "    If S = \"xxxx\" Then Scored = Scored + 1",
            "End Function"), "Scored", recoveries: 0);
}
