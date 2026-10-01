using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;

namespace RDCore.Tests.Parser;

/// <summary>
/// An argument written with <c>ByVal</c> (<strong>MS-VBAL §5.6.13.1</strong>) keeps the keyword in the tree: dropped,
/// <c>Foo ByVal x</c> was <c>Foo x</c>, an argument aliased to a <c>ByRef</c> parameter where it was written not to be.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.13.1 Argument Lists")]
public sealed class ByValArgumentParserTests
{
    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static ModuleParseResult Parse(string source) => new ModuleParser().Parse(TestUri.TestModuleUri(), source);

    [TestMethod]
    public void AParenthesizedCallArgument_WrittenWithByVal_IsAByValArgumentNode()
    {
        var result = Parse("Public Const N = Foo(ByVal x)");

        Assert.IsTrue(result.IsSuccess, string.Join("; ", result.SyntaxErrors.Select(error => error.Verbose)));
        var index = Descendants(result.SyntaxTree!).OfType<IndexExpressionNode>().Single();
        var argument = Assert.IsInstanceOfType<ByValArgumentExpressionNode>(index.Arguments.Single());
        Assert.AreEqual("x", Assert.IsInstanceOfType<SimpleNameExpressionNode>(argument.Operand).IdentifierName);
    }

    [TestMethod]
    public void AnArgumentWrittenWithoutByVal_IsNotWrapped()
    {
        var result = Parse("Public Const N = Foo(x)");

        var index = Descendants(result.SyntaxTree!).OfType<IndexExpressionNode>().Single();
        Assert.IsInstanceOfType<SimpleNameExpressionNode>(index.Arguments.Single());
    }

    [TestMethod]
    public void OnlyTheArgumentWrittenWithByVal_IsWrapped()
    {
        var result = Parse("Public Const N = Foo(x, ByVal y, z)");

        var index = Descendants(result.SyntaxTree!).OfType<IndexExpressionNode>().Single();
        Assert.IsInstanceOfType<SimpleNameExpressionNode>(index.Arguments[0]);
        Assert.IsInstanceOfType<ByValArgumentExpressionNode>(index.Arguments[1]);
        Assert.IsInstanceOfType<SimpleNameExpressionNode>(index.Arguments[2]);
    }

    [TestMethod]
    public void ANamedArgument_WrittenWithByVal_HoldsTheByValArgument()
    {
        var result = Parse("Public Const N = Foo(Bar:=ByVal x)");

        var named = (NamedArgumentNode)Descendants(result.SyntaxTree!).OfType<IndexExpressionNode>().Single().Arguments.Single();
        Assert.IsInstanceOfType<ByValArgumentExpressionNode>(named.Value);
    }

    [TestMethod]
    public void ACallStatementArgument_WrittenWithByVal_IsAByValArgumentNode()
    {
        var result = Parse("Public Sub S()\r\nFoo ByVal x, y\r\nEnd Sub\r\n");

        Assert.IsTrue(result.IsSuccess, string.Join("; ", result.SyntaxErrors.Select(error => error.Verbose)));
        var byVal = Descendants(result.SyntaxTree!).OfType<ByValArgumentExpressionNode>().Single();
        Assert.AreEqual("x", Assert.IsInstanceOfType<SimpleNameExpressionNode>(byVal.Operand).IdentifierName);
    }

    [TestMethod]
    public void ByValInACallIsNotASyntaxError_WhetherItIsValidDependsOnTheCallee()
    {
        // MS-VBAL §5.6.13.1: it is the argument list of an external procedure's invocation that may have it, and the
        // parser cannot tell what is being invoked.
        Assert.IsTrue(Parse("Public Sub S()\r\nFoo ByVal x\r\nEnd Sub\r\n").IsSuccess);
    }

    [TestMethod]
    public void ARaiseEventArgument_WrittenWithByVal_IsASyntaxError_AndStaysInTheTree()
    {
        var result = Parse("Public Sub S()\r\nRaiseEvent Changed(ByVal x)\r\nEnd Sub\r\n");

        var error = result.SyntaxErrors.Single();
        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId);
        StringAssert.Contains(error.Verbose, "RaiseEvent");
        var raise = Descendants(result.SyntaxTree!).OfType<KeywordStatementNode>().Single(statement => statement.Token == "RaiseEvent");
        Assert.IsInstanceOfType<ByValArgumentExpressionNode>(raise.Inputs.Last());
    }

    [TestMethod]
    public void ARaiseEventArgumentWithoutByVal_IsValid()
    {
        var result = Parse("Public Sub S()\r\nRaiseEvent Changed(x, 1)\r\nEnd Sub\r\n");

        Assert.IsTrue(result.IsSuccess, string.Join("; ", result.SyntaxErrors.Select(error => error.Verbose)));
        var raise = Descendants(result.SyntaxTree!).OfType<KeywordStatementNode>().Single(statement => statement.Token == "RaiseEvent");
        Assert.IsFalse(raise.Inputs.OfType<ByValArgumentExpressionNode>().Any());
    }
}
