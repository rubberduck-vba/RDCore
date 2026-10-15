using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;

namespace RDCore.Tests.Parser;

/// <summary>
/// <c>Date</c> is a <em>reserved-type-identifier</em> and a <em>reserved-name</em> both (<strong>MS-VBAL §3.3.5.2</strong>): it is a type where a declaration
/// names one, and in an expression it is "used ... as if it was a normal program defined entity" - a name, which is the <c>Date</c> property of the library.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.3.5.2 Reserved Identifiers and IDENTIFIER")]
public sealed class ReservedNameDateTests
{
    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node)
        => node.Children.SelectMany(child => Descendants(child).Prepend(child));

    private static ModuleParseResult Parse(string body)
    {
        var result = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Run()\r\n{body}\r\nEnd Sub\r\n");
        Assert.IsTrue(result.IsSuccess, string.Join("; ", result.SyntaxErrors.Select(error => error.Verbose)));
        return result;
    }

    [TestMethod]
    [DataRow("x = Date")]
    [DataRow("Debug.Print Date")]
    [DataRow("x = Date + 1")]
    [DataRow("x = Year(Date)")]
    [DataRow("If Date > d Then x = 1")]
    public void DateInAnExpression_IsASimpleName(string statement)
    {
        var tree = Parse(statement).SyntaxTree!;

        Assert.IsTrue(Descendants(tree).OfType<SimpleNameExpressionNode>().Any(name => name.IdentifierName == "Date"), "Date is a name there");
        Assert.IsFalse(Descendants(tree).OfType<UnbuiltExpressionTriviaNode>().Any(), "and nothing is left unbuilt");
    }

    [TestMethod]
    public void DateInADeclaration_IsStillTheType()
    {
        var tree = Parse("Dim d As Date").SyntaxTree!;

        Assert.IsFalse(Descendants(tree).OfType<SimpleNameExpressionNode>().Any(name => name.IdentifierName == "Date"));
    }
}
