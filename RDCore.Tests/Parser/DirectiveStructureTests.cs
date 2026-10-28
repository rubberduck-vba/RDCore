using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Errors;

namespace RDCore.Tests.Parser;

/// <summary>
/// The conditional-compilation directives of a module that are wrong (<strong>MS-VBAL §3.4</strong>), each a syntax error MS-VBA reports in words of its own:
/// a directive that belongs to no block, a block that is not ended, and a <c>#Const</c> with no name, no <c>=</c> or no value. A directive that is not well
/// formed is no directive.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.4 Conditional Compilation")]
public sealed class DirectiveStructureTests
{
    private static ModuleParseResult Parse(params string[] lines)
        => new ModuleParser().Parse(TestUri.TestModuleUri(), string.Join("\r\n", lines) + "\r\nPublic Function Probe() As String\r\n    Probe = \"ok\"\r\nEnd Function\r\n");

    private static (VBCompileErrorId Id, int Line) Single(ModuleParseResult result)
    {
        var error = Assert.ContainsSingle(result.SyntaxErrors, string.Join("; ", result.SyntaxErrors.Select(error => $"{error.ErrorId} {error.Verbose}")));
        return ((VBCompileErrorId)error.ErrorId, error.Location.Range.Start.Line);
    }

    [TestMethod]
    [DataRow(new[] { "Private Const A = 1", "#End If" }, 1, DisplayName = "an #End If with no #If")]
    [DataRow(new[] { "Private Sub S()", "    Dim x As Long", "#End If", "    x = 1", "End Sub" }, 2, DisplayName = "an #End If with no #If, in a procedure")]
    [DataRow(new[] { "#If VBA7", "Private Const A = 1", "#End If" }, 2, DisplayName = "an #If with no Then is no directive")]
    [DataRow(new[] { "#If VBA7 Then Private Const A = 1", "#End If" }, 1, DisplayName = "an #If with code after its Then is no directive")]
    [DataRow(new[] { "#If X = Then", "Private Const A = 1", "#End If" }, 2, DisplayName = "an #If whose expression is no expression is no directive")]
    [DataRow(new[] { "#Iff VBA7 Then", "Private Const A = 1", "#End If" }, 2, DisplayName = "an unknown keyword is no directive")]
    public void AnEndIfThatEndsNoBlock_IsDirectiveWithoutIf(string[] lines, int line)
        => Assert.AreEqual((VBCompileErrorId.DirectiveWithoutIf, line), Single(Parse(lines)));

    [TestMethod]
    [DataRow(new[] { "Private Const A = 1", "#Else", "Private Const B = 1" }, 1, DisplayName = "an #Else with no #If")]
    [DataRow(new[] { "#If VBA7 Then", "Private Const A = 1", "#Else", "Private Const B = 1", "#Else", "Private Const C = 1", "#End If" }, 4, DisplayName = "a second #Else")]
    public void AnElseThatBelongsToNoBlock_IsHashElseWithoutIf(string[] lines, int line)
        => Assert.AreEqual((VBCompileErrorId.HashElseWithoutIf, line), Single(Parse(lines)));

    [TestMethod]
    [DataRow(new[] { "Private Const A = 1", "#ElseIf VBA7 Then", "Private Const B = 1" }, 1, DisplayName = "an #ElseIf with no #If")]
    [DataRow(new[] { "#If Mac Then", "Private Const A = 1", "#Else", "Private Const B = 1", "#ElseIf VBA7 Then", "Private Const C = 1", "#End If" }, 4, DisplayName = "an #ElseIf after the #Else")]
    public void AnElseIfThatBelongsToNoBlock_IsElseIfWithoutIf(string[] lines, int line)
        => Assert.AreEqual((VBCompileErrorId.ElseIfWithoutIf, line), Single(Parse(lines)));

    [TestMethod]
    [DataRow(new[] { "#If VBA7 Then", "Private Const A = 1" }, 0, DisplayName = "no #End If")]
    [DataRow(new[] { "#If Mac Then", "Private Const A = 1", "#End If junk" }, 0, DisplayName = "an #End If with text after it is no directive")]
    public void ABlockThatIsNotEnded_IsHashIfWithoutEndIf(string[] lines, int line)
        => Assert.AreEqual((VBCompileErrorId.HashIfWithoutEndIf, line), Single(Parse(lines)));

    [TestMethod]
    [DataRow("#Const X", "Expected: =", DisplayName = "no =")]
    [DataRow("#Const = 1", "Expected: identifier", DisplayName = "no name")]
    [DataRow("#Const X =", "Expected: expression", DisplayName = "no value")]
    public void AConstDirectiveWithNoNameOrNoEquals_IsASyntaxError(string line, string expected)
    {
        var result = Parse(line);
        var error = Assert.ContainsSingle(result.SyntaxErrors);

        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId);
        Assert.StartsWith(expected, error.Verbose);
        Assert.AreEqual(0, error.Location.Range.Start.Line);
    }

    [TestMethod]
    [DataRow(new[] { "#If VBA7 Then", "Private Const A = 1", "#EndIf" }, DisplayName = "#EndIf, as one word")]
    [DataRow(new[] { "#If Mac Then", "Private Const A = 1", "#ElseIf VBA7 Then", "Private Const B = 1", "#Else", "Private Const C = 1", "#End If" }, DisplayName = "an #If, an #ElseIf and an #Else")]
    [DataRow(new[] { "#Const Debugging = 1", "#If Debugging Then ' a comment", "#If VBA7 Then", "Private Const A = 1", "#End If", "#End If" }, DisplayName = "a nested block, and a comment after Then")]
    [DataRow(new[] { "#If VBA7 _", "    Then", "Private Const A = 1", "#End If" }, DisplayName = "an #If continued over lines")]
    public void WellFormedDirectives_AreNoError(string[] lines)
        => Assert.IsEmpty(Parse(lines).SyntaxErrors, string.Join("; ", Parse(lines).SyntaxErrors.Select(error => $"{error.ErrorId} {error.Verbose}")));
}
