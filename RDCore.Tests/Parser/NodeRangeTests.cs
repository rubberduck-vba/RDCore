using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// The range of a node is the text it was written as, from its first character to the one after its last: an LSP range is end-exclusive, and a statement of one token
/// (<c>Stop</c>) is four characters and not a range of nothing.
/// </summary>
[TestClass]
public sealed class NodeRangeTests
{
    private static string TextOf(string source, SourceRange range)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        int Offset(SourcePosition position) => lines.Take(position.Line).Sum(line => line.Length + 1) + position.Character;
        var text = string.Join("\n", lines);
        return text[Offset(range.Start)..Offset(range.End)];
    }

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node) => node.Children.SelectMany(Descendants).Prepend(node);

    private static (string Source, ModuleNode Tree) Parse(string body)
    {
        var source = $"Public Sub Main()\r\n{body}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return (source, parse.SyntaxTree!);
    }

    private static string StatementText(string statement)
    {
        var (source, tree) = Parse(statement);
        var node = tree.Children.OfType<MemberDeclarationNode>().Single().Children.First(child => child is StatementNode);
        return TextOf(source, node.SourceLocation.Range);
    }

    [TestMethod]
    [DataRow("Stop")]
    [DataRow("End")]
    [DataRow("Reset")]
    [DataRow("Exit Sub")]
    [DataRow("Return")]
    [DataRow("x = 1")]
    [DataRow("Debug.Print 1")]
    [DataRow("Debug.Print x; y")]
    [DataRow("Call Foo(1, 2)")]
    [DataRow("Foo 1, 2")]
    [DataRow("Set x = Nothing")]
    [DataRow("Close #1")]
    [DataRow("Open \"a\" For Input As #1")]
    [DataRow("GoTo Somewhere")]
    [DataRow("On Error Resume Next")]
    [DataRow("Erase a")]
    [DataRow("ReDim a(3)")]
    public void AStatement_IsTheTextItWasWrittenAs(string statement) => Assert.AreEqual(statement, StatementText(statement));

    [TestMethod]
    [DataRow("If x Then\r\ny = 1\r\nEnd If")]
    [DataRow("For i = 1 To 3\r\nNext")]
    [DataRow("Do\r\nLoop")]
    [DataRow("While x\r\nWend")]
    [DataRow("With x\r\n.y = 1\r\nEnd With")]
    [DataRow("Select Case x\r\nCase 1\r\nEnd Select")]
    public void ABlockStatement_EndsAfterItsCloser(string statement) => Assert.AreEqual(statement.Replace("\r\n", "\n"), StatementText(statement));

    [TestMethod]
    public void AStatementOnSeveralLines_EndsAtItsLastToken()
        => Assert.AreEqual("x = 1 + _\n    2", StatementText("x = 1 + _\r\n    2"));

    [TestMethod]
    public void AnExpression_IsTheTextItWasWrittenAs()
    {
        var (source, tree) = Parse("x = Left$(name, 1) & a.b & 42");

        var texts = Descendants(tree).OfType<ExpressionNode>().Select(expression => TextOf(source, expression.SourceLocation.Range)).ToArray();

        CollectionAssert.IsSubsetOf(new[] { "x", "Left$(name, 1)", "Left$", "name", "1", "a.b", "42", "Left$(name, 1) & a.b & 42" }, texts);
    }

    [TestMethod]
    public void AMember_IsTheTextOfItsWholeDeclaration()
    {
        var (source, tree) = Parse("Debug.Print 1");

        var member = tree.Children.OfType<MemberDeclarationNode>().Single();

        Assert.AreEqual(source.TrimEnd().Replace("\r\n", "\n"), TextOf(source, member.SourceLocation.Range));
    }

    [TestMethod]
    public void TheKeywordsOfADeclaration_AreInTheNodeOfItsFirstDeclarator()
    {
        string[] Texts(string body, Func<SyntaxNode, bool> select)
        {
            var (source, tree) = Parse(body);
            return [.. Descendants(tree).Where(select).Select(node => TextOf(source, node.SourceLocation.Range))];
        }

        CollectionAssert.AreEqual(new[] { "Dim a As Long", "b As String" }, Texts("Dim a As Long, b As String", node => node is VariableDeclarationNode));
        CollectionAssert.AreEqual(new[] { "Static n%" }, Texts("Static n%", node => node is VariableDeclarationNode));
        CollectionAssert.AreEqual(new[] { "Const c = 1", "d = 2" }, Texts("Const c = 1, d = 2", node => node is ConstantDeclarationNode));
        CollectionAssert.AreEqual(new[] { "ReDim Preserve a(3)", "b(4)" }, Texts("ReDim Preserve a(3), b(4)", node => node is RedimDeclarationNode));
    }

    [TestMethod]
    public void TheKeywordsOfAModuleLevelDeclaration_AreInItsNode()
    {
        const string source = "Private WithEvents w As Object\r\nPublic Const K As Long = 1\r\nDim x\r\nGlobal g As Long\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var texts = parse.SyntaxTree!.Children.Where(node => node is VariableDeclarationNode or ConstantDeclarationNode)
            .Select(node => TextOf(source, node.SourceLocation.Range)).ToArray();

        CollectionAssert.AreEqual(new[] { "Private WithEvents w As Object", "Public Const K As Long = 1", "Dim x", "Global g As Long" }, texts);
    }

    [TestMethod]
    public void TheNameOfADeclaration_IsAlsoWhereItIsWritten()
    {
        var (source, tree) = Parse("Dim a$, b As Long\r\nConst c = 1");

        var names = Descendants(tree).Select(node => node switch
        {
            VariableDeclarationNode { NameRange: { } range } => TextOf(source, range),
            ConstantDeclarationNode { NameRange: { } range } => TextOf(source, range),
            _ => null,
        }).OfType<string>().ToArray();

        CollectionAssert.AreEqual(new[] { "a$", "b", "c" }, names);
    }

    [TestMethod]
    [DataRow("Here:\r\nStop", typeof(LineLabelNode), "Here:")]
    [DataRow("10 Stop", typeof(LineNumberNode), "10")]
    [DataRow("20: Stop", typeof(LineNumberNode), "20:")]
    [DataRow("30 There: Stop", typeof(LineLabelNode), "There:")]
    [DataRow("-1 Stop", typeof(LineNumberNode), "-1")]
    [DataRow("&H10 Stop", typeof(LineNumberNode), "&H10")]
    public void ALabel_IsTheTextItWasWrittenAs_ColonIncluded(string body, Type kind, string expected)
    {
        var (source, tree) = Parse(body);

        var label = Descendants(tree).Single(node => node.GetType() == kind);

        Assert.AreEqual(expected, TextOf(source, label.SourceLocation.Range));
    }

    [TestMethod]
    public void NoNodeOfAModule_HasNoRange_ExceptWhatIsGenuinelyEmpty()
    {
        const string source = """
            Option Explicit
            Private Type T
                X As Long
            End Type
            Public Enum E
                A
                B = 2
            End Enum
            Private WithEvents w As Object
            Public Function Make(ByVal n As Long, Optional ByRef s As String = "x") As Collection
                Dim c As New Collection, i As Long
                For i = 1 To n
                    c.Add i, CStr(i)
                Next
                Set Make = c
            End Function
            """;

        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        foreach (var node in Descendants(parse.SyntaxTree!).Where(node => node is not ModuleNode))
        {
            var range = node.SourceLocation.Range;
            Assert.IsTrue(range.End > range.Start, $"{node.GetType().Name} has the empty range {range}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(TextOf(source, range)), $"{node.GetType().Name} {range} is no text");
        }
    }

    [TestMethod]
    public void TheHeaderOfAClassModule_IsKeptAsTrivia_AndLeavesNoStrayNodes()
    {
        const string source = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Thing\"\r\nOption Explicit\r\nPublic X As Long\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Thing.cls"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var header = parse.Trivia.OfType<ModuleHeaderTriviaNode>().ToArray();

        Assert.AreEqual(2, header.Length);
        StringAssert.StartsWith(header[0].Source, "VERSION");
        StringAssert.StartsWith(header[1].Source, "BEGIN");
        Assert.IsFalse(parse.SyntaxTree!.Children.OfType<ExpressionNode>().Any(), "the header left an expression among the module's nodes");
    }

    [TestMethod]
    public void AComment_IsTrivia_WhereverItIsWritten()
    {
        const string source = "' first\r\nOption Explicit ' after a directive\r\nPublic Sub Main()\r\n    Dim x As Long ' after a declaration\r\n    Rem a remark\r\n    x = 1 'after a statement\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var comments = parse.Trivia.OfType<CommentTriviaNode>().Select(node => TextOf(source, node.SourceLocation.Range)).ToArray();

        CollectionAssert.AreEqual(new[] { "' first", "' after a directive", "' after a declaration", "Rem a remark", "'after a statement" }, comments);
        Assert.IsFalse(Descendants(parse.SyntaxTree!).Any(node => node is CommentTriviaNode), "comments are not among the nodes of the tree");
    }

    [TestMethod]
    public void AnAnnotation_IsATrivia_InsideItsCommentLine_AndItsArgumentsAreNotNodesOfTheModule()
    {
        const string source = "'@Folder(\"Battleship.Model\") @PredeclaredId: why\r\nOption Explicit\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.cls"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var line = parse.Trivia.OfType<CommentTriviaNode>().Single();
        var annotations = parse.Trivia.OfType<AnnotationTriviaNode>().ToArray();

        Assert.AreEqual("'@Folder(\"Battleship.Model\") @PredeclaredId: why", TextOf(source, line.SourceLocation.Range));
        CollectionAssert.AreEqual(new[] { "Folder", "PredeclaredId" }, annotations.Select(annotation => annotation.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "@Folder(\"Battleship.Model\")", "@PredeclaredId" }, annotations.Select(annotation => TextOf(source, annotation.SourceLocation.Range)).ToArray());
        Assert.AreEqual(1, parse.SyntaxTree!.Children.Length, "only the Option Explicit directive is a node of the module");
    }

    [TestMethod]
    [DataRow("Option Base 1")]
    [DataRow("Option Base 0")]
    public void OptionBase_IsOnlyItsDirective(string directive)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), directive + "\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        Assert.AreEqual(1, parse.SyntaxTree!.Children.Length);
        Assert.AreEqual(directive, TextOf(directive, parse.SyntaxTree.Children[0].SourceLocation.Range));
    }

    [TestMethod]
    public void AChild_IsWithinItsParent()
    {
        var (_, tree) = Parse("If x Then\r\nFoo 1, Bar(2)\r\nElse\r\nDebug.Print a.b\r\nEnd If");

        foreach (var parent in Descendants(tree).Where(node => node is not ModuleNode))
        {
            foreach (var child in parent.Children)
            {
                var (inner, outer) = (child.SourceLocation.Range, parent.SourceLocation.Range);
                Assert.IsTrue(inner.Start >= outer.Start && inner.End <= outer.End, $"{child.GetType().Name} {inner} is not within {parent.GetType().Name} {outer}");
            }
        }
    }
}
