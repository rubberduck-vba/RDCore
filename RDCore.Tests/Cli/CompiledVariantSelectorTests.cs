using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Select Case</c> whose selector is a call of a function declared to return a <c>Variant</c>, compiled: the selector is a Variant, holding a String
/// here, and each <c>Case</c> compares it with its value as a Variant (MS-VBAL §5.4.2.10, §5.6.9.5), "10" being greater than the number 9, as MS-VBA
/// compares it. The interpreter steps the Select and its Cases. The function's String form, with a $, is a String selector, which compiled code compares
/// as text, the value 9 let-coerced to "9" (RD-VBAL §5.4.2.10): "10" sorts before it. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledVariantSelectorTests
{
    private static string Selecting(string selector) => Program(
        "Public Sub Main()",
        "    Debug.Print Picked()",
        "End Sub",
        "Private Function Picked() As Long",
        "    Select Case " + selector,
        "        Case Is > 9",
        "            Picked = 1",
        "        Case Else",
        "            Picked = 2",
        "    End Select",
        "End Function",
        "Private Function Ten() As Variant",
        "    Ten = \"10\"",
        "End Function");

    private static string Reasons(CompilationResult result) => string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"));

    [TestMethod]
    [DataRow("Mid(\"10\", 1)", DisplayName = "Mid")]
    [DataRow("Left(\"10\", 2)", DisplayName = "Left")]
    [DataRow("Right(\"10\", 2)", DisplayName = "Right")]
    [DataRow("Trim(\" 10 \")", DisplayName = "Trim")]
    [DataRow("LTrim(\" 10\")", DisplayName = "LTrim")]
    [DataRow("RTrim(\"10 \")", DisplayName = "RTrim")]
    [DataRow("LCase(\"10\")", DisplayName = "LCase")]
    [DataRow("UCase(\"10\")", DisplayName = "UCase")]
    [DataRow("Format(10)", DisplayName = "Format")]
    [DataRow("CVar(\"10\")", DisplayName = "CVar")]
    [DataRow("Ten()", DisplayName = "a function of the workspace")]
    public async Task ACallReturningAVariant_IsAVariantSelector(string selector)
    {
        var compiled = await AssertAlikeAsync(Selecting(selector), expected: " 1 ");
        Assert.IsTrue(compiled.TryGetValue("Picked", out var result), "Picked was not called");
        Assert.IsTrue(result.Stepped.Any(reason => reason is { Kind: IneligibilityKind.VariantOperand, Detail: "Select Case" }), Reasons(result));
    }

    [TestMethod]
    [DataRow("Mid$(\"10\", 1)", DisplayName = "Mid$")]
    [DataRow("Left$(\"10\", 2)", DisplayName = "Left$")]
    [DataRow("Right$(\"10\", 2)", DisplayName = "Right$")]
    [DataRow("Trim$(\" 10 \")", DisplayName = "Trim$")]
    [DataRow("LTrim$(\" 10\")", DisplayName = "LTrim$")]
    [DataRow("RTrim$(\"10 \")", DisplayName = "RTrim$")]
    [DataRow("LCase$(\"10\")", DisplayName = "LCase$")]
    [DataRow("UCase$(\"10\")", DisplayName = "UCase$")]
    [DataRow("Format$(10)", DisplayName = "Format$")]
    public async Task ACallReturningAString_IsCompiledAsText(string selector)
    {
        var compiled = await AssertAlikeAsync(Selecting(selector), expected: " 2 ");
        Assert.IsTrue(compiled.TryGetValue("Picked", out var result), "Picked was not called");
        Assert.IsNotNull(result.Body, string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
        Assert.IsEmpty(result.Stepped, Reasons(result));
    }
}
