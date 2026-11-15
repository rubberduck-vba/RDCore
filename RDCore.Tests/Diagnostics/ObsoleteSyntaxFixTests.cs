using RDCore.Diagnostics;
using static RDCore.Tests.Diagnostics.CodeActionFixture;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The fixes of the <c>RDC003xx</c> family: obsolete syntax written the way it is written today. The code means what it meant.
/// </summary>
[TestClass]
public sealed class ObsoleteSyntaxFixTests
{
    private static readonly string[] Bar = ["Private Sub Bar(ByVal a As Long, ByVal b As Long)", "End Sub"];

    [TestMethod]
    [DataRow("    Call Bar(1, 2)", "    Bar 1, 2", DisplayName = "arguments")]
    [DataRow("    Call Bar(1, _\r\n        2)", "    Bar 1, _\r\n        2", DisplayName = "arguments on two lines")]
    [DataRow("    Call Bar(b:=2, a:=1)", "    Bar b:=2, a:=1", DisplayName = "named arguments")]
    public async Task ACallWithArguments_IsWrittenWithoutCallOrParentheses(string call, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteCallStatement, [.. Bar, "Private Sub S()", call, "End Sub"], [.. Bar, "Private Sub S()", written, "End Sub"]);

    [TestMethod]
    [DataRow("    Call Foo", "    Foo", DisplayName = "no arguments")]
    [DataRow("    Call Foo()", "    Foo", DisplayName = "empty parentheses")]
    public async Task ACallWithNoArguments_IsWrittenWithoutCall(string call, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteCallStatement,
            ["Private Sub Foo()", "End Sub", "Private Sub S()", call, "End Sub"], ["Private Sub Foo()", "End Sub", "Private Sub S()", written, "End Sub"]);

    [TestMethod]
    public async Task ACallOfAMethod_IsWrittenWithoutCall()
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteCallStatement,
            ["Private Sub S()", "    Dim items As New Collection", "    Call items.Add(1)", "End Sub"],
            ["Private Sub S()", "    Dim items As New Collection", "    items.Add 1", "End Sub"]);

    [TestMethod]
    public async Task AnArgumentPassedByReference_IsStillPassedByReference()
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteCallStatement,
            ["Public Sub Main()", "    Dim x As Long", "    Call Bump(x)", "    Debug.Print x", "End Sub", "Private Sub Bump(ByRef n As Long)", "    n = n + 1", "End Sub"],
            ["Public Sub Main()", "    Dim x As Long", "    Bump x", "    Debug.Print x", "End Sub", "Private Sub Bump(ByRef n As Long)", "    n = n + 1", "End Sub"],
            runs: true);

    [TestMethod]
    public async Task ACallWhoseParenthesesTheTreeDoesNotPlace_IsNotRewritten()
        => await AssertNoFixAsync(RDCoreDiagnosticId.ObsoleteCallStatement, [.. Bar, "Private Sub S()", "    Call Bar( 1, 2 )", "End Sub"]);

    [TestMethod]
    [DataRow("    Rem a note", "    ' a note", DisplayName = "on a line of its own")]
    [DataRow("    Rem", "    '", DisplayName = "empty")]
    public async Task ARemComment_IsWrittenWithAQuote(string comment, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteCommentSyntax, ["Private Sub S()", comment, "End Sub"], ["Private Sub S()", written, "End Sub"]);

    [TestMethod]
    [DataRow("    Error 5", "    Err.Clear: Err.Raise 5", DisplayName = "an Integer literal")]
    [DataRow("    Error n", "    Err.Clear: Err.Raise CInt(n)", DisplayName = "a variable")]
    [DataRow("    Error n + 1", "    Err.Clear: Err.Raise CInt(n + 1)", DisplayName = "an expression")]
    public async Task AnErrorStatement_IsWrittenErrRaise(string statement, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteErrorSyntax,
            ["Private Sub S(ByVal n As Long)", statement, "End Sub"], ["Private Sub S(ByVal n As Long)", written, "End Sub"]);

    [TestMethod]
    public async Task AnErrorRaisedOverAnotherOne_IsTheSameError()
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteErrorSyntax,
            ["Public Sub Main()", "    On Error Resume Next", "    Err.Raise 13, \"Elsewhere\", \"Not this one\"", "    Error 5",
                "    Debug.Print Err.Number; Err.Description; Err.Source; Err.HelpContext", "End Sub"],
            ["Public Sub Main()", "    On Error Resume Next", "    Err.Raise 13, \"Elsewhere\", \"Not this one\"", "    Err.Clear: Err.Raise 5",
                "    Debug.Print Err.Number; Err.Description; Err.Source; Err.HelpContext", "End Sub"],
            runs: true);

    [TestMethod]
    public async Task AnErrorNumberThatIsNoInteger_StillOverflows()
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteErrorSyntax,
            ["Public Sub Main()", "    Dim n As Long", "    n = 40000", "    On Error Resume Next", "    Error n", "    Debug.Print Err.Number", "End Sub"],
            ["Public Sub Main()", "    Dim n As Long", "    n = 40000", "    On Error Resume Next", "    Err.Clear: Err.Raise CInt(n)", "    Debug.Print Err.Number", "End Sub"],
            runs: true);

    [TestMethod]
    [DataRow("Global g As Long", "Public g As Long", DisplayName = "a variable")]
    [DataRow("Global g As Long, h As Long", "Public g As Long, h As Long", DisplayName = "two variables")]
    [DataRow("Global Const C As Long = 1", "Public Const C As Long = 1", DisplayName = "a constant")]
    public async Task AGlobalDeclaration_IsWrittenPublic(string declaration, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteGlobalModifier, [declaration], [written]);

    [TestMethod]
    [DataRow("    Let x = 1", "    x = 1", DisplayName = "a variable")]
    [DataRow("    Let items(1) = 2", "    items(1) = 2", DisplayName = "an element")]
    public async Task ALetStatement_IsWrittenWithoutLet(string statement, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteLetStatement,
            ["Private Sub S()", "    Dim x As Long", "    Dim items(1 To 2) As Long", statement, "End Sub"],
            ["Private Sub S()", "    Dim x As Long", "    Dim items(1 To 2) As Long", written, "End Sub"]);

    [TestMethod]
    [DataRow("    Dim s$", "    Dim s As String", DisplayName = "a variable")]
    [DataRow("    Dim s$, d#", "    Dim s As String, d#", DisplayName = "the first of two")]
    [DataRow("    Dim a$(1 To 3)", "    Dim a(1 To 3) As String", DisplayName = "an array")]
    [DataRow("    Dim a&()", "    Dim a() As Long", DisplayName = "a dynamic array")]
    [DataRow("    Const T$ = \"x\"", "    Const T As String = \"x\"", DisplayName = "a constant")]
    public async Task ALocalWithATypeCharacter_IsDeclaredAsItsType(string declaration, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteTypeHint, ["Private Sub S()", declaration, "End Sub"], ["Private Sub S()", written, "End Sub"]);

    [TestMethod]
    [DataRow("Private Sub S(ByVal s$)", "Private Sub S(ByVal s As String)", DisplayName = "a parameter")]
    [DataRow("Private Sub S(Optional ByVal n& = 5)", "Private Sub S(Optional ByVal n As Long = 5)", DisplayName = "an optional parameter")]
    [DataRow("Private Sub S(ByRef a$())", "Private Sub S(ByRef a() As String)", DisplayName = "an array parameter")]
    public async Task AParameterWithATypeCharacter_IsDeclaredAsItsType(string header, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteTypeHint, [header, "End Sub"], [written, "End Sub"]);

    [TestMethod]
    public async Task AModuleVariableWithATypeCharacter_IsDeclaredAsItsType()
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteTypeHint, ["Private c@"], ["Private c As Currency"]);

    [TestMethod]
    public async Task ADeclarationWithATypeCharacter_DeclaredAsItsType_HoldsWhatItHeld()
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteTypeHint,
            ["Public Sub Main()", "    Dim s$", "    s$ = 1.5", "    Debug.Print TypeName(s); s", "End Sub"],
            ["Public Sub Main()", "    Dim s As String", "    s$ = 1.5", "    Debug.Print TypeName(s); s", "End Sub"],
            runs: true);

    [TestMethod]
    public async Task AReferenceWithATypeCharacter_IsWrittenWithoutIt()
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteTypeHint,
            ["Public Sub Main()", "    Dim s As String", "    s$ = \"x\"", "    Debug.Print s", "End Sub"],
            ["Public Sub Main()", "    Dim s As String", "    s = \"x\"", "    Debug.Print s", "End Sub"],
            runs: true);

    [TestMethod]
    public async Task AFunctionWithATypeCharacter_IsNotRewritten()
        => await AssertNoFixAsync(RDCoreDiagnosticId.ObsoleteTypeHint, "Private Function F$()", "End Function");

    [TestMethod]
    public async Task AWhileLoop_IsWrittenDoWhile()
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteWhileWend,
            ["Public Sub Main()", "    Dim x As Long", "    While x < 3", "        x = x + 1", "    Wend", "    Debug.Print x", "End Sub"],
            ["Public Sub Main()", "    Dim x As Long", "    Do While x < 3", "        x = x + 1", "    Loop", "    Debug.Print x", "End Sub"],
            runs: true);

    [TestMethod]
    public async Task AWhileLoop_WithAnExitDoOfAnInnerDo_IsWrittenDoWhile()
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteWhileWend,
            ["Public Sub Main()", "    Dim x As Long", "    While x < 3", "        Do", "            Exit Do", "        Loop", "        x = x + 1", "    Wend", "    Debug.Print x", "End Sub"],
            ["Public Sub Main()", "    Dim x As Long", "    Do While x < 3", "        Do", "            Exit Do", "        Loop", "        x = x + 1", "    Loop", "    Debug.Print x", "End Sub"],
            runs: true);

    [TestMethod]
    public async Task AWhileLoop_WithAnExitDoOfAnOuterDo_IsNotRewritten()
        => await AssertNoFixAsync(RDCoreDiagnosticId.ObsoleteWhileWend,
            "Private Sub S(ByVal y As Boolean)", "    Do", "        While True", "            If y Then Exit Do", "        Wend", "    Loop", "End Sub");

    [TestMethod]
    [DataRow("    On Local Error Resume Next", "    On Error Resume Next", DisplayName = "Resume Next")]
    [DataRow("    On Local Error GoTo 0", "    On Error GoTo 0", DisplayName = "GoTo 0")]
    public async Task AnOnLocalErrorStatement_IsWrittenOnError(string statement, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteOnLocalErrorStatement, ["Private Sub S()", statement, "End Sub"], ["Private Sub S()", written, "End Sub"]);

    [TestMethod]
    public async Task AnOnLocalErrorGoTo_IsWrittenOnErrorGoTo_AndStillHandles()
        => await AssertFixedAsync(RDCoreDiagnosticId.ObsoleteOnLocalErrorStatement,
            ["Public Sub Main()", "    On Local Error GoTo Handler", "    Debug.Print 1 / 0", "    Exit Sub", "Handler:", "    Debug.Print Err.Number", "End Sub"],
            ["Public Sub Main()", "    On Error GoTo Handler", "    Debug.Print 1 / 0", "    Exit Sub", "Handler:", "    Debug.Print Err.Number", "End Sub"],
            runs: true);

    [TestMethod]
    [DataRow("    Debug.Print Twice(ByVal 2)", "    Debug.Print Twice(2)", DisplayName = "a function's argument")]
    [DataRow("    Debug.Print Twice(n:=ByVal 2)", "    Debug.Print Twice(n:=2)", DisplayName = "named")]
    public async Task AByValArgumentOfAByValParameter_IsWrittenWithoutByVal(string statement, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.RedundantByValArgument,
            ["Private Function Twice(ByVal n As Long) As Long", "    Twice = n * 2", "End Function", "Public Sub Main()", statement, "End Sub"],
            ["Private Function Twice(ByVal n As Long) As Long", "    Twice = n * 2", "End Function", "Public Sub Main()", written, "End Sub"],
            runs: true);
}
