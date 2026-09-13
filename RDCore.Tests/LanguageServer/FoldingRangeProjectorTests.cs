using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Folding;
using RDCore.Parsing;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class FoldingRangeProjectorTests
{
    private static List<FoldingRange> Project(params string[] lines)
    {
        var parseResult = new ModuleParser().Parse(TestUri.TestModuleUri(), string.Join("\n", lines));
        Assert.IsNotNull(parseResult.SyntaxTree, "the projector has nothing to say about a module that did not parse");
        return [.. FoldingRangeProjector.Project(parseResult.SyntaxTree)];
    }

    private static void AssertRange(FoldingRange range, int startLine, int endLine)
    {
        Assert.AreEqual(startLine, range.StartLine);
        Assert.AreEqual(endLine, range.EndLine);
        Assert.AreEqual(FoldingRangeKind.Region, range.Kind);
    }

    [TestMethod]
    public void Procedure_FoldsFromHeaderToEndStatement()
    {
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Public Sub Alpha()",
            "    Dim i As Long",
            "End Sub");

        AssertRange(ranges.Single(), 1, 3);
    }

    [TestMethod]
    public void Function_Folds()
    {
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Public Function Beta() As Long",
            "    Beta = 1",
            "End Function");

        AssertRange(ranges.Single(), 1, 3);
    }

    [TestMethod]
    public void EveryPropertyAccessorFoldsSeparately()
    {
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Public Property Get Value() As Long",
            "    Value = mValue",
            "End Property",
            "Public Property Let Value(ByVal v As Long)",
            "    mValue = v",
            "End Property");

        Assert.HasCount(2, ranges);
        AssertRange(ranges[0], 1, 3);
        AssertRange(ranges[1], 4, 6);
    }

    [TestMethod]
    public void TypeBlock_Folds()
    {
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Public Type TPoint",
            "    X As Long",
            "End Type");

        AssertRange(ranges.Single(), 1, 3);
    }

    [TestMethod]
    public void EnumBlock_Folds()
    {
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Public Enum Colour",
            "    Red = 1",
            "End Enum");

        AssertRange(ranges.Single(), 1, 3);
    }

    [TestMethod]
    public void SingleLineDeclare_DoesNotFold()
    {
        // A Declare is a member and occupies one line. Folding it would collapse nothing and put a
        // chevron in the gutter that does not do anything.
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Public Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As Long)");

        Assert.IsEmpty(ranges);
    }

    [TestMethod]
    public void ModuleFieldsAndDirectives_DoNotFold()
    {
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Option Explicit",
            "Private mValue As Long",
            "Public Const Limit As Long = 10");

        Assert.IsEmpty(ranges);
    }

    [TestMethod]
    public void AMultiLineFieldOrConstantStillDoesNotFold()
    {
        // These are excluded by the TYPE filter, not by the span test: a field is a
        // VariableDeclarationNode and a constant a ConstantDeclarationNode, neither of which is a
        // MemberDeclarationNode. Broken over a continuation they genuinely span two lines, so a test
        // using single-line ones cannot tell the two mechanisms apart.
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Private mValue _",
            "    As Long",
            "Public Const Limit _",
            "    As Long = 10");

        Assert.IsEmpty(ranges);
    }

    [TestMethod]
    public void ADeclareBrokenOverAContinuation_Folds()
    {
        // The other half of the span rule: the same member folds once it occupies more than one line.
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Public Declare PtrSafe Sub Sleep _",
            "    Lib \"kernel32\" (ByVal ms As Long)");

        Assert.HasCount(1, ranges);
        AssertRange(ranges.Single(), 1, 2);
    }

    [TestMethod]
    public void MembersInsideConditionalCompilationBranches_EachFold()
    {
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "#If VBA7 Then",
            "Public Sub Alpha()",
            "End Sub",
            "#Else",
            "Public Sub Beta()",
            "End Sub",
            "#End If");

        Assert.HasCount(2, ranges);
        AssertRange(ranges[0], 2, 3);
        AssertRange(ranges[1], 5, 6);
    }

    [TestMethod]
    public void TheModuleItselfDoesNotFold()
    {
        // ModuleNode is constructed with an empty source range, so there is nothing to project a module
        // fold from — and a fold spanning the whole document earns little.
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Public Sub Alpha()",
            "End Sub");

        Assert.HasCount(1, ranges);
        AssertRange(ranges.Single(), 1, 2);
    }

    [TestMethod]
    public void SeveralMembers_EachFoldSeparatelyAndInSourceOrder()
    {
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Public Type TPoint",
            "    X As Long",
            "End Type",
            "",
            "Private mValue As Long",
            "",
            "Public Sub Alpha()",
            "    Dim i As Long",
            "End Sub",
            "",
            "Public Function Beta() As Long",
            "End Function");

        Assert.HasCount(3, ranges);
        AssertRange(ranges[0], 1, 3);
        AssertRange(ranges[1], 7, 9);
        AssertRange(ranges[2], 11, 12);
    }

    [TestMethod]
    public void AModuleWithNoMembers_ProjectsNothing()
    {
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Option Explicit");

        Assert.IsEmpty(ranges);
    }

    [TestMethod]
    public void EveryProjectedRangeSpansMoreThanOneLine()
    {
        // The invariant the whole projection rests on, asserted over a module carrying every member kind
        // rather than inferred from the cases above: a client that is handed a range ending where it
        // starts draws a fold that does nothing, and one ending before it starts is undefined behaviour.
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Option Explicit",
            "Public Type TPoint",
            "    X As Long",
            "End Type",
            "Public Enum Colour",
            "    Red = 1",
            "End Enum",
            "Private mValue As Long",
            "Public Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As Long)",
            "Public Event Changed(ByVal v As Long)",
            "Public Sub Alpha()",
            "End Sub",
            "Public Function Beta() As Long",
            "End Function",
            "Public Property Get Value() As Long",
            "End Property");

        Assert.IsTrue(ranges.Count > 0, "the corpus is meant to produce folds");
        foreach (var range in ranges)
        {
            Assert.IsTrue(range.EndLine > range.StartLine,
                $"range {range.StartLine}..{range.EndLine} does not span more than one line");
        }
    }

    [TestMethod]
    public void AMemberWithNoEndStatement_StillProjectsSomethingUsable()
    {
        // Half-typed source is the ordinary state of a document in an editor, and the recovered member
        // still has a span. Whatever comes back must satisfy the same invariant as everything else.
        var ranges = Project(
            "Attribute VB_Name = \"Mod1\"",
            "Public Sub Alpha()",
            "    Dim i As Long");

        // A foreach over a possibly-empty collection asserts nothing; the parser recovers this shape
        // with a real span, so pin it.
        Assert.HasCount(1, ranges);
        AssertRange(ranges.Single(), 1, 2);
    }
}
