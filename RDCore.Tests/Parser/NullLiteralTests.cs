using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Parser;

/// <summary>
/// The <c>Null</c> literal — the grammar's <c>variantLiteralIdentifier : EMPTY | NULL</c>.
/// </summary>
/// <remarks>
/// <c>Null</c> produced no node at all until a <c>Write #</c> test asked for one, and a literal with no node
/// evaluates to an internal error rather than to anything a program could use. It is a <c>Variant</c> subtype
/// rather than an object, which is why it sits with <c>Empty</c> and not with <c>Nothing</c>: <c>Nothing</c>
/// is the absent <em>reference</em>, <c>Null</c> the absent <em>value</em>.
/// </remarks>
[TestClass]
public sealed class NullLiteralTests
{
    private static LiteralExpressionNode Literal(string expression)
    {
        var parse = new ModuleParser().Parse(
            new Uri("file:///c:/ws/Mod1.bas"), $"Sub Foo()\r\nx = {expression}\r\nEnd Sub\r\n");

        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        return parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single()
            .Children.SelectMany(Flatten).OfType<LiteralExpressionNode>().Single();
    }

    private static IEnumerable<SyntaxNode> Flatten(SyntaxNode node)
        => [node, .. node.Children.SelectMany(Flatten)];

    [TestMethod]
    public void Null_IsALiteralCarryingTheNullValue()
        => Assert.IsInstanceOfType<VBNullValue>(Literal("Null").StaticValue);

    [TestMethod]
    public void Empty_IsALiteralCarryingTheEmptyValue()
        => Assert.IsInstanceOfType<VBEmptyValue>(Literal("Empty").StaticValue);

    [TestMethod]
    public void Nothing_IsALiteralCarryingTheNothingValue()
        => Assert.IsInstanceOfType<VBObjectValue>(Literal("Nothing").StaticValue);
}
