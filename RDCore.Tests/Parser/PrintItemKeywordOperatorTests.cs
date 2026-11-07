using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// An operator-identifier is a reserved identifier (<strong>MS-VBAL §3.3.5.2</strong>). After an item of a list to print, it is an operator, and with no
/// right operand it is a syntax error, as a symbol operator is. After a <c>.</c> or a <c>!</c>, it names a member, as any keyword does.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.6.1 Syntax Errors")]
public sealed class PrintItemKeywordOperatorTests
{
    private static ModuleParseResult Parse(string statement)
        => new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{statement}\r\nEnd Sub\r\n");

    [TestMethod]
    [DataRow("Debug.Print 1 Like")]
    [DataRow("Debug.Print 1 Is")]
    [DataRow("Debug.Print 1 Mod")]
    [DataRow("Debug.Print 1 And")]
    [DataRow("Debug.Print 1 Or")]
    [DataRow("Debug.Print 1 Xor")]
    [DataRow("Debug.Print 1 Eqv")]
    [DataRow("Debug.Print 1 Imp")]
    [DataRow("Debug.Print x; Mod", DisplayName = "an operator alone after a separator")]
    [DataRow("Debug.Print 1; 2 Like", DisplayName = "after the second item")]
    [DataRow("Debug.Print Spc(2) Mod", DisplayName = "after a Spc clause")]
    [DataRow("Debug.Print Tab(2) And", DisplayName = "after a Tab clause")]
    [DataRow("Print #1, 1 Like", DisplayName = "after an item of a Print")]
    [DataRow("Print #1, x; Or", DisplayName = "alone after a separator of a Print")]
    [DataRow("Print #1, Tab(2) Xor", DisplayName = "after a Tab clause of a Print")]
    [DataRow("Write #1, 1 And", DisplayName = "after an item of a Write")]
    [DataRow("Write #1, 1, 2 Imp", DisplayName = "after the second item of a Write")]
    public void AKeywordOperatorAfterAnItemToPrint_IsOneSyntaxError(string statement)
    {
        var parse = Parse(statement);

        var error = Assert.ContainsSingle(parse.SyntaxErrors, Said(parse));
        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId);
        Assert.AreEqual(1, error.Location.Range.Start.Line);
    }

    // an operator keyword names a member, and the item that names it is printed.
    [TestMethod]
    [DataRow("Debug.Print x.Mod")]
    [DataRow("Debug.Print x.And; d!Xor")]
    [DataRow("Print #1, x.Like")]
    [DataRow("Write #1, x.Is, x.Or")]
    [DataRow("Debug.Print 1 Mod 2; 3 And 4")]
    public void AnOperatorKeyword_ThatNamesAMember_IsAnItemToPrint(string statement)
    {
        var parse = Parse(statement);

        Assert.IsEmpty(parse.SyntaxErrors, Said(parse));
        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        Assert.IsNotInstanceOfType<UnbuiltStatementTriviaNode>(Assert.ContainsSingle(member.Children.OfType<StatementNode>()));
    }

    // an operator keyword is no name of a variable: a statement that uses it as one is one syntax error, at the keyword.
    [TestMethod]
    [DataRow("Mod = 1", 0)]
    [DataRow("And = 1", 0)]
    [DataRow("Dim Mod As Long", 4)]
    [DataRow("Dim Like", 4)]
    public void AnOperatorKeyword_AsTheNameOfAVariable_IsOneSyntaxError_AtTheKeyword(string statement, int column)
    {
        var parse = Parse(statement);

        var error = Assert.ContainsSingle(parse.SyntaxErrors, Said(parse));
        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId);
        Assert.AreEqual(new SourcePosition(1, column), error.Location.Range.Start);
    }

    // the name of a second variable of the statement too.
    [TestMethod]
    public void AnOperatorKeyword_AsTheNameOfASecondVariable_IsOneSyntaxError()
    {
        var parse = Parse("Dim a As Long, Or As Long");

        Assert.AreEqual(1, Assert.ContainsSingle(parse.SyntaxErrors, Said(parse)).Location.Range.Start.Line);
    }

    // an error inside a statement stays in it: the statement is kept where it is, and the next one is read on its own.
    [TestMethod]
    [DataRow("Mod = 1", DisplayName = "an operator keyword where a statement begins")]
    [DataRow("Dim Mod As Long", DisplayName = "an operator keyword as the name of a variable")]
    [DataRow("Dim For As Long", DisplayName = "a statement keyword as the name of a variable")]
    [DataRow("Dim 1 As Long", DisplayName = "a number as the name of a variable")]
    public void AnErrorInsideAStatement_KeepsTheStatement_AndTheNextIsReadOnItsOwn(string statement)
    {
        var parse = Parse($"{statement}\r\nb = 2");

        Assert.AreEqual(1, Assert.ContainsSingle(parse.SyntaxErrors, Said(parse)).Location.Range.Start.Line);
        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        var statements = member.Children.OfType<StatementNode>().ToArray();
        Assert.IsTrue(statements.Any(node => node.SourceLocation.Range.Start.Line == 1), "the statement with the error is kept");
        Assert.IsTrue(statements.OfType<AssignmentStatementNode>().Any(node => node.SourceLocation.Range.Start.Line == 2), "the next statement is read on its own");
    }

    // two statements of one line, each with its error: each is reported.
    [TestMethod]
    public void TwoStatementsOfALine_EachWithAnOperatorKeyword_AreTwoSyntaxErrors()
    {
        var parse = Parse("Mod = 1: And = 2");

        Assert.HasCount(2, parse.SyntaxErrors, Said(parse));
    }

    // a parameter's name is a name, as a variable's is (MS-VBAL 5.3.1.5): a reserved identifier there is a syntax error.
    [TestMethod]
    [DataRow("Public Sub S(ByVal Mod As Long)", DisplayName = "an operator keyword, ByVal")]
    [DataRow("Public Sub S(And)", DisplayName = "an operator keyword alone")]
    [DataRow("Public Sub S(Dim As Long)", DisplayName = "a statement keyword")]
    [DataRow("Public Sub S(ByVal For As Long)", DisplayName = "a statement keyword, ByVal")]
    [DataRow("Public Sub S(ByVal Next As Long)", DisplayName = "the statement keyword Next, ByVal")]
    [DataRow("Public Declare PtrSafe Sub S Lib \"x\" (ByVal Like As Long)", DisplayName = "an operator keyword, in a Declare")]
    public void AReservedIdentifierAsTheNameOfAParameter_IsASyntaxError(string header)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), header.Contains("Declare") ? $"{header}\r\n" : $"{header}\r\nEnd Sub\r\n");

        Assert.IsTrue(parse.SyntaxErrors.Any(error => error.ErrorId == (int)VBCompileErrorId.SyntaxError && error.Location.Range.Start.Line == 0), Said(parse));
    }

    [TestMethod]
    [DataRow("Public Sub S(ByVal Name As String)")]
    [DataRow("Public Sub S(ByVal Text As String, Optional ByVal Value$ = \"\")")]
    [DataRow("Public Sub S(ByRef Items() As Variant, ParamArray Args() As Variant)")]
    [DataRow("Public Sub S([Mod] As Long)")]
    public void AParameterWithAName_IsValid(string header)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"{header}\r\nEnd Sub\r\n");

        Assert.IsEmpty(parse.SyntaxErrors, Said(parse));
    }

    private static string Said(ModuleParseResult parse)
        => string.Join(" | ", parse.SyntaxErrors.Select(error => $"{error.Location.Range}: {error.Verbose}"));
}
