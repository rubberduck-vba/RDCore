using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Parser;

/// <summary>
/// The data value (MS-VBAL 2.1) a <c>&lt;STRING&gt;</c> token (MS-VBAL 3.3.4) carries into its node.
/// </summary>
/// <remarks>
/// The listener sliced the delimiters off the token's source text and stopped there, so a doubled
/// double-quote — the only escape the token grammar has — survived into the value as two characters. Every
/// consumer downstream then printed, concatenated and compared the un-collapsed text.
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL §3.3.4 String Tokens")]
public sealed class StringLiteralTests
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

    private static string? ValueOf(string expression)
        => ((VBStringValue)Literal(expression).StaticValue!).Value;

    [TestMethod]
    public void StringLiteral_CarriesTheTextBetweenItsDelimiters()
        => Assert.AreEqual("hello", ValueOf("\"hello\""));

    [TestMethod]
    public void EmptyStringLiteral_CarriesTheZeroLengthString()
        => Assert.AreEqual(string.Empty, ValueOf("\"\""));

    [TestMethod]
    // MS-VBAL 3.3.4: "a sequence of two <double-quote> characters represents a single occurrence of the
    // character U+0022 within the data value".
    [DataRow("\"say \"\"hi\"\"\"", "say \"hi\"")]
    [DataRow("\"\"\"\"", "\"")]
    [DataRow("\"\"\"leading\"", "\"leading")]
    [DataRow("\"trailing\"\"\"", "trailing\"")]
    [DataRow("\"\"\"\"\"\"", "\"\"")]
    public void DoubledDoubleQuote_CollapsesToOne(string literal, string expected)
        => Assert.AreEqual(expected, ValueOf(literal));
}
