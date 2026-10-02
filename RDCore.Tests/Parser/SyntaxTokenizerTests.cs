using RDCore.Parsing;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// The tokens of a text, by what their spelling and their place in a line say they are: the lexical half of what a listing is highlighted by.
/// </summary>
[TestClass]
public sealed class SyntaxTokenizerTests
{
    private static readonly ISyntaxTokenizer Sut = new SyntaxTokenizer();

    private static string Described(string text)
        => string.Join(" ", Sut.Tokenize(text).Select(token => $"{token.Kind}@{token.Line}:{token.Character}+{token.Length}"));

    private static string Kinds(string text) => string.Join(" ", Sut.Tokenize(text).Select(token => token.Kind));

    [TestMethod]
    public void ADeclaration_IsKeywordsAndNames_AndTheTypeAfterAsIsATypeName()
        => Assert.AreEqual("Keyword Identifier Keyword TypeName", Kinds("Dim x As Long"));

    [TestMethod]
    public void TheTokensAreWhereTheyAre_WhitespaceIsNotOne()
        => Assert.AreEqual("Keyword@0:0+3 Identifier@0:4+1 Operator@0:6+1 Number@0:8+1", Described("Dim x = 1"));

    [TestMethod]
    public void ALiteral_IsAStringOrANumber()
        => Assert.AreEqual("Identifier Operator String Operator Number Operator Number", Kinds("x = \"a\" & 1 + &HFF"));

    [TestMethod]
    public void ACommentIsOneToken_FromItsQuoteToTheEndOfTheLine()
        => Assert.AreEqual("Keyword@0:0+3 Identifier@0:4+1 Comment@0:6+13", Described("Dim x ' Dim y = \"z\""));

    [TestMethod]
    public void RemAtTheStartOfAStatement_IsAComment_AndRemElsewhereIsNot()
    {
        Assert.AreEqual("Comment", Kinds("Rem this is a comment"));
        Assert.AreEqual("Number Comment", Kinds("100 Rem this is a comment"));
        Assert.AreEqual("Identifier Operator Comment", Kinds("x: Rem y"));
        Assert.AreEqual("Identifier Operator Keyword", Kinds("x = Rem"));
    }

    [TestMethod]
    public void ACommentThatIsContinued_IsAComment_OnEachLineOfIt()
        => Assert.AreEqual("Comment@0:0+7 Comment@1:1+3 Identifier@2:0+1", Described("' one _\r\n two\r\nx"));

    [TestMethod]
    public void AWordOfTheLanguageAfterAMemberAccess_IsTheNameOfAMember()
    {
        Assert.AreEqual("Identifier Operator Identifier", Kinds("x.Name"));
        Assert.AreEqual("Identifier Operator Identifier", Kinds("d!Name"));
        Assert.AreEqual("Keyword", Kinds("Name"));
    }

    [TestMethod]
    public void ANameThatFollowsAsNew_IsATypeName_AndSoIsAQualifiedOne()
    {
        Assert.AreEqual("Keyword Identifier Keyword Keyword TypeName", Kinds("Dim x As New Collection"));
        Assert.AreEqual("Keyword Identifier Keyword TypeName Operator TypeName", Kinds("Dim x As Excel.Range"));
        Assert.AreEqual("Keyword TypeName", Kinds("Implements IFoo"));
    }

    [TestMethod]
    public void ALineNumber_IsANumber_AndWhatFollowsItStartsAStatement()
        => Assert.AreEqual("Number Keyword String", Kinds("100 Print \"hello\""));

    [TestMethod]
    public void TheLinesAreTheLinesOfTheText()
        => Assert.AreEqual("Identifier@0:0+1 Operator@0:2+1 Number@0:4+1 Identifier@1:0+1 Operator@1:2+1 Number@1:4+1", Described("x = 1\r\ny = 2"));

    [TestMethod]
    public void TextThatIsNotAProgram_IsTokenizedAllTheSame()
        => Assert.IsNotEmpty(Sut.Tokenize("If Then ((( \"unterminated"));
}
