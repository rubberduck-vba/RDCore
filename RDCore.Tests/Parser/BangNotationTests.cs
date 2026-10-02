using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;

namespace RDCore.Tests.Parser;

/// <summary>
/// The bang notation, <c>owner!Member</c>, and the type hint <c>!</c> are the same character: what follows it decides. A name that begins immediately after
/// the bang is a member, whatever kind of token the name is - a keyword is a legal name (<strong>MS-VBAL §3.3.5.2</strong>: only the reserved identifiers are not).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.14 Dictionary Access Expressions")]
public sealed class BangNotationTests
{
    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node)
        => node.Children.SelectMany(child => Descendants(child).Prepend(child));

    private static ModuleParseResult Parse(string body)
    {
        var result = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Run(d As Object)\r\n{body}\r\nEnd Sub\r\n");
        Assert.IsTrue(result.IsSuccess, string.Join("; ", result.SyntaxErrors.Select(error => error.Verbose)));
        return result;
    }

    [TestMethod]
    [DataRow("Foo")]
    [DataRow("Name")]
    [DataRow("Date")]
    [DataRow("Open")]
    [DataRow("Print")]
    [DataRow("Len")]
    [DataRow("String")]
    public void AMemberAfterTheBang_IsTheMember_WhateverKindOfTokenItIs(string member)
    {
        foreach (var statement in new[] { $"Debug.Print d!{member}", $"x = d!{member}", $"x = (d!{member})", $"Foo d!{member}" })
        {
            var access = Descendants(Parse(statement).SyntaxTree!).OfType<DictionaryAccessExpressionNode>().SingleOrDefault();

            Assert.IsNotNull(access, statement);
            Assert.AreEqual("d", ((SimpleNameExpressionNode)access.Owner!).IdentifierName, statement);
            Assert.AreEqual(member, access.Member.IdentifierName, statement);
        }
    }

    [TestMethod]
    public void ABangThatIsFollowedByWhitespace_IsStillATypeHint()
    {
        var descendants = Descendants(Parse("Debug.Print d! Name").SyntaxTree!).ToList();

        Assert.IsEmpty(descendants.OfType<DictionaryAccessExpressionNode>());
    }

    [TestMethod]
    public void ATypedIdentifier_IsStillATypedIdentifier()
    {
        var result = Parse("Dim a!\r\na! = 1.5\r\nDebug.Print a! + 1");

        Assert.IsEmpty(Descendants(result.SyntaxTree!).OfType<DictionaryAccessExpressionNode>());
    }
}
