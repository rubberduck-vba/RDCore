using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// A block whose header or one of whose statements ends with an operator, or has one where an expression ends before a keyword (<c>For i = 1 - To 2</c>), is
/// still read as that block: its body is its own, it ends where its end is written, the statements after it are read as they are, and so is the procedure after it.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.6.1 Syntax Errors")]
public sealed class BlockHeaderRecoveryTests
{
    // c = 3 is in the block, b = 2 is after it.
    [TestMethod]
    [DataRow("Do\r\nc = 3\r\na = 1 -\r\nLoop Until x", DisplayName = "a statement of a Do ... Loop Until body")]
    [DataRow("Do\r\nc = 3\r\na = 1 +\r\nLoop Until x", DisplayName = "a statement of a Do ... Loop Until body, an operator the parse reads")]
    [DataRow("Do\r\nc = 3\r\nLoop Until x -", DisplayName = "the condition of a Loop Until")]
    [DataRow("Do\r\nFor i = 1 To 2 -\r\nc = 3\r\nNext\r\nLoop", DisplayName = "a For header, in a Do ... Loop")]
    [DataRow("Do\r\nFor i = 1 To 2 -\r\nc = 3\r\nNext\r\nLoop Until x", DisplayName = "a For header, in a Do ... Loop Until")]
    [DataRow("For i = 1 - To 2\r\nc = 3\r\nNext", DisplayName = "the start of a For")]
    [DataRow("For i = 1 + To 2\r\nc = 3\r\nNext", DisplayName = "the start of a For, an operator the parse reads")]
    [DataRow("For i = 1 To 2 - Step 1\r\nc = 3\r\nNext", DisplayName = "the end of a For, before its Step")]
    [DataRow("For i = 1 To 2 - -\r\nc = 3\r\nNext", DisplayName = "the end of a For, two operators")]
    [DataRow("If x - Then\r\nc = 3\r\nEnd If", DisplayName = "the condition of an If")]
    [DataRow("If x + Then\r\nc = 3\r\nEnd If", DisplayName = "the condition of an If, an operator the parse reads")]
    [DataRow("If x Then\r\nc = 0\r\nElseIf x - Then\r\nc = 3\r\nEnd If", DisplayName = "the condition of an ElseIf")]
    [DataRow("Do While x -\r\nc = 3\r\nLoop", DisplayName = "the condition of a Do While")]
    [DataRow("While x -\r\nc = 3\r\nWend", DisplayName = "the condition of a While")]
    [DataRow("Select Case x -\r\nCase 1\r\nc = 3\r\nEnd Select", DisplayName = "the expression of a Select Case")]
    [DataRow("With x -\r\nc = 3\r\nEnd With", DisplayName = "the expression of a With")]
    public void ABlockWithAnOperatorWhereAnExpressionEnds_IsTheBlockItIs(string block)
    {
        var source = $"Public Sub Main()\r\n{block}\r\nb = 2\r\nEnd Sub\r\nPublic Sub Two()\r\nd = 4\r\nEnd Sub\r\n";
        var lines = source.Split("\r\n").ToList();
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        var said = string.Join(" | ", parse.SyntaxErrors.Select(error => $"{error.Location.Range}: {error.Verbose}"));
        Assert.IsNotEmpty(parse.SyntaxErrors, "the operator is a syntax error");
        var members = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToList();
        var main = Assert.ContainsSingle(members.Where(member => member.Name == "Main"), said);
        Assert.AreEqual(lines.IndexOf("End Sub"), main.SourceLocation.Range.End.Line, $"the procedure ends at its End Sub; {said}");

        var c = Assert.ContainsSingle(main.Descendants().OfType<AssignmentStatementNode>().Where(node => node.SourceLocation.Range.Start.Line == lines.IndexOf("c = 3")), $"the body is kept; {said}");
        Assert.DoesNotContain(c, main.Children, $"the body is the block's; {said}");
        Assert.ContainsSingle(main.Children.OfType<AssignmentStatementNode>().Where(node => node.SourceLocation.Range.Start.Line == lines.IndexOf("b = 2")), $"the statement after the block is the procedure's; {said}");
        Assert.IsFalse(main.Descendants().OfType<UnbuiltStatementTriviaNode>().Any(node => node.Source.Contains("End", StringComparison.Ordinal)), $"no end of a block is left a statement no rule reads; {said}");

        var two = Assert.ContainsSingle(members.Where(member => member.Name == "Two"), $"the procedure after it is read; {said}");
        Assert.AreEqual(lines.LastIndexOf("End Sub"), two.SourceLocation.Range.End.Line, said);
    }
}
