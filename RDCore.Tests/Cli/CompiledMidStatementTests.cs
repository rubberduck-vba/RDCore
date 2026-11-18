using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledIntrinsicTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The <c>Mid</c> statement of a <c>String</c> variable, in characters, run by compiled code (MS-VBAL §5.4.3.5): a program runs the same as the
/// interpreter runs it, and the statements compiled code leaves to the interpreter are the ones each row names.
/// </summary>
[TestClass]
public sealed class CompiledMidStatementTests
{
    // a start and a length given as a Long, as an Integer and as Len of the String (a Variant the statement let-coerces), and none; a value longer than
    // what is left, a zero length, vbNullString; and a String appended to before the statement changes it.
    [TestMethod]
    public async Task AMidStatementOfAStringVariable_IsCompiled()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Masked(\"RD-VBA platform\", 6)",
            "    Debug.Print Masked(\"ab\", 1)",
            "End Sub",
            "Private Function Masked(ByVal P As String, ByVal N As Long) As String",
            "    Dim I As Long, K As Integer, S As String, T As String",
            "    S = P",
            "    For I = 1 To N",
            "        K = I",
            "        Mid$(S, I, 1) = \"*\"",
            "        Mid(S, K + 1) = \"ab\"",
            "        Mid(S, I, 0) = \"zz\"",
            "        T = T & S & \";\"",
            "        T = T & S",
            "        Mid$(T, 1, 2) = \"<<\"",
            "    Next",
            "    Mid(S, 1) = vbNullString",
            "    Mid(S, Len(S), 5) = \"LAST\"",
            "    Masked = S & \"|\" & T",
            "End Function"), [("Masked", 0)]);

    // MS-VBAL §5.4.3.5: a start below 1 or past the end of the String, and a negative length, raise 5: the interpreter's, given the statement, which
    // Resume Next goes on after.
    [TestMethod]
    public async Task AMidStatementOutOfTheString_RaisesWhereTheInterpreterRaises()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Wrong(\"abc\")",
            "End Sub",
            "Private Function Wrong(ByVal P As String) As String",
            "    Dim S As String, E As Long",
            "    S = P",
            "    On Error Resume Next",
            "    Mid(S, 0) = \"x\"",
            "    E = Err.Number",
            "    Mid(S, 4) = \"x\"",
            "    E = E * 10 + Err.Number",
            "    Mid(S, 1, -1) = \"x\"",
            "    E = E * 10 + Err.Number",
            "    Wrong = S & E",
            "End Function"), [("Wrong", 3)]);

    // a String appended to: the statement changes what is appended to it, which is read where it is (U = T), and then a String assigned anew; the first
    // statement after the first append, and the ones after T is assigned, change the String the variable holds.
    [TestMethod]
    public async Task AMidStatementOfAStringAppendedTo_ChangesWhatIsAppended()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Built(5)",
            "End Sub",
            "Private Function Built(ByVal N As Long) As String",
            "    Dim I As Long, T As String, U As String",
            "    For I = 1 To N",
            "        T = T & \"ab\"",
            "        T = T & \"cd\"",
            "        Mid$(T, 1, 1) = CStr(I)",
            "        Mid(T, Len(T) - 1) = \"XY\"",
            "        If I = 3 Then U = T",
            "        If I = 4 Then T = \"re\"",
            "    Next",
            "    Mid(T, 2) = \"Z\"",
            "    Built = T & \"|\" & U",
            "End Function"), [("Built", 0)]);

    // a String appended to and changed by the statement is what a callee given it by reference reads, and changes; and what the interpreter reads, given
    // the statement that raises 5.
    [TestMethod]
    public async Task AMidStatementOfAStringAppendedTo_IsWhatACalleeAndTheInterpreterRead()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Passed()",
            "End Sub",
            "Private Function Passed() As String",
            "    Dim I As Long, T As String, E As Long",
            "    On Error Resume Next",
            "    For I = 1 To 3",
            "        T = T & \"abc\"",
            "        T = T & \"def\"",
            "        Mid$(T, I, 1) = \"z\"",
            "        Shown T",
            "    Next",
            "    T = T & \"ghi\"",
            "    Mid(T, 99) = \"x\"",
            "    E = Err.Number",
            "    Passed = T & E",
            "End Function",
            "Private Sub Shown(ByRef S As String)",
            "    Debug.Print S",
            "    Mid(S, Len(S), 1) = \"!\"",
            "End Sub"), [("Passed", 1), ("Shown", 0)]);

    // a parameter by reference is the caller's variable, which the statement changes.
    [TestMethod]
    public async Task AMidStatementOfAParameterByReference_ChangesTheCallersString()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim S As String",
            "    S = \"abcdef\"",
            "    Changed S",
            "    Debug.Print S",
            "End Sub",
            "Private Sub Changed(ByRef T As String)",
            "    Mid(T, 2, 2) = \"XY\"",
            "End Sub"), [("Changed", 0)]);

    // a String of the session in a BSTR (StrPtr) is written in place, as MS-VBA writes it: the interpreter's statement, and the String keeps its address.
    [TestMethod]
    public async Task AMidStatementOfAStringInABstr_IsTheInterpreters()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim S As String, P As LongPtr",
            "    S = \"abcdef\"",
            "    P = StrPtr(S)",
            "    Changed S",
            "    Debug.Print S, StrPtr(S) = P",
            "End Sub",
            "Private Sub Changed(ByRef T As String)",
            "    Mid(T, 2, 2) = \"XY\"",
            "End Sub"), [("Changed", 1)]);

    // the statements of a String variable change its characters where they are, as MS-VBA changes them in the BSTR: the String is copied once for each
    // activation (TextsCopied), not once for each statement, and is what the variable holds once it is read.
    [TestMethod]
    public async Task MidStatementsOfAStringVariable_CopyItOnce_NotOnceForEachStatement()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Stamped(1000)",
            "    Debug.Print Stamped(3)",
            "End Sub",
            "Private Function Stamped(ByVal N As Long) As String",
            "    Dim I As Long, S As String",
            "    S = \"RD-VBA\"",
            "    For I = 1 To N",
            "        Mid$(S, 1 + I Mod 6, 1) = CStr(I Mod 10)",
            "    Next",
            "    Stamped = S",
            "End Function"), [("Stamped", 0)]);

        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(2, compiled["Stamped"].Body!.TextsCopied, "one copy for each of the two activations");
        }
    }

    // a String that a Let gave two variables: a statement of either changes only its own, before and after the other is read, and a variable given
    // the other's String is changed apart from it too.
    [TestMethod]
    public async Task AMidStatementOfAStringTwoVariablesHold_ChangesOnlyItsOwn()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Kept(\"abcdef\")",
            "End Sub",
            "Private Function Kept(ByVal P As String) As String",
            "    Dim S As String, T As String, U As String, R As String",
            "    S = P",
            "    T = S",
            "    Mid$(S, 1, 1) = \"x\"",
            "    U = S",
            "    Mid$(S, 2, 1) = \"y\"",
            "    Mid$(T, 3, 1) = \"z\"",
            "    R = S & \"|\" & T & \"|\" & U & \"|\" & P",
            "    S = T",
            "    Mid$(S, 4, 1) = \"w\"",
            "    Kept = R & \"|\" & S & \"|\" & T",
            "End Function"), [("Kept", 0)]);

    // a variable changed by the statement, then given by reference twice: each parameter is the variable, and reads what the other changes; and a module
    // variable given by reference, which the callee reads by its own name (a Print of a module variable, which the interpreter runs). The caller's
    // variable is changed again after the call.
    [TestMethod]
    public async Task AMidStatementOfAParameterByReference_IsWhatEveryNameOfTheVariableReads()
        => await AssertAlikeAsync(Program(
            "Private G As String",
            "Public Sub Main()",
            "    Debug.Print Handed(\"abcdef\")",
            "    G = \"abcdef\"",
            "    Through G",
            "    Debug.Print G",
            "End Sub",
            "Private Function Handed(ByVal P As String) As String",
            "    Dim S As String",
            "    S = P",
            "    Mid$(S, 1, 1) = \"1\"",
            "    Twice S, S",
            "    Mid$(S, 2, 1) = \"2\"",
            "    Handed = S",
            "End Function",
            "Private Sub Twice(ByRef A As String, ByRef B As String)",
            "    Mid$(A, 3, 1) = \"3\"",
            "    Debug.Print B",
            "    Mid$(B, 4, 1) = \"4\"",
            "    Debug.Print A",
            "End Sub",
            "Private Sub Through(ByRef A As String)",
            "    Mid$(A, 1, 1) = \"Z\"",
            "    Debug.Print G",
            "End Sub"), [("Handed", 0), ("Twice", 0), ("Through", 1)]);

    // the statement before the first append, between appends and after them, the String read on each pass: what is appended goes after the characters
    // the statements changed.
    [TestMethod]
    public async Task AMidStatementWhileAStringIsAppendedTo_ChangesWhatItHolds()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Grown(4)",
            "End Sub",
            "Private Function Grown(ByVal N As Long) As String",
            "    Dim I As Long, S As String, T As String",
            "    S = \"ab\"",
            "    For I = 1 To N",
            "        Mid$(S, 1, 1) = CStr(I)",
            "        S = S & \"-\" & CStr(I)",
            "        Mid$(S, Len(S), 1) = \"#\"",
            "        T = T & S & \";\"",
            "    Next",
            "    Grown = S & \"|\" & T",
            "End Function"), [("Grown", 0)]);

    // MS-VBAL §5.4.3.5: starts below 1, past the end and past any String's length, and a negative length, raise 5 while the variable holds characters a
    // statement changed and nothing read yet; so does any start of an empty String. Each is the interpreter's, given the variable as the statements left
    // it; the last character can be changed.
    [TestMethod]
    public async Task AMidStatementOutOfAChangedString_RaisesWhereTheInterpreterRaises()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Bounded(\"abc\")",
            "End Sub",
            "Private Function Bounded(ByVal P As String) As String",
            "    Dim S As String, E As String, R As Long",
            "    S = P",
            "    On Error Resume Next",
            "    Mid$(S, 1, 1) = \"<\"",
            "    Mid$(S, 0) = \"x\"",
            "    R = Err.Number",
            "    Mid$(S, Len(S)) = \">\"",
            "    Mid$(S, Len(S) + 1) = \"x\"",
            "    R = R * 10 + Err.Number",
            "    Mid$(S, 2147483647) = \"x\"",
            "    R = R * 10 + Err.Number",
            "    Mid$(S, 2, -1) = \"x\"",
            "    R = R * 10 + Err.Number",
            "    Mid$(E, 1) = \"x\"",
            "    R = R * 10 + Err.Number",
            "    Bounded = S & \"|\" & E & \"|\" & R",
            "End Function"), [("Bounded", 5)]);

    // MidB, and a Mid statement of a Variant, are the interpreter's statements. The Let of the function's Variant result from V also goes to the
    // interpreter when it runs, so Varied counts two recoveries.
    [TestMethod]
    public async Task AMidBStatementOrAVariantTarget_IsTheInterpreters()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Bytes(\"abcdef\")",
            "    Debug.Print Varied(\"abcdef\")",
            "End Sub",
            "Private Function Bytes(ByVal P As String) As String",
            "    Dim S As String",
            "    S = P",
            "    MidB(S, 3, 2) = \"Z\"",
            "    Bytes = S",
            "End Function",
            "Private Function Varied(ByVal P As String) As Variant",
            "    Dim V As Variant",
            "    V = P",
            "    Mid(V, 1, 1) = \"Q\"",
            "    Varied = V",
            "End Function"), [("Bytes", 1), ("Varied", 2)]);
}
