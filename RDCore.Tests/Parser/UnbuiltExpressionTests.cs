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
/// is now modeled as a real <see cref="NewExpressionNode"/> (MS-VBAL §5.6.8), and <c>TypeOf...Is</c> as
/// a real <see cref="TypeOfIsExpressionNode"/> (MS-VBAL §5.6.9.4) — the trivia wrapping remains only as
/// the recovery-path fallback for a bare <c>TypeOf &lt;expr&gt;</c> with no <c>Is &lt;type&gt;</c>
/// following it.
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
    public void TypeOfIsExpression_BuildsARealTypeOfIsExpressionNode()
    {
        var result = Parse("Sub S()\r\nx = TypeOf x Is Foo\r\nEnd Sub");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var assignment = member.Children.OfType<AssignmentStatementNode>().Single();
        var typeOfIs = Assert.IsInstanceOfType<TypeOfIsExpressionNode>(assignment.Value);
        var operand = Assert.IsInstanceOfType<SimpleNameExpressionNode>(typeOfIs.Operand);
        Assert.AreEqual("x", operand.IdentifierName);
        var typeExpression = Assert.IsInstanceOfType<SimpleNameExpressionNode>(typeOfIs.TypeExpression);
        Assert.AreEqual("Foo", typeExpression.IdentifierName);
    }

    [TestMethod]
    public void TypeOfIsCondition_SurvivesInsideAnIfBlock()
    {
        var result = Parse("Sub S()\r\nIf TypeOf x Is Foo Then\r\nEnd If\r\nEnd Sub");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var ifBlock = member.Children.OfType<IfBlockStatementNode>().Single();
        Assert.IsInstanceOfType<TypeOfIsExpressionNode>(ifBlock.ConditionExpression);
    }

    [TestMethod]
    public void TypeOfExpressionWithoutIs_StaysUnbuiltTrivia()
        // TypeOf <expr> is only ever meaningful paired with Is <type> (MS-VBAL §5.6.9.4), but the
        // grammar itself doesn't enforce that pairing - typeofexpr is just another `expression`
        // alternative, so a bare `TypeOf x` with nothing following it is syntactically legal (if
        // semantically nonsensical). No enclosing IS relationalOp exists here to unwrap the trivia
        // ExitTypeofexpr always wraps its operand in, so it must survive as the final result rather
        // than leaking "x" up as if `TypeOf` had never been there.
    {
        var result = Parse("Sub S()\r\nx = TypeOf x\r\nEnd Sub");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var assignment = member.Children.OfType<AssignmentStatementNode>().Single();
        var trivia = Assert.IsInstanceOfType<UnbuiltExpressionTriviaNode>(assignment.Value);
        Assert.AreEqual("TypeOf x", trivia.Source);
        var inner = Assert.IsInstanceOfType<SimpleNameExpressionNode>(trivia.Inputs.Single());
        Assert.AreEqual("x", inner.IdentifierName);
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
