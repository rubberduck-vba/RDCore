using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledIntrinsicTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The <c>Mid</c> statement of a <c>String</c> variable, in characters, run by compiled code (MS-VBAL §5.4.3.5): a program runs the same as the
/// interpreter runs it, and the statements compiled code leaves to the interpreter are the ones said.
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
