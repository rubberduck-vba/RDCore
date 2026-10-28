using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Errors;

namespace RDCore.Tests.Parser;

/// <summary>
/// The conditional-compilation directives of a module that are wrong (<strong>MS-VBAL §3.4</strong>), each a syntax error MS-VBA reports in words of its own:
/// a directive that belongs to no block, a block that is not ended, and a directive that is not well formed, at the token MS-VBA puts the caret on. A
/// directive that is not well formed is no directive for the structure of the blocks.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.4 Conditional Compilation")]
public sealed class DirectiveStructureTests
{
    private static ModuleParseResult Parse(params string[] lines)
        => new ModuleParser().Parse(TestUri.TestModuleUri(), string.Join("\r\n", lines) + "\r\nPublic Function Probe() As String\r\n    Probe = \"ok\"\r\nEnd Function\r\n");

    private static string Describe(ModuleParseResult result)
        => string.Join("; ", result.SyntaxErrors.Select(error => $"{error.ErrorId} {error.Location.Range.Start.Line}:{error.Location.Range.Start.Character} {error.Verbose}"));

    [TestMethod]
    [DataRow(new[] { "Private Const A = 1", "#End If" }, 1, DisplayName = "an #End If with no #If")]
    [DataRow(new[] { "Private Sub S()", "    Dim x As Long", "#End If", "    x = 1", "End Sub" }, 2, DisplayName = "an #End If with no #If, in a procedure")]
    [DataRow(new[] { "#If VBA7", "Private Const A = 1", "#End If" }, 2, DisplayName = "an #If with no Then is no directive")]
    [DataRow(new[] { "#If VBA7 Then Private Const A = 1", "#End If" }, 1, DisplayName = "an #If with code after its Then is no directive")]
    [DataRow(new[] { "#If X = Then", "Private Const A = 1", "#End If" }, 2, DisplayName = "an #If whose expression is no expression is no directive")]
    [DataRow(new[] { "#Iff VBA7 Then", "Private Const A = 1", "#End If" }, 2, DisplayName = "an unknown keyword is no directive")]
    public void AnEndIfThatEndsNoBlock_IsDirectiveWithoutIf(string[] lines, int line)
    {
        var result = Parse(lines);

        var structure = result.SyntaxErrors.Where(error => error.ErrorId != (int)VBCompileErrorId.SyntaxError).ToList();
        Assert.AreEqual((VBCompileErrorId.DirectiveWithoutIf, line, 0), ((VBCompileErrorId)Assert.ContainsSingle(structure, Describe(result)).ErrorId,
            structure[0].Location.Range.Start.Line, structure[0].Location.Range.Start.Character));
    }

    [TestMethod]
    [DataRow(new[] { "Private Const A = 1", "#Else", "Private Const B = 1" }, 1, DisplayName = "an #Else with no #If")]
    [DataRow(new[] { "#If VBA7 Then", "Private Const A = 1", "#Else", "Private Const B = 1", "#Else", "Private Const C = 1", "#End If" }, 4, DisplayName = "a second #Else")]
    public void AnElseThatBelongsToNoBlock_IsHashElseWithoutIf(string[] lines, int line)
        => Assert.AreEqual((VBCompileErrorId.HashElseWithoutIf, line), Only(Parse(lines)));

    [TestMethod]
    [DataRow(new[] { "Private Const A = 1", "#ElseIf VBA7 Then", "Private Const B = 1" }, 1, DisplayName = "an #ElseIf with no #If")]
    [DataRow(new[] { "#If Mac Then", "Private Const A = 1", "#Else", "Private Const B = 1", "#ElseIf VBA7 Then", "Private Const C = 1", "#End If" }, 4, DisplayName = "an #ElseIf after the #Else")]
    public void AnElseIfThatBelongsToNoBlock_IsElseIfWithoutIf(string[] lines, int line)
        => Assert.AreEqual((VBCompileErrorId.ElseIfWithoutIf, line), Only(Parse(lines)));

    [TestMethod]
    [DataRow(new[] { "#If VBA7 Then", "Private Const A = 1" }, DisplayName = "no #End If")]
    [DataRow(new[] { "#If Mac Then", "Private Const A = 1", "#End If junk" }, DisplayName = "an #End If with text after it is no directive")]
    [DataRow(new[] { "#If VBA7 Then", "Private Const A = 1", "#End If Rem c" }, DisplayName = "an #End If with Rem after it is no directive")]
    public void ABlockThatIsNotEnded_IsHashIfWithoutEndIf_AtItsIf(string[] lines)
    {
        // MS-VBA puts the caret on the module's last line; the #If is the one that is not ended.
        var result = Parse(lines);

        Assert.IsTrue(result.SyntaxErrors.Any(error => error.ErrorId == (int)VBCompileErrorId.HashIfWithoutEndIf && error.Location.Range.Start.Line == 0), Describe(result));
    }

    // a directive line that is not well formed, alone: the syntax error MS-VBA reports of it, where MS-VBA puts the caret.
    [TestMethod]
    [DataRow(new[] { "#If VBA7", "Private Const A = 1" }, 0, 8, 8, "Expected: Then", DisplayName = "an #If with no Then")]
    [DataRow(new[] { "#If VBA7 Foo Then", "Private Const A = 1" }, 0, 9, 12, "Expected: Then", DisplayName = "an #If with more than an expression before its Then")]
    [DataRow(new[] { "#If X = Then", "Private Const A = 1" }, 0, 8, 12, "Expected: expression", DisplayName = "an #If whose expression is not complete")]
    [DataRow(new[] { "#If VBA7 Then Private Const A = 1" }, 0, 14, 21, "Expected: end of statement", DisplayName = "an #If with code after its Then")]
    [DataRow(new[] { "#If VBA7 Then", "Private Const A = 1", "#End If junk", "#End If" }, 2, 8, 12, "Expected: end of statement", DisplayName = "an #End If with text after it")]
    [DataRow(new[] { "#If VBA7 Then", "Private Const A = 1", "#Else junk", "Private Const B = 1", "#End If" }, 2, 6, 10, "Expected: end of statement", DisplayName = "an #Else with text after it")]
    [DataRow(new[] { "#Iff X Then", "Private Const A = 1" }, 0, 1, 4, "Expected: If or Else or ElseIf or End or EndIf or Const", DisplayName = "an unknown keyword")]
    [DataRow(new[] { "#", "Private Const A = 1" }, 0, 1, 1, "Expected: If or Else or ElseIf or End or EndIf or Const", DisplayName = "no keyword")]
    [DataRow(new[] { "#Const X" }, 0, 8, 8, "Expected: =", DisplayName = "a #Const with no =")]
    [DataRow(new[] { "#Const X As Long = 1" }, 0, 9, 11, "Expected: =", DisplayName = "a #Const with a type")]
    [DataRow(new[] { "#Const = 1" }, 0, 7, 8, "Expected: identifier", DisplayName = "a #Const with no name")]
    [DataRow(new[] { "#Const X =" }, 0, 10, 10, "Expected: expression", DisplayName = "a #Const with no value")]
    [DataRow(new[] { "#Const Été =" }, 0, 12, 12, "Expected: expression", DisplayName = "a #Const with an accented name and no value")]
    [DataRow(new[] { "#Const X = 1 junk" }, 0, 13, 17, "Expected: end of statement", DisplayName = "a #Const with text after its value")]
    public void ADirectiveLineThatIsNotWellFormed_IsTheSyntaxErrorMsVbaReports(string[] lines, int line, int start, int end, string expected)
    {
        var result = Parse(lines);

        var error = Assert.ContainsSingle(result.SyntaxErrors.Where(error => error.ErrorId == (int)VBCompileErrorId.SyntaxError), Describe(result));
        Assert.StartsWith(expected, error.Verbose, Describe(result));
        Assert.AreEqual((line, start, line, end), (error.Location.Range.Start.Line, error.Location.Range.Start.Character,
            error.Location.Range.End.Line, error.Location.Range.End.Character));
    }

    [TestMethod]
    [DataRow(new[] { "#If VBA7 Then", "Private Const A = 1", "#EndIf" }, DisplayName = "#EndIf, as one word")]
    [DataRow(new[] { "#If Mac Then", "Private Const A = 1", "#ElseIf VBA7 Then", "Private Const B = 1", "#Else", "Private Const C = 1", "#End If" }, DisplayName = "an #If, an #ElseIf and an #Else")]
    [DataRow(new[] { "#Const Debugging = 1", "#If Debugging Then ' a comment", "#If VBA7 Then", "Private Const A = 1", "#End If", "#End If" }, DisplayName = "a nested block, and a comment after Then")]
    [DataRow(new[] { "#If VBA7 _", "    Then", "Private Const A = 1", "#End If" }, DisplayName = "an #If continued over lines")]
    [DataRow(new[] { "#If(VBA7) Then", "Private Const A = 1", "#End If" }, DisplayName = "an #If whose condition is in parentheses, written against it")]
    [DataRow(new[] { "#If (Win64)Then", "Private Const A = 1", "#End If" }, DisplayName = "a Then written against the parenthesis")]
    [DataRow(new[] { "#If Mac Then", "Private Const A = 1", "#ElseIf(VBA7) Then", "Private Const B = 1", "#End If" }, DisplayName = "an #ElseIf whose condition is in parentheses, written against it")]
    [DataRow(new[] { "#Const Été = 1", "#If Été Then", "Private Const A = 1", "#End If" }, DisplayName = "an accented constant")]
    public void WellFormedDirectives_AreNoError(string[] lines)
    {
        var result = Parse(lines);

        Assert.IsEmpty(result.SyntaxErrors, Describe(result));
    }

    [TestMethod]
    public void WhatIsWrongWithAConst_StandsBesideTheErrorsOfTheBlocks()
    {
        // a module with no #If, whose grammar errors the block scanner restates: the #Const's error is not one of them, and stays.
        var result = Parse("#Const X", "Private Sub S()", "    For i = 1 To 2", "End Sub");

        Assert.IsTrue(result.SyntaxErrors.Any(error => error.Verbose?.StartsWith("Expected: =") == true), Describe(result));
        Assert.IsTrue(result.SyntaxErrors.Any(error => error.ErrorId == (int)VBCompileErrorId.ForWithoutNext), Describe(result));
    }

    private static (VBCompileErrorId Id, int Line) Only(ModuleParseResult result)
    {
        var error = Assert.ContainsSingle(result.SyntaxErrors, Describe(result));
        return ((VBCompileErrorId)error.ErrorId, error.Location.Range.Start.Line);
    }
}
