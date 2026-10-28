using RDCore.Parsing;
using RDCore.SDK.Formatting;

namespace RDCore.Tests.Formatting;

/// <summary>
/// A line is indented a level for each body that encloses it in the syntax tree; a line the tree has no statement of is indented by its keywords; a line number
/// stays at the start of its line, and a continued line lines up with the lines of its statement before it.
/// </summary>
[TestClass]
public sealed class ModuleIndenterTests
{
    private static string Indented(string text, CodeFormattingOptions? options = null, int first = 0, int last = int.MaxValue)
    {
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), text);
        return ModuleIndenter.Apply(text, ModuleIndenter.Indent(parse, text, options ?? CodeFormattingOptions.Default, first, last));
    }

    private static string Lines(params string[] lines) => string.Join("\r\n", lines);

    [TestMethod]
    public void NestedBlocks_AreALevelEach_TheirBranchesAtTheLevelOfTheBlock()
    {
        var text = Lines(
            "Public Sub Test()",
            "With Me",
            "If a Then",
            "For i = 1 To 2",
            "Debug.Print i",
            "Next",
            "ElseIf b Then",
            "Do While c",
            "Loop",
            "Else",
            "While d",
            "Wend",
            "End If",
            "End With",
            "End Sub");

        Assert.AreEqual(Lines(
            "Public Sub Test()",
            "    With Me",
            "        If a Then",
            "            For i = 1 To 2",
            "                Debug.Print i",
            "            Next",
            "        ElseIf b Then",
            "            Do While c",
            "            Loop",
            "        Else",
            "            While d",
            "            Wend",
            "        End If",
            "    End With",
            "End Sub"), Indented(text));
    }

    [TestMethod]
    public void TheLevelOfALine_IsOfSpaces_AsManyAsTheSettingSays()
        => Assert.AreEqual(Lines("Sub A()", "  If x Then", "    y = 1", "  End If", "End Sub"),
            Indented(Lines("Sub A()", "If x Then", "y = 1", "End If", "End Sub"), CodeFormattingOptions.Default with { IndentSpaces = 2 }));

    [TestMethod]
    public void ATab_IsReplacedBySpaces()
        => Assert.AreEqual(Lines("Sub A()", "    x = 1", "End Sub"), Indented(Lines("Sub A()", "\tx = 1", "End Sub")));

    [TestMethod]
    [DataRow(true, DisplayName = "indented")]
    [DataRow(false, DisplayName = "not indented")]
    public void WhatAnIfDirectiveGuards_IsIndented_AsTheSettingSays(bool indent)
    {
        var text = Lines("Sub A()", "#If DEBUG Then", "x = 1", "#Else", "x = 2", "#End If", "End Sub");
        var body = indent ? "        " : "    ";

        Assert.AreEqual(Lines("Sub A()", "    #If DEBUG Then", body + "x = 1", "    #Else", body + "x = 2", "    #End If", "End Sub"),
            Indented(text, CodeFormattingOptions.Default with { IndentCompilerDirectives = indent }));
    }

    // a branch that is not compiled has no statements in the tree: its keywords indent it.
    [TestMethod]
    public void ABranchThatIsNotCompiled_IsIndentedByItsKeywords()
    {
        var text = Lines("Sub A()", "#If False Then", "If x Then", "y = 1", "End If", "#End If", "End Sub");

        Assert.AreEqual(Lines("Sub A()", "    #If False Then", "        If x Then", "            y = 1", "        End If", "    #End If", "End Sub"), Indented(text));
    }

    [TestMethod]
    public void AContinuedLine_LinesUpWithTheArgumentsOfItsStatement()
        => Assert.AreEqual(Lines("Sub A()", "    x = Foo(1, _", "            2)", "End Sub"),
            Indented(Lines("Sub A()", "x = Foo(1, _", "      2)", "End Sub")));

    [TestMethod]
    public void AContinuedLine_NotAligned_IsWhereItsStatementStarts()
        => Assert.AreEqual(Lines("Sub A()", "    x = Foo(1, _", "    2)", "End Sub"),
            Indented(Lines("Sub A()", "x = Foo(1, _", "      2)", "End Sub"), CodeFormattingOptions.Default with { AlignContinuations = false }));

    [TestMethod]
    public void ARange_IsGivenEditsForItsLinesOnly_TheirLevelsOfTheWholeModule()
    {
        var text = Lines("Sub A()", "If x Then", "y = 1", "End If", "End Sub");

        Assert.AreEqual(Lines("Sub A()", "If x Then", "        y = 1", "End If", "End Sub"), Indented(text, first: 2, last: 2));
    }

    // only whitespace changes, and an edit is the run of it that changes: the indentation, and the gap before an end-of-line comment, apart.
    [TestMethod]
    public void AnEditOfALine_IsARunOfWhitespace_ThatIsNotWhatItIsToBe()
    {
        var text = Lines("Sub A()", "x = 1 ' one", "End Sub");
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), text);

        var edits = ModuleIndenter.Indent(parse, text, CodeFormattingOptions.Default with { EndOfLineCommentStyle = EndOfLineCommentStyle.StandardGap });

        CollectionAssert.AreEqual(new[] { new IndentationEdit(1, 0, 0, "    "), new IndentationEdit(1, 5, 1, "        ") }, edits.ToArray());
    }

    [TestMethod]
    public void AModuleIndentedAlready_IsGivenNoEdit()
    {
        var text = Lines("Option Explicit", "Public Enum Shade", "    Light", "End Enum", "Sub A()", "    Select Case x", "    Case 1", "        y = 1", "    End Select", "End Sub");
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), text);

        Assert.IsEmpty(ModuleIndenter.Indent(parse, text, CodeFormattingOptions.Default));
    }

    // as it is typed: every module a line at a time, broken as it is until it is whole, is indented, and never refused.
    [TestMethod]
    public void EveryModuleAsItIsTyped_IsIndented()
    {
        string[] lines =
        [
            "Option Explicit", "Private Type Point", "X As Long", "End Type", "Public Function Area(ByVal a As Long, _", "ByVal b As Long) As Long",
            "Dim p As Point", "If a > 0 Then", "Select Case b", "Case Is > 0", "10 Area = a * b", "Case Else", "Area = 0", "End Select", "Else",
            "Do Until a = 0", "a = a - 1", "Loop", "End If", "With p", ".X = 1", "End With", "#If DEBUG Then", "Debug.Print Area", "#End If", "End Function",
        ];

        for (var count = 1; count <= lines.Length; count++)
        {
            var text = Lines(lines[..count]);
            var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), text);
            var edits = ModuleIndenter.Indent(parse, text, CodeFormattingOptions.Default);

            Assert.IsTrue(edits.All(edit => edit.Line < count && edit.NewText.Trim().Length == 0 || char.IsDigit(edit.NewText.TrimStart()[0])),
                $"{count} lines: only whitespace, or a line number written again");
            _ = ModuleIndenter.Apply(text, edits);
        }
    }
}
