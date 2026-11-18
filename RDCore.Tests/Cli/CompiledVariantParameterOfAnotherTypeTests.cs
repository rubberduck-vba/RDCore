using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Variant</c> parameter passed by reference a variable of another type, a <c>String</c> or a number (<strong>MS-VBAL §5.3.1.11</strong>): the
/// parameter is a second name for the variable, and reads as a <c>Variant</c> holding its value. Compiled code reads it as the interpreter reads it, for
/// the arguments of the standard library and for what it assigns, with no instruction handed to the interpreter.
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

    // a Variant let to a String (MS-VBAL §5.5.1.2.4): one that holds a String is read in compiled code; a Double and a Boolean, which the let-coercion
    // writes in the regional settings of the session, are the interpreter's, once each.
    [TestMethod]
    public async Task AVariantLetToAString_IsReadInCompiledCode_WhenItHoldsAString()
        => await AssertAlikeAsync(Program(
            "Private Function Named(ByVal V As Variant) As String",
            "    Dim S As String",
            "    S = V",
            "    Named = S & \"!\"",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print Named(\"a\") & Named(2.5) & Named(True) & Named(\"b\")",
            "End Sub"), "Named", recoveries: 2, expected: "a!2.5!True!b!");
}
