using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.Tests.Parser;

/// <summary>
/// A name written with parentheses is an index expression, however the parentheses are written, and its argument list holds what is between them: empty
/// parentheses are one missing argument (<strong>MS-VBAL §5.6.13.1</strong>). A name written without them is a simple name, with no argument list, and a
/// call statement's blanks after its name begin its argument list only where an argument follows them.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.13.1 Argument lists")]
public sealed class ArgumentListShapeTests
{
    private static MemberDeclarationNode Procedure(string statement)
    {
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), $"Sub S()\r\n{statement}\r\nEnd Sub\r\n");

        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        return parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
    }

    private static ExpressionNode ValueOf(string expression)
        => Procedure($"x = {expression}").Children.OfType<AssignmentStatementNode>().Single().Value;

    [TestMethod]
    [DataRow("F()", 1, DisplayName = "empty parentheses")]
    [DataRow("F( )", 1, DisplayName = "a blank between the parentheses")]
    [DataRow("F ()", 1, DisplayName = "a blank before the parentheses")]
    [DataRow("F _\r\n()", 1, DisplayName = "a line continuation before the parentheses")]
    [DataRow("F(, )", 2, DisplayName = "two missing arguments")]
    public void ANameWithParentheses_IsAnIndexExpression_OfTheMissingArgumentsWrittenInThem(string expression, int missing)
    {
        var index = Assert.IsInstanceOfType<IndexExpressionNode>(ValueOf(expression));

        Assert.AreEqual("F", Assert.IsInstanceOfType<SimpleNameExpressionNode>(index.Callee).IdentifierName);
        Assert.HasCount(missing, index.Arguments);
        Assert.IsTrue(
            index.Arguments.All(argument => argument is MissingArgumentNode), string.Join(", ", index.Arguments.Select(argument => argument.GetType().Name)));
    }

    [TestMethod]
    public void ANameWithoutParentheses_IsASimpleName()
        => Assert.AreEqual("F", Assert.IsInstanceOfType<SimpleNameExpressionNode>(ValueOf("F")).IdentifierName);

    [TestMethod]
    [DataRow("Foo", DisplayName = "the name alone")]
    [DataRow("Foo ", DisplayName = "a blank after the name")]
    [DataRow("Foo   ", DisplayName = "blanks after the name")]
    [DataRow("Foo  ' a comment", DisplayName = "blanks, then a comment")]
    public void ACallStatement_WithNothingAfterItsName_HasNoArgument(string statement)
    {
        var call = Procedure(statement).Children.OfType<CallStatementNode>().Single();

        Assert.AreEqual("Foo", Assert.IsInstanceOfType<SimpleNameExpressionNode>(call.Callee).IdentifierName);
        Assert.IsEmpty(call.Arguments, string.Join(", ", call.Arguments.Select(argument => argument.GetType().Name)));
    }

    [TestMethod]
    [DataRow("Foo 1 ", DisplayName = "a blank after the argument")]
    [DataRow("Foo 1  ' a comment", DisplayName = "blanks and a comment after the argument")]
    public void ACallStatement_WithBlanksAfterItsArgument_HasThatArgumentOnly(string statement)
    {
        var call = Procedure(statement).Children.OfType<CallStatementNode>().Single();

        Assert.IsNotInstanceOfType<MissingArgumentNode>(call.Arguments.Single());
    }

    [TestMethod]
    [DataRow("If True Then Foo Else Bar", DisplayName = "a single-line If")]
    [DataRow("If True Then Foo  Else Bar  ' a comment", DisplayName = "a single-line If, with blanks and a comment")]
    [DataRow("x = 1: Foo : Bar", DisplayName = "statements on a line")]
    public void ACallStatement_BeforeAnElseOrAColon_HasNoArgument(string statement)
    {
        var calls = Procedure(statement).Descendants().OfType<CallStatementNode>().ToList();

        Assert.HasCount(2, calls);
        Assert.IsTrue(
            calls.All(call => call.Arguments.IsEmpty), string.Join(", ", calls.SelectMany(call => call.Arguments).Select(argument => argument.GetType().Name)));
    }

    [TestMethod]
    [DataRow("Foo , 2", 2, DisplayName = "a missing first argument")]
    [DataRow("Foo _\r\n    1", 1, DisplayName = "an argument on the next line")]
    public void ACallStatement_WithAnArgumentAfterItsBlanks_HasItsArgumentList(string statement, int count)
        => Assert.HasCount(count, Procedure(statement).Children.OfType<CallStatementNode>().Single().Arguments);
}
