using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.Tests.Parser;

/// <summary>
/// An expression written back as code (<see cref="ExpressionWriter"/>) parses to the expression it was written from (<see cref="ExpressionEquivalence"/>):
/// every expression of the benchmark corpus, and the expressions fixes and refactorings write.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.6 Expressions")]
public sealed class ExpressionRoundTripTests
{
    private static readonly Uri Module = new("file:///c:/ws/Module1.bas");

    // the value of `x = <expression>`, in a With block of a procedure: a member of the block (`.Enabled`) is an expression there too.
    private static ExpressionNode? ValueOf(string expression)
    {
        var parse = new ModuleParser().Parse(Module, $"Sub Main()\r\n    With w\r\n        x = {expression}\r\n    End With\r\nEnd Sub\r\n");
        return parse.SyntaxErrors.IsEmpty ? parse.SyntaxTree!.Descendants().OfType<AssignmentStatementNode>().Single().Value : null;
    }

    private static string CorpusFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var corpus = Path.Combine(folder.FullName, "RDCore.CLI", "Bench", "Corpus");
            if (Directory.Exists(corpus))
            {
                return corpus;
            }
        }

        throw new DirectoryNotFoundException("the benchmark corpus is not above the test's folder");
    }

    // the expressions a statement is made of, whole: not the parts of another expression.
    private static IEnumerable<ExpressionNode> WholeExpressionsOf(SyntaxNode tree)
        => tree.Descendants().OfType<StatementNode>().SelectMany(statement => statement.ChildNodes().OfType<ExpressionNode>())
            .Where(expression => expression is not (MissingArgumentNode or NamedArgumentNode or ByValArgumentExpressionNode or PrintOutputItemNode
                or PrintSpcClauseNode or PrintTabClauseNode or AsTypeExpressionNode or VBTypedDeclarationExpressionNode or UnbuiltExpressionTriviaNode));

    [TestMethod]
    public void EveryExpressionOfTheCorpus_ParsesBackToItself()
    {
        var files = Directory.GetFiles(CorpusFolder()).Where(file => file.EndsWith(".bas") || file.EndsWith(".cls")).ToArray();
        Assert.IsNotEmpty(files);

        var failures = new List<string>();
        var count = 0;
        foreach (var file in files)
        {
            var parse = new ModuleParser().Parse(new Uri(file), File.ReadAllText(file));
            foreach (var expression in WholeExpressionsOf(parse.SyntaxTree!))
            {
                count++;
                var written = ExpressionWriter.Write(expression);
                if (written is null)
                {
                    failures.Add($"{Path.GetFileName(file)} {expression.Location.Range}: {expression.GetType().Name} not written");
                }
                else if (!ExpressionEquivalence.AreEquivalent(expression, ValueOf(written)))
                {
                    failures.Add($"{Path.GetFileName(file)} {expression.Location.Range}: `{written}` parses to another expression");
                }
            }
        }

        Assert.IsGreaterThan(100, count, "the corpus has expressions");
        Assert.IsEmpty(failures, string.Join("\r\n", failures));
    }

    [TestMethod]
    // fixes: RDC00209's targets.
    [DataRow("flags(i)", "flags(i)")]
    [DataRow(".Enabled", ".Enabled")]
    [DataRow("Range(\"A1\").Locked", "Range(\"A1\").Locked")]
    // refactorings: a field on the state, call sites, arguments as written.
    [DataRow("this.count", "this.count")]
    [DataRow("this.[Some Name]", "this.[Some Name]")]
    [DataRow("this.count(1)", "this.count(1)")]
    [DataRow("obj.Items(i).Value", "obj.Items(i).Value")]
    [DataRow("Name(a, b)", "Name(a, b)")]
    [DataRow("Name((x), -x, a & \"b\", x!Key, Me.Prop, Not IsMissing(p))", "Name((x), -x, a & \"b\", x!Key, Me.Prop, Not IsMissing(p))")]
    [DataRow("Name(Arg:=1)", "Name(Arg:=1)")]
    [DataRow("Left$(t$, 1)", "Left$(t$, 1)")]
    [DataRow("n& + 1", "n& + 1")]
    // what the tree does not keep is written one way; what it keeps, as it was.
    [DataRow("a.b( 1 )", "a.b(1)")]
    [DataRow("(a+b)*c", "(a + b) * c")]
    [DataRow("&HFF + 1#", "&HFF + 1#")]
    [DataRow("\"say \"\"hi\"\"\"", "\"say \"\"hi\"\"\"")]
    [DataRow("#1/2/2020#", "#1/2/2020#")]
    [DataRow("TypeOf o Is Collection", "TypeOf o Is Collection")]
    [DataRow("New Collection", "New Collection")]
    [DataRow("UBound(a, 2) - LBound(a)", "UBound(a, 2) - LBound(a)")]
    [DataRow("Array(1, 2)", "Array(1, 2)")]
    [DataRow("a Mod b Like c", "a Mod b Like c")]
    public void AnExpression_IsWrittenAsItReads_AndParsesBackToItself(string source, string expected)
    {
        var expression = ValueOf(source);
        Assert.IsNotNull(expression, "the expression parses");

        var written = ExpressionWriter.Write(expression);

        Assert.AreEqual(expected, written);
        Assert.IsTrue(ExpressionEquivalence.AreEquivalent(expression, ValueOf(written!)));
    }

    [TestMethod]
    [DataRow("A.B(1)", "a.b( 1 )", true)]
    [DataRow("&HFF", "255", true)]
    [DataRow("(x)", "x", false)]
    [DataRow("s$", "s", false)]
    [DataRow(".Enabled", "b.Enabled", false)]
    [DataRow("a + b", "b + a", false)]
    public void TwoExpressions_AreTheSameWhenTheyAreMadeOfTheSameOperations(string left, string right, bool expected)
        => Assert.AreEqual(expected, ExpressionEquivalence.AreEquivalent(ValueOf(left), ValueOf(right)));
}
