using RDCore.Parsing;
using RDCore.SDK.Formatting;

namespace RDCore.Tests.Formatting;

/// <summary>
/// The blocks a line of a program is in that the program has no closer for, as the shell offers to close them while a program is typed one numbered line at
/// a time.
/// </summary>
[TestClass]
public sealed class ProgramBlocksTests
{
    private static readonly Uri Program = new("untitled:Program.rdc");

    private static ProgramBlock[] OpenAt(int index, params string[] statements)
        => [.. ProgramBlocks.OpenAt(statements, index, text => new ModuleParser().Parse(Program, text))];

    [TestMethod]
    public void AnIfAlone_IsOpen_WithNoBody()
    {
        var open = OpenAt(0, "If x > 0 Then");

        CollectionAssert.AreEqual(new[] { new ProgramBlock(0, 0, "End If", false) }, open);
    }

    [TestMethod]
    public void AnIfWithABody_IsOpen_WithItsBody()
    {
        var open = OpenAt(1, "If x > 0 Then", "Debug.Print 1");

        CollectionAssert.AreEqual(new[] { new ProgramBlock(0, 0, "End If", true) }, open);
    }

    [TestMethod]
    public void AnIfWithItsEndIf_IsClosed()
    {
        Assert.IsEmpty(OpenAt(2, "If x > 0 Then", "Debug.Print 1", "End If"));
    }

    [TestMethod]
    public void ALineOfAClosedBlock_IsInNoOpenBlock()
    {
        Assert.IsEmpty(OpenAt(1, "If x > 0 Then", "Debug.Print 1", "End If"));
        Assert.IsEmpty(OpenAt(0, "If x > 0 Then", "End If"));
    }

    [TestMethod]
    public void AOneLineIf_IsNoBlock()
    {
        Assert.IsEmpty(OpenAt(0, "If x Then Debug.Print 1"));
    }

    [TestMethod]
    public void ALoopInAnIf_IsOpen_InsideTheIf()
    {
        var open = OpenAt(2, "If x Then", "For i = 1 To 3", "Debug.Print i");

        CollectionAssert.AreEqual(new[] { new ProgramBlock(1, 1, "Next", true), new ProgramBlock(0, 0, "End If", true) }, open);
    }

    [TestMethod]
    public void TheNextOfTheInnerLoop_LeavesTheIfOpen()
    {
        var open = OpenAt(3, "If x Then", "For i = 1 To 3", "Debug.Print i", "Next");

        CollectionAssert.AreEqual(new[] { new ProgramBlock(0, 0, "End If", true) }, open);
    }

    [TestMethod]
    public void TheEndIfOfAnInnerIf_DoesNotCloseTheOuterIf()
    {
        var open = OpenAt(3, "If x Then", "If y Then", "Debug.Print 1", "End If");

        CollectionAssert.AreEqual(new[] { new ProgramBlock(0, 0, "End If", true) }, open);
    }

    [TestMethod]
    public void ANextThatListsTwoNames_ClosesTwoLoops()
    {
        Assert.IsEmpty(OpenAt(3, "For i = 1 To 3", "For j = 1 To 3", "Debug.Print i * j", "Next j, i"));
    }

    [TestMethod]
    public void ACloserAfterAColon_ClosesItsBlock()
    {
        Assert.IsEmpty(OpenAt(1, "If x Then", "y = 1: End If"));
    }

    [TestMethod]
    public void AnElse_IsTheBranchOfItsIf_WithNoBodyUntilAStatementFollowsIt()
    {
        CollectionAssert.AreEqual(new[] { new ProgramBlock(0, 2, "End If", false) }, OpenAt(2, "If x Then", "Debug.Print 1", "Else"));
        CollectionAssert.AreEqual(new[] { new ProgramBlock(0, 2, "End If", true) }, OpenAt(3, "If x Then", "Debug.Print 1", "Else", "Debug.Print 2"));
    }

    [TestMethod]
    public void AnElseIf_IsTheBranchOfItsIf()
    {
        CollectionAssert.AreEqual(new[] { new ProgramBlock(0, 2, "End If", false) }, OpenAt(2, "If x Then", "Debug.Print 1", "ElseIf y Then"));
    }

    [TestMethod]
    public void ACase_IsTheBranchOfItsSelect()
    {
        CollectionAssert.AreEqual(new[] { new ProgramBlock(0, 0, "End Select", false) }, OpenAt(0, "Select Case x"));
        CollectionAssert.AreEqual(new[] { new ProgramBlock(0, 1, "End Select", false) }, OpenAt(1, "Select Case x", "Case 1"));
        CollectionAssert.AreEqual(new[] { new ProgramBlock(0, 1, "End Select", true) }, OpenAt(2, "Select Case x", "Case 1", "Debug.Print 1"));
    }

    [TestMethod]
    [DataRow("Do", "Loop")]
    [DataRow("Do While x", "Loop")]
    [DataRow("For Each v In c", "Next")]
    [DataRow("While x", "Wend")]
    [DataRow("With y", "End With")]
    public void EachBlock_IsOpen_WithItsCloser(string opener, string closer)
    {
        CollectionAssert.AreEqual(new[] { new ProgramBlock(0, 0, closer, false) }, OpenAt(0, opener));
        CollectionAssert.AreEqual(new[] { new ProgramBlock(0, 0, closer, true) }, OpenAt(1, opener, "x = 1"));
        Assert.IsEmpty(OpenAt(2, opener, "x = 1", closer));
    }

    [TestMethod]
    public void ALoopClosedByLoopUntil_IsClosed()
    {
        Assert.IsEmpty(OpenAt(2, "Do", "x = x + 1", "Loop Until x > 3"));
    }

    [TestMethod]
    public void AStatementBeforeTheBlock_IsInNoBlock()
    {
        Assert.IsEmpty(OpenAt(0, "x = 1", "If x Then"));
    }

    [TestMethod]
    public void AStatementWithALineEnd_LeavesNoBlockOpen()
    {
        Assert.IsEmpty(OpenAt(0, "If x Then\r\nDebug.Print 1"));
    }

    [TestMethod]
    public void AnIndexOfNoStatement_IsRefused()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OpenAt(1, "If x Then"));
    }
}
