using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Variant</c> parameter passed by reference a variable of another type, a <c>String</c> or a number (<strong>MS-VBAL §5.3.1.11</strong>): the
/// parameter is a second name for the variable, and reads as a <c>Variant</c> holding its value. Compiled code reads it as the interpreter reads it, for
/// the arguments of the standard library and for what it assigns, with no instruction handed to the interpreter. A caller in compiled code gives the
/// parameter its variable as the interpreter gives it: what the callee assigns it is let-coerced to the variable's type.
/// </summary>
[TestClass]
public sealed class CompiledVariantParameterOfAnotherTypeTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // a key formatted by what it holds, as the cross-platform Dictionary formats one: given a String variable and a Long variable by reference.
    [TestMethod]
    public async Task AVariantGivenAStringOrALongByReference_IsReadInCompiledCode()
    {
        var result = await AssertAlikeAsync(Program(
            "Private Function Formatted(Key As Variant) As String",
            "    If IsObject(Key) Then",
            "        Formatted = \"o\"",
            "    ElseIf VarType(Key) = vbBoolean Then",
            "        Formatted = \"b\"",
            "    ElseIf VarType(Key) = vbString Then",
            "        Formatted = Key",
            "    Else",
            "        Formatted = CStr(Key) & \"__\" & CStr(Key)",
            "    End If",
            "End Function",
            "Public Sub Main()",
            "    Dim S As String, N As Long, I As Long, Text As String",
            "    For I = 1 To 3",
            "        S = \"k\" & I",
            "        N = I * 10",
            "        Text = Text & Formatted(S) & \",\" & Formatted(N) & \";\"",
            "    Next",
            "    Debug.Print Text",
            "End Sub"), "Formatted", recoveries: 0, expected: "k1,10__10;k2,20__20;k3,30__30;");
        Assert.IsEmpty(result.Stepped, string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }

    // the caller: a Long, a Byte and a String variable given by reference, at each turn of a loop. What the callee assigns the parameter is let-coerced to
    // the variable's type, and the parameter reads as a Variant of that type.
    [TestMethod]
    public async Task AScalarOrAStringVariable_GivenToAVariantByReference_IsAssignedByTheCallee()
    {
        var result = await AssertAlikeAsync(Program(
            "Private Function Bumped(Key As Variant) As Long",
            "    Key = Key + 1",
            "    Bumped = VarType(Key)",
            "End Function",
            "Private Function Tagged(Key As Variant) As Long",
            "    Key = Key & \"!\"",
            "    Tagged = Len(Key)",
            "End Function",
            "Private Function Counted() As String",
            "    Dim N As Long, B As Byte, S As String, I As Long, Total As Long",
            "    S = \"k\"",
            "    For I = 1 To 3",
            "        Total = Total + Bumped(N) + Bumped(B) + Tagged(S)",
            "    Next",
            "    Counted = N & \";\" & B & \";\" & S & \";\" & Total",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print Counted()",
            "End Sub"), "Counted", recoveries: 0, expected: "3;3;k!!!;69");
        Assert.IsEmpty(result.Stepped, string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }

    // a member of an object given a Long variable by reference: the parameter reads as a Long.
    [TestMethod]
    public async Task ALongVariable_GivenToAVariantOfAMember_ReadsAsALong()
    {
        var result = await AssertAlikeAsync(Program(
            "Private Function Measured() As Long",
            "    Dim K As Keys, I As Long, N As Long, W As Long",
            "    Set K = New Keys",
            "    For I = 1 To 3",
            "        N = I * 100",
            "        W = K.Width(N)",
            "        Measured = Measured + W",
            "    Next",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print Measured()",
            "End Sub"), "Measured", recoveries: 0, classes:
            [
                ("Keys", ModuleWorkspace.ClassModule("Keys",
                    "Option Explicit",
                    "Public Function Width(Key As Variant) As Long",
                    "    Width = VarType(Key) * 10 + Len(CStr(Key))",
                    "End Function")),
            ], expected: "99");
        Assert.IsEmpty(result.Stepped, string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }

    // MS-VBAL §5.3.1.11: what the callee assigns an Integer variable through the parameter is let-coerced to Integer, and a sum the Integer does not hold
    // raises error 6 (MS-VBAL §5.5.1.2.1; the sum of a Variant is a Long, §5.6.9.3), which the caller goes past: the variable keeps what it held.
    [TestMethod]
    public async Task AnIntegerVariable_AssignedTooLargeAValueThroughAVariant_RaisesOverflow()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Private Sub Grown(Key As Variant)",
            "    Key = Key + 20000",
            "End Sub",
            "Private Function Clipped() As Long",
            "    On Error Resume Next",
            "    Dim N As Integer, I As Long",
            "    For I = 1 To 3",
            "        Grown N",
            "        Clipped = Clipped + Err.Number",
            "        Err.Clear",
            "    Next",
            "    Clipped = Clipped * 100 + N",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print Clipped()",
            "End Sub"), expected: "21200");
        Assert.IsTrue(compiled.TryGetValue("Clipped", out var result), "Clipped was not called");
        Assert.IsNotNull(result.Body, string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }
}
