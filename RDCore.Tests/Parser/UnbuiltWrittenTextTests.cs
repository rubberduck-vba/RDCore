using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// What the parse could not build is kept as it is written: an unbuilt expression or statement ends at its last token, and its source is the text at its
/// range. The blanks the parse read after it, a line continuation, and a token ANTLR made up for one missing (the <c>)</c> of <c>Foo(-</c>) are no part of it.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.6.1 Syntax Errors")]
public sealed class UnbuiltWrittenTextTests
{
    private static string TextOf(string source, SourceRange range)
    {
        var lines = source.Split("\r\n");
        return range.Start.Line == range.End.Line
            ? lines[range.Start.Line][range.Start.Character..range.End.Character]
            : string.Join("\n", [lines[range.Start.Line][range.Start.Character..], .. lines[(range.Start.Line + 1)..range.End.Line], lines[range.End.Line][..range.End.Character]]);
    }

    [TestMethod]
    [DataRow("Call Foo(-", DisplayName = "a call whose parenthesis ANTLR made up")]
    [DataRow("Call Foo(-  ", DisplayName = "a call whose parenthesis ANTLR made up, blanks after it")]
    [DataRow("a = (1 +  ", DisplayName = "an operation in parentheses, blanks after it")]
    [DataRow("a = -  ", DisplayName = "a negation, blanks after it")]
    [DataRow("a = Foo.  ", DisplayName = "a member access, blanks after it")]
    [DataRow("a = .  ", DisplayName = "a member access in a With, blanks after it")]
    [DataRow("x = Foo(a:=1 -  ", DisplayName = "a named argument, blanks after it")]
    [DataRow("Debug.Print Spc(  ", DisplayName = "a Spc clause, blanks after it")]
    [DataRow("a = - _\r\n", DisplayName = "a negation, a line continuation and a blank line")]
    [DataRow("Do Until  \r\na = 1\r\nLoop", DisplayName = "a Do whose condition is missing, blanks after it")]
    public void AnUnbuiltNode_IsItsTextAsWritten(string statement)
    {
        var source = $"Public Sub Main()\r\n{statement}\r\nb = 2\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        var unbuilt = parse.SyntaxTree!.Descendants()
            .Select(node => node switch
            {
                UnbuiltExpressionTriviaNode expression => (Node: (SyntaxNode)expression, expression.Source),
                UnbuiltStatementTriviaNode trivia => (Node: trivia, trivia.Source),
                _ => (Node: null!, Source: null!),
            })
            .Where(row => row.Node is not null)
            .ToList();

        Assert.IsNotEmpty(unbuilt, "the statement holds what the parse could not build");
        foreach (var (node, text) in unbuilt)
        {
            Assert.AreEqual(TextOf(source, node.SourceLocation.Range), text, $"the source of the {node.GetType().Name} at {node.SourceLocation.Range} is the text at its range");
            Assert.DoesNotContain("<missing", text, "no token ANTLR made up");
            Assert.IsFalse(char.IsWhiteSpace(text[^1]) || text.EndsWith('_'), $"«{text}» ends at its last token");
        }
    }
}
