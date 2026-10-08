using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.Tests.Parser;

/// <summary>
/// The type-declaration character a name is written with is part of the name: <c>Environ$</c> is not <c>Environ</c>, and a tree that kept only the identifier would
/// hand the wrong member of the library to everything that reads it.
/// </summary>
[TestClass]
public sealed class TypeHintedNameTests
{
    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node) => node.Children.SelectMany(Descendants).Prepend(node);

    private static IReadOnlyList<SimpleNameExpressionNode> Names(string body)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{body}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return [.. Descendants(parse.SyntaxTree!).OfType<SimpleNameExpressionNode>()];
    }

    [TestMethod]
    [DataRow("x = Left$(a, 1)", "Left", "$")]
    [DataRow("x = Environ$(\"PATH\")", "Environ", "$")]
    [DataRow("n% = 1", "n", "%")]
    [DataRow("Debug.Print total&", "total", "&")]
    [DataRow("Debug.Print r#", "r", "#")]
    public void ANameWrittenWithATypeDeclarationCharacter_KeepsIt(string statement, string name, string hint)
    {
        var written = Names(statement).Single(candidate => candidate.IdentifierName == name);

        Assert.AreEqual(hint, written.TypeHint);
        Assert.AreEqual(name + hint, written.WrittenName);
        CollectionAssert.AreEqual(new[] { name + hint, name }, written.LookupNames.ToArray(), "as written first, and then as the declaration of the identifier");
    }

    [TestMethod]
    public void ANameWrittenWithNone_HasNone()
    {
        var written = Names("x = y").Single(candidate => candidate.IdentifierName == "y");

        Assert.IsNull(written.TypeHint);
        CollectionAssert.AreEqual(new[] { "y" }, written.LookupNames.ToArray());
    }

    [TestMethod]
    public void AMemberWrittenWithOne_KeepsItToo()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), "Public Sub Main()\r\nx = VBA.Left$(a, 1)\r\nEnd Sub\r\n");

        var access = Descendants(parse.SyntaxTree!).OfType<MemberAccessExpressionNode>().Single();
        Assert.AreEqual("Left", access.Member.IdentifierName);
        Assert.AreEqual("$", access.Member.TypeHint);
    }

    [TestMethod]
    public void ACallStatementOfAHintedName_KeepsItToo()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), "Public Sub Main()\r\nFoo$ 1\r\nEnd Sub\r\n");

        var call = Descendants(parse.SyntaxTree!).OfType<CallStatementNode>().Single();
        Assert.AreEqual("$", Assert.IsInstanceOfType<SimpleNameExpressionNode>(call.Callee).TypeHint);
    }
}
