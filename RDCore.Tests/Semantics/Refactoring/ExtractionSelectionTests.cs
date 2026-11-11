using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// A selection extracts whole statements: it grows to the smallest whole run of statements of one block that holds it, and is refused, naming the statement, when it
/// cuts a block statement in half (<strong>Extract Method §2</strong>).
/// </summary>
[TestClass]
public sealed class ExtractionSelectionTests
{
    // the lines of the procedure, the header first (line 0).
    private static readonly string[] Lines =
    [
        "Public Sub S(ByVal n As Long)",
        "Dim x As Long",
        "x = 1",
        "If x > 0 Then",
        "    x = 2",
        "    x = 3",
        "Else",
        "    x = 4",
        "End If",
        "Debug.Print x",
        "For n = 1 To 3",
        "    x = n",
        "Next",
        "End Sub",
    ];

    // the lines of a procedure with an #If block in it, the header first (line 0).
    private static readonly string[] Conditional =
    [
        "Public Sub S(ByVal n As Long)",
        "Dim x As Long",
        "#If True Then",
        "x = 1",
        "x = 2",
        "#Else",
        "x = 3",
        "#End If",
        "Debug.Print x",
        "End Sub",
    ];

    private static (MemberDeclarationNode Procedure, InstructionList Instructions, ExtractionDirectives Directives) Procedure(string[]? lines = null)
    {
        var text = string.Join("\r\n", lines ?? Lines) + "\r\n";
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), text);
        Assert.IsEmpty(result.SyntaxErrors);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        return (member, InstructionListLowering.Lower(new StatementBlock([.. member.Children])).InstructionList, ExtractionDirectives.Of(result.PrecompilerTrivia, text));
    }

    private static ExtractionSelection Snapped(int startLine, int startCharacter, int endLine, int endCharacter, string[]? lines = null)
    {
        var (procedure, instructions, directives) = Procedure(lines);
        Assert.IsTrue(ExtractionSelection.TrySnap(procedure, instructions, directives, new SourceRange(startLine, startCharacter, endLine, endCharacter), out var selection,
            out var refusal, out _), $"refused: {refusal}");
        return selection!;
    }

    private static (ExtractionSelectionRefusal Refusal, string? Cut) Refused(int startLine, int endLine, string[]? lines = null)
    {
        var (procedure, instructions, directives) = Procedure(lines);
        Assert.IsFalse(ExtractionSelection.TrySnap(procedure, instructions, directives, new SourceRange(startLine, 0, endLine, 1), out _, out var refusal, out var cut));
        return (refusal, cut?.GetType().Name);
    }

    private static string[] TextOf(ExtractionSelection selection)
        => [.. selection.Statements.Select(statement => Lines[statement.SourceLocation.Range.Start.Line].Trim())];

    [TestMethod]
    public void APartOfAStatement_GrowsToTheStatement()
    {
        var selection = Snapped(2, 2, 2, 3);

        CollectionAssert.AreEqual(new[] { "x = 1" }, TextOf(selection));
        Assert.AreEqual(2, selection.Range.Start.Line);
        Assert.AreEqual(0, selection.Range.Start.Character);
    }

    // an editor selects whole lines up to the start of the next one, which is not in the selection.
    [TestMethod]
    public void AnEndAtTheStartOfALine_EndsOnTheLineBefore()
        => CollectionAssert.AreEqual(new[] { "x = 1" }, TextOf(Snapped(2, 0, 3, 0)));

    [TestMethod]
    public void AnEndInsideALine_TakesThatLine()
        => CollectionAssert.AreEqual(new[] { "x = 1", "If x > 0 Then" }, TextOf(Snapped(2, 0, 8, 1)));

    [TestMethod]
    public void ARunOfStatements_HoldsABlockStatementWhole()
        => CollectionAssert.AreEqual(new[] { "x = 1", "If x > 0 Then", "Debug.Print x" }, TextOf(Snapped(2, 0, 9, 3)));

    [TestMethod]
    public void StatementsInsideABlock_AreExtractedFromIt()
        => CollectionAssert.AreEqual(new[] { "x = 2", "x = 3" }, TextOf(Snapped(4, 4, 5, 5)));

    [TestMethod]
    [DataRow(3, 5, DisplayName = "from the If line into its body")]
    [DataRow(5, 8, DisplayName = "from its body to the End If line")]
    [DataRow(3, 8, DisplayName = "from the If line to the End If line")]
    public void AnEndOnTheLineOfABlockStatement_TakesItWhole(int startLine, int endLine)
        => CollectionAssert.AreEqual(new[] { "If x > 0 Then" }, TextOf(Snapped(startLine, 0, endLine, 1)));

    [TestMethod]
    [DataRow(2, 4, DisplayName = "from before the If into its body")]
    [DataRow(5, 9, DisplayName = "from its body to after it")]
    [DataRow(4, 7, DisplayName = "from the Then branch to the Else branch")]
    public void ASelectionThatCutsABlockStatement_IsRefused_NamingIt(int startLine, int endLine)
    {
        var (refusal, cut) = Refused(startLine, endLine);

        Assert.AreEqual(ExtractionSelectionRefusal.CutsABlock, refusal);
        Assert.AreEqual(nameof(IfBlockStatementNode), cut);
    }

    [TestMethod]
    [DataRow(0, 2, DisplayName = "the header")]
    [DataRow(12, 13, DisplayName = "the End Sub line")]
    public void ASelectionOnTheProceduresOwnLines_IsRefused(int startLine, int endLine)
        => Assert.AreEqual(ExtractionSelectionRefusal.NoStatement, Refused(startLine, endLine).Refusal);

    [TestMethod]
    public void TheRegion_IsTheInstructionsOfTheStatements_AndTheClosersOfTheirBlocks()
    {
        var (procedure, instructions, directives) = Procedure();
        Assert.IsTrue(ExtractionSelection.TrySnap(procedure, instructions, directives, new SourceRange(10, 0, 12, 4), out var selection, out _, out _));

        var region = selection!.Region.Order().ToArray();
        Assert.IsTrue(instructions.TryGetOffsetAtLine(10, out var opener));
        Assert.AreEqual(opener, region[0]);
        Assert.AreEqual(InstructionKind.ForNext, instructions.Items[region[^1]].Kind, "the Next, which has no statement of its own");
        CollectionAssert.AreEqual(Enumerable.Range(region[0], region.Length).ToArray(), region, "contiguous");
    }

    // a Dim is written in the block as a statement is: what moves with the selection, or out of it (§3.2), is the declaration of the variable it holds.
    [TestMethod]
    public void ADeclarationOfALocal_IsTakenWithTheStatementsAfterIt()
    {
        var selection = Snapped(1, 0, 2, 3);

        CollectionAssert.AreEqual(new[] { "x = 1" }, TextOf(selection));
        Assert.AreEqual("x", ((VariableDeclarationNode)selection.Declarations.Single()).Name);
        Assert.AreEqual(1, selection.Range.Start.Line, "the selection starts at the Dim");
    }

    [TestMethod]
    public void ADeclarationAlone_HoldsNoStatement()
        => Assert.AreEqual(ExtractionSelectionRefusal.NoStatement, Refused(1, 1).Refusal);

    [TestMethod]
    public void ADeclarationInsideABlock_IsOneOfItsLines()
    {
        string[] lines = ["Public Sub S()", "If True Then", "    Dim y As Long", "    y = 1", "End If", "End Sub"];
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), string.Join("\r\n", lines) + "\r\n");
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var instructions = InstructionListLowering.Lower(new StatementBlock([.. member.Children])).InstructionList;

        Assert.IsTrue(ExtractionSelection.TrySnap(member, instructions, ExtractionDirectives.None, new SourceRange(2, 4, 3, 5), out var selection, out var refusal, out _),
            $"refused: {refusal}");
        Assert.AreEqual("y", ((VariableDeclarationNode)selection!.Declarations.Single()).Name);
        Assert.AreEqual(2, selection.Range.Start.Line, "inside the If's body, not grown to the If");
    }

    [TestMethod]
    public void TheRegionOfAnIf_DoesNotHoldTheStatementAfterIt()
    {
        var (procedure, instructions, directives) = Procedure();
        Assert.IsTrue(ExtractionSelection.TrySnap(procedure, instructions, directives, new SourceRange(3, 0, 8, 6), out var selection, out _, out _));

        Assert.IsTrue(instructions.TryGetOffsetAtLine(9, out var after));
        Assert.DoesNotContain(after, selection!.Region);
        Assert.IsTrue(instructions.TryGetOffsetAtLine(7, out var inElse));
        Assert.Contains(inElse, selection.Region);
    }

    // an #If block moves whole: the selection is grown from the #If to the end of the #End If line, every branch included.
    [TestMethod]
    [DataRow(2, 7, DisplayName = "from the #If to the #End If")]
    [DataRow(1, 8, DisplayName = "the statements around it")]
    public void ASelectionThatHoldsAnIfBlock_HoldsItWhole(int startLine, int endLine)
    {
        var selection = Snapped(startLine, 0, endLine, 1, Conditional);

        Assert.IsTrue(selection.Range.Start <= new SourcePosition(2, 0), $"{selection.Range}");
        Assert.IsTrue(selection.Range.End >= new SourcePosition(7, "#End If".Length), $"{selection.Range}");
    }

    [TestMethod]
    [DataRow(3, 6, DisplayName = "from a branch to the next")]
    [DataRow(6, 8, DisplayName = "from a branch to after the block")]
    [DataRow(1, 5, DisplayName = "from before the block to its #Else")]
    public void ASelectionThatCutsAnIfBlock_IsRefused(int startLine, int endLine)
        => Assert.AreEqual(ExtractionSelectionRefusal.CutsADirectiveBlock, Refused(startLine, endLine, Conditional).Refusal);

    [TestMethod]
    public void StatementsInsideABranch_AreExtractedFromIt()
    {
        var selection = Snapped(3, 0, 4, 1, Conditional);

        Assert.AreEqual(new SourceRange(3, 0, 4, "x = 2".Length), selection.Range);
    }

    // the lines of a procedure with statements separated by colons, the header first (line 0): line 2 holds three statements, line 3 a For block, line 4 a
    // line number and two statements.
    private static readonly string[] Colons =
    [
        "Public Sub S(ByVal n As Long)",
        "Dim x As Long",
        "x = 1: x = x + 1: Debug.Print x",
        "For n = 1 To 3: x = n: Next",
        "10 x = 2: x = x + 2",
        "End Sub",
    ];

    private static string[] ColonTextOf(ExtractionSelection selection)
        => [.. selection.Statements.Select(statement => statement.SourceLocation.Range)
            .Select(range => Colons[range.Start.Line][range.Start.Character..(range.End.Line == range.Start.Line ? range.End.Character : Colons[range.Start.Line].Length)])];

    [TestMethod]
    [DataRow(2, 8, 2, 10, DisplayName = "inside it")]
    [DataRow(2, 7, 2, 16, DisplayName = "exactly it")]
    [DataRow(2, 6, 2, 16, DisplayName = "from the separator before it")]
    [DataRow(2, 9, 2, 17, DisplayName = "to the separator after it")]
    [DataRow(2, 7, 2, 7, DisplayName = "a cursor at its start")]
    public void APartOfALineOfSeveralStatements_TakesTheStatementsItTouches_AndNotTheLine(int startLine, int startCharacter, int endLine, int endCharacter)
    {
        var selection = Snapped(startLine, startCharacter, endLine, endCharacter, Colons);

        CollectionAssert.AreEqual(new[] { "x = x + 1" }, ColonTextOf(selection));
        Assert.AreEqual(new SourceRange(2, 7, 2, 16), selection.Range);
    }

    [TestMethod]
    public void ASelectionAcrossTwoStatementsOfALine_TakesBoth()
        => CollectionAssert.AreEqual(new[] { "x = 1", "x = x + 1" }, ColonTextOf(Snapped(2, 2, 2, 10, Colons)));

    [TestMethod]
    public void AWholeLineOfSeveralStatements_TakesThemAll()
        => CollectionAssert.AreEqual(new[] { "x = 1", "x = x + 1", "Debug.Print x" }, ColonTextOf(Snapped(2, 0, 3, 0, Colons)));

    [TestMethod]
    public void AStatementInsideABlockWrittenOnOneLine_TakesTheBlockWhole()
        => CollectionAssert.AreEqual(new[] { "For n = 1 To 3: x = n: Next" }, ColonTextOf(Snapped(3, 16, 3, 21, Colons)));

    // a line number opens its line: an end on it takes it and the statement after it, and a start past it leaves it to name the call.
    [TestMethod]
    [DataRow(0, 1, "10", DisplayName = "on the line number")]
    [DataRow(3, 4, null, DisplayName = "past it")]
    public void ASelectionOnALineThatOpensWithALineNumber_TakesTheStatementAfterIt(int startCharacter, int endCharacter, string? label)
    {
        var selection = Snapped(4, startCharacter, 4, endCharacter, Colons);

        CollectionAssert.AreEqual(new[] { "x = 2" }, ColonTextOf(selection));
        CollectionAssert.AreEqual(label is null ? Array.Empty<string>() : new[] { label }, selection.Labels.ToArray());
    }

    [TestMethod]
    public void ACursorBetweenTwoStatementsOfALine_HoldsNoStatement()
    {
        var (procedure, instructions, directives) = Procedure(Colons);

        Assert.IsFalse(ExtractionSelection.TrySnap(procedure, instructions, directives, new SourceRange(2, 6, 2, 6), out _, out var refusal, out _));
        Assert.AreEqual(ExtractionSelectionRefusal.NoStatement, refusal);
    }
}
