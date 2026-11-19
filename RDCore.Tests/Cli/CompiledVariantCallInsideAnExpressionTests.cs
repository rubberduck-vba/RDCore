using static RDCore.Tests.Cli.CompiledErrorHandlingTests;
using static RDCore.Tests.Cli.CompiledVariantParameterOfAnotherTypeTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A function or a <c>Property Get</c> of the workspace that returns a <c>Variant</c>, called inside an expression (<strong>MS-VBAL §5.6.13.1</strong>):
/// compiled code makes the call once and reads what the <c>Variant</c> holds of a scalar type as the interpreter reads it, a condition as its truth
/// (<strong>MS-VBAL §5.4.2.8</strong>). What it holds of no scalar type is given to the interpreter, which goes on with the statement and does not make the
/// call again.
/// </summary>
[TestClass]
public sealed class CompiledVariantCallInsideAnExpressionTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static (string Name, string Source) Flags() => ("Flags", ModuleWorkspace.ClassModule("Flags",
        "Option Explicit",
        "Private m As Collection",
        "Private Sub Class_Initialize()",
        "    Set m = New Collection",
        "End Sub",
        "Public Sub Add(ByVal Value As Variant, ByVal Key As String)",
        "    m.Add Value, Key",
        "End Sub",
        "Public Property Get Item(Key As Variant) As Variant",
        "    Item = m(Key)",
        "End Property"));

    // a Property Get with an argument, the condition of an If, given a Variant and a String by reference.
    [TestMethod]
    public async Task AVariantMemberInACondition_IsCalledByCompiledCode()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim F As Flags",
            "    Set F = New Flags",
            "    F.Add True, \"a\"",
            "    F.Add False, \"b\"",
            "    Debug.Print Counted(F)",
            "End Sub",
            "Private Function Counted(ByVal F As Flags) As String",
            "    Dim I As Long, K As Variant, J As String, T As String",
            "    For I = 1 To 3",
            "        K = \"a\"",
            "        J = \"b\"",
            "        If F.Item(K) Then T = T & \"i\"",
            "        If F.Item(J) Then T = T & \"x\" Else T = T & \"d\"",
            "    Next",
            "    Counted = T",
            "End Function"), classes: [Flags()], expected: "ididid");
        AssertNoneStepped(compiled, "Counted");
    }

    // a function of a standard module that returns a Variant, an operand of an operation of Longs.
    [TestMethod]
    public async Task AVariantFunctionInAnOperation_IsCalledByCompiledCode()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Private Function Twice(ByVal N As Long) As Variant",
            "    Twice = N * 2",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print Summed()",
            "End Sub",
            "Private Function Summed() As Long",
            "    Dim I As Long, T As Long",
            "    For I = 1 To 4",
            "        T = T + Twice(I)",
            "    Next",
            "    Summed = T",
            "End Function"), expected: "20");
        AssertNoneStepped(compiled, "Summed");
    }

    // what the Variant holds is a String: the interpreter is given it, and the call is made once.
    [TestMethod]
    public async Task AVariantThatHoldsAString_IsGivenToTheInterpreter_AndTheCallIsMadeOnce()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Private Function Named(ByVal N As Long) As Variant",
            "    Debug.Print \"c\";",
            "    Named = CStr(N)",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print \"=\" & Summed()",
            "End Sub",
            "Private Function Summed() As Long",
            "    Dim I As Long, T As Long",
            "    For I = 1 To 3",
            "        T = T + Named(I)",
            "    Next",
            "    Summed = T",
            "End Function"), expected: "ccc=6");
        Assert.IsTrue(compiled.TryGetValue("Summed", out var result), "Summed was not called");
        Assert.IsNotNull(result.Body, string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }
}
