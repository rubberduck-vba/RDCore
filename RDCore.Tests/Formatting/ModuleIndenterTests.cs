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
        var text = Lines("Option Explicit", "", "Public Enum Shade", "    Light", "End Enum", "", "Sub A()", "    Select Case x", "    Case 1", "        y = 1", "    End Select", "End Sub");
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), text);

        Assert.IsEmpty(ModuleIndenter.Indent(parse, text, CodeFormattingOptions.Default));
    }

    // as it is typed: every module a line at a time, unfinished until it is whole, has each of its lines where the whole module has it.
    [TestMethod]
    public void EveryModuleAsItIsTyped_HasEachLineWhereTheWholeModuleHasIt()
    {
        string[] indented =
        [
            "Option Explicit",
            "",
            "Private Type Point",
            "    X As Long",
            "End Type",
            "",
            "Public Function Area(ByVal a As Long, _",
            "                     ByVal b As Long) As Long",
            "    Dim p As Point",
            "    If a > 0 Then",
            "        Select Case b",
            "        Case Is > 0",
            "10          Area = a * b",
            "        Case Else",
            "            Area = 0",
            "        End Select",
            "    Else",
            "        Do Until a = 0",
            "            a = a - 1",
            "        Loop",
            "    End If",
            "    With p",
            "        .X = 1",
            "    End With",
            "    #If DEBUG Then",
            "        Debug.Print Area",
            "    #End If",
            "End Function",
        ];
        var typed = indented.Select(line => line.StartsWith("10", StringComparison.Ordinal) ? "10 " + line[2..].TrimStart() : line.TrimStart()).ToArray();

        for (var count = 1; count <= typed.Length; count++)
        {
            Assert.AreEqual(Lines(indented[..count]), Indented(Lines(typed[..count])), $"{count} lines typed");
        }
    }

    [TestMethod]
    [DataRow(new[] { "Sub A()", "If x Then", "For i = 1 To 2", "y = 1" }, new[] { "Sub A()", "    If x Then", "        For i = 1 To 2", "            y = 1" },
        DisplayName = "blocks not closed yet")]
    [DataRow(new[] { "Sub A(", "If x Then", "y = = 1", "End If", "End Sub" }, new[] { "Sub A(", "    If x Then", "        y = = 1", "    End If", "End Sub" },
        DisplayName = "a body the parser built nothing of")]
    [DataRow(new[] { "Sub A(", "If x Then", "y = = 1", "Else ' c", "y = = 2", "End If", "End Sub" },
        new[] { "Sub A(", "    If x Then", "        y = = 1", "    Else                                         ' c", "        y = = 2", "    End If", "End Sub" }, DisplayName = "a branch with a comment")]
    public void UnfinishedCode_IsIndentedByWhatEnclosesIt(string[] typed, string[] indented)
        => Assert.AreEqual(Lines(indented), Indented(Lines(typed)));

    // as Rubberduck's indenter has it: the line that closes a block is at the block's level, wherever the closer is on it.
    [TestMethod]
    public void ALineWithACloserAfterItsCode_IsAtTheLevelOfTheBlock()
        => Assert.AreEqual(Lines("Sub A()", "    If a Then", "    x = 1: End If", "    For i = 1 To 2", "    Debug.Print i: Next", "End Sub"),
            Indented(Lines("Sub A()", "If a Then", "x = 1: End If", "For i = 1 To 2", "Debug.Print i: Next", "End Sub")));

    // the empty lines between procedures are one edit, from the end of the line before them, with that line's line end; what is in a procedure is not spaced.
    [TestMethod]
    public void TheEmptyLinesBetweenProcedures_AreOneEditFromTheEndOfTheLineBeforeThem()
    {
        var text = "Sub A()\n\n\nEnd Sub\n  \n\n\nSub B()\r\nEnd Sub\r\nSub C()\r\nEnd Sub";
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), text);

        var edits = ModuleIndenter.Indent(parse, text, CodeFormattingOptions.Default);

        CollectionAssert.AreEqual(new[] { new IndentationEdit(3, 7, 7, 0, "\n\n"), new IndentationEdit(8, 7, 9, 0, "\r\n\r\n") }, edits.ToArray());
        Assert.AreEqual("Sub A()\n\n\nEnd Sub\n\nSub B()\r\nEnd Sub\r\n\r\nSub C()\r\nEnd Sub", ModuleIndenter.Apply(text, edits));
    }

    [TestMethod]
    public void TheHeaderOfAClassModule_AndItsAttributes_AreLeftAsTheyAreWritten()
    {
        var text = Lines("VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"Class1\"", "Option Explicit",
            "Public Property Get Item() As Long", "Attribute Item.VB_UserMemId = 0", "Item = 1", "End Property");

        Assert.AreEqual(Lines("VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"Class1\"", "Option Explicit", "",
            "Public Property Get Item() As Long", "Attribute Item.VB_UserMemId = 0", "    Item = 1", "End Property"), Indented(text));
    }

    [TestMethod]
    public void ARangeThatSplitsAStatement_IsWidenedToTheWholeStatement()
        => Assert.AreEqual(Lines("Sub A()", "    x = Foo(1, _", "            2)", "End Sub"),
            Indented(Lines("Sub A()", "x = Foo(1, _", "      2)", "End Sub"), first: 2, last: 2));

    // as the VBE has it: a label is in column 1, and the code after it where the level has it.
    [TestMethod]
    public void ALabel_IsInColumn1()
        => Assert.AreEqual(Lines("Sub A()", "    On Error GoTo ErrH", "    Exit Sub", "ErrH:", "Done: Beep", "End Sub"),
            Indented(Lines("Sub A()", "On Error GoTo ErrH", "Exit Sub", "    ErrH:", "  Done:   Beep", "End Sub")));

    [TestMethod]
    public void ALineNumberWithNoCode_IsWrittenWithNoWhitespaceAfterIt()
        => Assert.AreEqual(Lines("Sub A()", "10  x = 1", "20", "End Sub"), Indented(Lines("Sub A()", "10 x = 1", "20  ", "End Sub")));

    [TestMethod]
    public void ATabInAContinuedLine_IsReplacedBySpaces()
        => Assert.AreEqual(Lines("Sub A()", "    x = Foo(1, _", "            2)", "End Sub"), Indented(Lines("Sub A()", "x = Foo(1, _", "\t2)", "End Sub")));

    [TestMethod]
    public void AProcedureWithAHeaderInEachBranchOfAnIf_HasThemAtItsLevel_AndItsBodyIndentedOnce()
    {
        var text = Lines("#If VBA7 Then", "  Public Sub A(ByVal x As LongPtr)", "  #Else", "    Public Sub A(ByVal x As Long)", " #End If", "y = 1", "End Sub");

        Assert.AreEqual(Lines("#If VBA7 Then", "Public Sub A(ByVal x As LongPtr)", "#Else", "Public Sub A(ByVal x As Long)", "#End If", "    y = 1", "End Sub"),
            Indented(text));
    }

    // a line ends as the Language Server Protocol counts it: a carriage return alone ends one too, and each line keeps what ends it.
    [TestMethod]
    public void ALineEnd_IsACarriageReturnALineFeedOrBoth()
    {
        var text = "Sub A()\rIf x Then\ny = 1\r\nEnd If\rEnd Sub";
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), text);

        var edits = ModuleIndenter.Indent(parse, text, CodeFormattingOptions.Default);

        CollectionAssert.AreEqual(new[] { new IndentationEdit(1, 0, 0, "    "), new IndentationEdit(2, 0, 0, "        "), new IndentationEdit(3, 0, 0, "    ") }, edits.ToArray());
        Assert.AreEqual("Sub A()\r    If x Then\n        y = 1\r\n    End If\rEnd Sub", ModuleIndenter.Apply(text, edits));
    }
}
