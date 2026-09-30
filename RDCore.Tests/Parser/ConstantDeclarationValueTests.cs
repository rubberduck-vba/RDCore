using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Parser;

/// <summary>
/// The value expression of a <c>Const</c> declaration (MS-VBAL §5.4.3.2 / §5.2.3.3), which is the whole
/// of what a constant <em>is</em> — it has no storage anywhere to hold a value instead.
/// </summary>
/// <remarks>
/// The declarations listener stops capturing expressions once it is inside a procedure body past the
/// argument list, because the statement listener builds those instead. A procedure-local <c>Const</c> is
/// built by the declarations listener even so, and its value fell into that gap: the node came out
/// carrying its As-type clause and nothing else, so the constant had no value anywhere in the AST.
/// A condition expression and a loop header already open a capture window for exactly this reason; a
/// constant declaration now opens one too.
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL §5.4.3.2 Local Constant Declarations")]
public sealed class ConstantDeclarationValueTests
{
    private static ConstantDeclarationNode Constant(string source)
    {
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        return parse.SyntaxTree!.Children.SelectMany(Flatten).OfType<ConstantDeclarationNode>().Single();
    }

    private static IEnumerable<SyntaxNode> Flatten(SyntaxNode node)
        => [node, .. node.Children.SelectMany(Flatten)];

    private static ExpressionNode? ValueOf(ConstantDeclarationNode constant)
        => constant.Children.OfType<ExpressionNode>().FirstOrDefault(child => child is not AsTypeExpressionNode);

    [TestMethod]
    public void AModuleConstant_CarriesItsValueExpression()
    {
        var value = ValueOf(Constant("Attribute VB_Name = \"M\"\r\nPublic Const K As Long = 5\r\n"));

        Assert.AreEqual(5, Assert.IsInstanceOfType<LiteralExpressionNode>(value).StaticValue is VBIntegerValue v ? (int)v.Value : -1);
    }

    [TestMethod]
    public void ALocalConstant_CarriesItsValueExpression()
    {
        var value = ValueOf(Constant("Attribute VB_Name = \"M\"\r\nSub S()\r\nConst K As Long = 5\r\nEnd Sub\r\n"));

        Assert.AreEqual(5, Assert.IsInstanceOfType<LiteralExpressionNode>(value).StaticValue is VBIntegerValue v ? (int)v.Value : -1);
    }

    [TestMethod]
    public void ALocalConstant_WithANonLiteralExpression_CarriesTheWholeTree()
        // a constant expression is not always a literal, so the declaration keeps the expression rather
        // than a folded value: what reduces it is the host, where the operator semantics live.
        => Assert.IsInstanceOfType<VBBinaryOperatorExpressionNode>(
            ValueOf(Constant("Attribute VB_Name = \"M\"\r\nSub S()\r\nConst K As Long = 3 * 5\r\nEnd Sub\r\n")));

    [TestMethod]
    public void ALocalConstantsValue_DoesNotLeakIntoTheStatementsAroundIt()
    {
        // the capture window is the declaration's own, so the statement after it is still built by the
        // statement listener alone — no second copy of its expression from the declarations listener.
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(),
            "Attribute VB_Name = \"M\"\r\nSub S()\r\nConst K As Long = 5\r\nDebug.Print K\r\nEnd Sub\r\n");

        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.HasCount(1, member.Children.OfType<ConstantDeclarationNode>());
        Assert.HasCount(1, member.Children.SelectMany(Flatten).OfType<SimpleNameExpressionNode>());
    }
}
