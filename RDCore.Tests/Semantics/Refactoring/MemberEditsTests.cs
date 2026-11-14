using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The whole text of a member of a module, and the edits that remove, insert and move it: each keeps the member's comments, annotations and
/// <c>Attribute</c> lines, and one blank line between two members.
/// </summary>
[TestClass]
public sealed class MemberEditsTests
{
    // First has a comment of the declarations above it, a blank line between them; Second, an annotated comment block and an Attribute line; Third ends the
    // text.
    private const string Module =
        "Option Explicit\r\n" +
        "' the declarations\r\n" +
        "\r\n" +
        "Public Sub First()\r\n" +
        "    Debug.Print 1\r\n" +
        "End Sub ' after First\r\n" +
        "\r\n" +
        "'@Description(\"Second\")\r\n" +
        "' the second one\r\n" +
        "Public Sub Second()\r\n" +
        "Attribute Second.VB_Description = \"Second\"\r\n" +
        "    Debug.Print 2\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub Third()\r\n" +
        "    Debug.Print 3\r\n" +
        "End Sub\r\n";

    private static (MemberExtent First, MemberExtent Second, MemberExtent Third) Extents(string text)
    {
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), text);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        var members = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToArray();
        MemberExtent Of(string name) => MemberEdits.Extent(parse, text, members.Single(member => member.Name == name));
        return (Of("First"), Of("Second"), Of("Third"));
    }

    [TestMethod]
    public void AMembersExtent_HoldsTheCommentsDirectlyAboveIt_ItsAttributeLines_AndItsLastLine()
    {
        var (first, second, third) = Extents(Module);

        Assert.AreEqual(new SourceRange(3, 0, 6, 0), first.Range, "the comment of the declarations has a blank line between it and First");
        Assert.AreEqual(new SourceRange(7, 0, 13, 0), second.Range, "the comment after First's End Sub is First's");
        Assert.AreEqual(new SourceRange(14, 0, 17, 0), third.Range);
        Assert.IsFalse(second.IsConditional);
    }

    [TestMethod]
    public void TheExtentOfAMemberThatEndsATextWhichEndsNoLine_EndsAtTheEndOfTheText()
    {
        var (_, _, third) = Extents(Module.TrimEnd('\r', '\n'));

        Assert.AreEqual(new SourceRange(14, 0, 16, 7), third.Range);
    }

    [TestMethod]
    public void AMemberInAnIfBlock_IsConditional()
    {
        const string text = "Option Explicit\r\n#If VBA7 Then\r\nPublic Sub First()\r\nEnd Sub\r\n#End If\r\nPublic Sub Second()\r\nEnd Sub\r\nPublic Sub Third()\r\nEnd Sub\r\n";

        var (first, second, _) = Extents(text);

        Assert.IsTrue(first.IsConditional);
        Assert.IsFalse(second.IsConditional);
    }

    [TestMethod]
    public void AMemberRemoved_TakesTheBlankLinesAfterIt()
    {
        var (_, second, _) = Extents(Module);

        var removed = Applied(Module, [MemberEdits.Remove(Module, second)]);

        Assert.AreEqual(
            "Option Explicit\r\n' the declarations\r\n\r\nPublic Sub First()\r\n    Debug.Print 1\r\nEnd Sub ' after First\r\n\r\n" +
            "Public Sub Third()\r\n    Debug.Print 3\r\nEnd Sub\r\n",
            removed);
    }

    [TestMethod]
    [DataRow(true, DisplayName = "a text that ends its last line")]
    [DataRow(false, DisplayName = "a text that ends none")]
    public void TheLastMemberRemoved_TakesTheBlankLinesBeforeIt_AndTheTextEndsAsItDid(bool endsLine)
    {
        var text = endsLine ? Module : Module.TrimEnd('\r', '\n');
        var (_, _, third) = Extents(text);

        var removed = Applied(text, [MemberEdits.Remove(text, third)]);

        var expected = "Option Explicit\r\n' the declarations\r\n\r\nPublic Sub First()\r\n    Debug.Print 1\r\nEnd Sub ' after First\r\n\r\n" +
            "'@Description(\"Second\")\r\n' the second one\r\nPublic Sub Second()\r\nAttribute Second.VB_Description = \"Second\"\r\n    Debug.Print 2\r\nEnd Sub";
        Assert.AreEqual(endsLine ? expected + "\r\n" : expected, removed);
    }

    [TestMethod]
    public void AMemberMovedDown_GoesAfterTheNextOne_WithItsComments()
    {
        var (first, second, _) = Extents(Module);

        var moved = Applied(Module, MemberEdits.Move(Module, first, second, after: true));

        Assert.AreEqual(
            "Option Explicit\r\n' the declarations\r\n\r\n" +
            "'@Description(\"Second\")\r\n' the second one\r\nPublic Sub Second()\r\nAttribute Second.VB_Description = \"Second\"\r\n    Debug.Print 2\r\nEnd Sub\r\n\r\n" +
            "Public Sub First()\r\n    Debug.Print 1\r\nEnd Sub ' after First\r\n\r\n" +
            "Public Sub Third()\r\n    Debug.Print 3\r\nEnd Sub\r\n",
            moved);
    }

    [TestMethod]
    [DataRow(true, DisplayName = "a text that ends its last line")]
    [DataRow(false, DisplayName = "a text that ends none")]
    public void TheLastMemberMovedUp_GoesBeforeThePreviousOne_AndTheTextEndsAsItDid(bool endsLine)
    {
        var text = endsLine ? Module : Module.TrimEnd('\r', '\n');
        var (_, second, third) = Extents(text);

        var moved = Applied(text, MemberEdits.Move(text, third, second, after: false));

        var expected = "Option Explicit\r\n' the declarations\r\n\r\nPublic Sub First()\r\n    Debug.Print 1\r\nEnd Sub ' after First\r\n\r\n" +
            "Public Sub Third()\r\n    Debug.Print 3\r\nEnd Sub\r\n\r\n" +
            "'@Description(\"Second\")\r\n' the second one\r\nPublic Sub Second()\r\nAttribute Second.VB_Description = \"Second\"\r\n    Debug.Print 2\r\nEnd Sub";
        Assert.AreEqual(endsLine ? expected + "\r\n" : expected, moved);
    }

    [TestMethod]
    public void AMemberMovedAfterTheLastOne_OfATextThatEndsNoLine_EndsTheTextWithoutALineEnd()
    {
        var text = Module.TrimEnd('\r', '\n');
        var (first, _, third) = Extents(text);

        var moved = Applied(text, MemberEdits.Move(text, first, third, after: true));

        Assert.EndsWith("End Sub\r\n\r\nPublic Sub First()\r\n    Debug.Print 1\r\nEnd Sub ' after First", moved);
        Assert.StartsWith("Option Explicit\r\n' the declarations\r\n\r\n'@Description", moved);
    }

    [TestMethod]
    public void AMemberMovedToWhereItIs_IsRefused()
    {
        var (first, second, _) = Extents(Module);

        Assert.ThrowsExactly<ArgumentException>(() => MemberEdits.Move(Module, second, first, after: true));
        Assert.ThrowsExactly<ArgumentException>(() => MemberEdits.Move(Module, first, first, after: false));
    }

    [TestMethod]
    public void AMemberMoved_InAModuleThatEndsItsLinesWithLineFeeds_IsWrittenSo()
    {
        var text = Module.Replace("\r\n", "\n", StringComparison.Ordinal);
        var (first, second, _) = Extents(text);

        var moved = Applied(text, MemberEdits.Move(text, first, second, after: true));

        Assert.DoesNotContain("\r", moved);
        StringAssert.Contains(moved, "End Sub\n\nPublic Sub First()\n");
    }

    // the text with the edits made, each of the text as it was before any of them.
    private static string Applied(string text, IReadOnlyList<RefactorTextEdit> edits)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        int Offset(SourcePosition position) => starts[position.Line] + position.Character;
        foreach (var edit in edits.OrderByDescending(edit => Offset(edit.Range.Start)))
        {
            text = string.Concat(text.AsSpan(0, Offset(edit.Range.Start)), edit.NewText, text.AsSpan(Offset(edit.Range.End)));
        }

        return text;
    }
}
