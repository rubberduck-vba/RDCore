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
}
