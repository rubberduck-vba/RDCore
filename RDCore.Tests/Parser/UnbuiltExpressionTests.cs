using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.Tests.Parser;

/// <summary>
/// Adversarial review, PRs #208-224, "worth knowing": <c>New &lt;class&gt;</c> and
/// <c>TypeOf &lt;expr&gt; Is &lt;type&gt;</c> had no dedicated AST node, and the parser wasn't just
/// leaving that position unbuilt — it was silently erasing the keyword and leaking the inner
/// sub-expression up as if it were a plain operand, so `New Collection` read back as a bare reference to
/// "Collection", and `TypeOf x Is Foo` read back as a plain `x Is Foo` identity comparison. Both parsed
/// cleanly (IsSuccess=true, no syntax error) with the wrong meaning. Per author direction: "build
/// nothing" is itself wrong here too — an unbuilt-but-recognized construct must get an
/// <see cref="UnbuiltExpressionTriviaNode"/> (preserving the exact source text and whatever the
/// grammar's own walk already built underneath), not silence and not a misrepresentation. <c>New</c>
/// is now modeled as a real <see cref="NewExpressionNode"/> (MS-VBAL §5.6.8); <c>TypeOf...Is</c>
/// remains unmodeled, still wrapped in trivia.
/// </summary>
[TestClass]
public sealed class UnbuiltExpressionTests
{
    private static readonly Uri Uri = TestUri.TestModuleUri();

    private static ModuleParseResult Parse(string source)
        => new ModuleParser().Parse(Uri, source);

    [TestMethod]
    public void NewExpression_BuildsARealNewExpressionNode()
    {
        var result = Parse("Sub S()\r\nSet x = New Collection\r\nEnd Sub");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var assignment = member.Children.OfType<AssignmentStatementNode>().Single();
        var newExpression = Assert.IsInstanceOfType<NewExpressionNode>(assignment.Value);
        var typeExpression = Assert.IsInstanceOfType<SimpleNameExpressionNode>(newExpression.TypeExpression);
        Assert.AreEqual("Collection", typeExpression.IdentifierName);
    }

    [TestMethod]
    public void TypeOfIsExpression_BuildsUnbuiltTrivia_NotAPlainIsComparison()
    {
        var result = Parse("Sub S()\r\nx = TypeOf x Is Foo\r\nEnd Sub");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var assignment = member.Children.OfType<AssignmentStatementNode>().Single();
        var isOp = Assert.IsInstanceOfType<VBBinaryOperatorExpressionNode>(assignment.Value);
        Assert.AreEqual(Tokens.CompareIsOp, isOp.Token);
        var trivia = Assert.IsInstanceOfType<UnbuiltExpressionTriviaNode>(isOp.Left);
        Assert.AreEqual("TypeOf x", trivia.Source);
        var inner = Assert.IsInstanceOfType<SimpleNameExpressionNode>(trivia.Inputs.Single());
        Assert.AreEqual("x", inner.IdentifierName);
        var right = Assert.IsInstanceOfType<SimpleNameExpressionNode>(isOp.Right);
        Assert.AreEqual("Foo", right.IdentifierName);
    }

    [TestMethod]
    public void TypeOfIsCondition_SurvivesInsideAnIfBlock()
    {
        var result = Parse("Sub S()\r\nIf TypeOf x Is Foo Then\r\nEnd If\r\nEnd Sub");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var ifBlock = member.Children.OfType<IfBlockStatementNode>().Single();
        var isOp = Assert.IsInstanceOfType<VBBinaryOperatorExpressionNode>(ifBlock.ConditionExpression);
        Assert.IsInstanceOfType<UnbuiltExpressionTriviaNode>(isOp.Left);
    }

    [TestMethod]
    public void NewExpression_DoesNotDisruptSiblingDeclarations()
    {
        const string source = """
            Sub S()
                Dim a As Long
                Set x = New Collection
                Dim b As Long
            End Sub
            """;

        var result = Parse(source);

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.HasCount(2, member.Children.OfType<VariableDeclarationNode>());
        Assert.ContainsSingle(member.Children.OfType<AssignmentStatementNode>());
    }
}
