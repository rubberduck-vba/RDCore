using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// A line continuation (<strong>MS-VBAL §3.2.2</strong>) is no code, and no node of the tree: it is trivia of the module, where it is written, and the keyword
/// of two words it is written inside of, when it is.
/// </summary>
[TestClass]
public sealed class LineContinuationTriviaTests
{
    private static LineContinuationTriviaNode[] ContinuationsIn(string body)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub S()\r\n{body}\r\nEnd Sub\r\n");
        Assert.IsEmpty(parse.SyntaxErrors, string.Join(" | ", parse.SyntaxErrors.Select(error => $"{error.Location.Range}: {error.Verbose}")));
        return [.. parse.Trivia.OfType<LineContinuationTriviaNode>()];
    }

    [TestMethod]
    public void AContinuationBetweenTwoTokens_IsWhereItIsWritten()
    {
        var continuation = Assert.ContainsSingle(ContinuationsIn("    x = 1 + _\r\n        2"));

        Assert.IsNull(continuation.Keyword);
        Assert.AreEqual(new SourceRange(new SourcePosition(1, 11), new SourcePosition(2, 8)), continuation.SourceLocation.Range);
    }

    [TestMethod]
    [DataRow("    If True Then\r\n    End _\r\n    If", "End If", 2, DisplayName = "End If")]
    [DataRow("    Exit _\r\nSub", "Exit Sub", 1, DisplayName = "Exit Sub")]
    [DataRow("    Do\r\n    Exit _\r\n  Do\r\n    Loop", "Exit Do", 2, DisplayName = "Exit Do")]
    public void AContinuationInsideAKeywordOfTwoWords_SaysWhichKeyword(string body, string keyword, int line)
    {
        var continuation = Assert.ContainsSingle(ContinuationsIn(body));

        Assert.AreEqual(keyword, continuation.Keyword);
        Assert.AreEqual(line, continuation.SourceLocation.Range.Start.Line);
        Assert.AreEqual(line + 1, continuation.SourceLocation.Range.End.Line);
    }

    [TestMethod]
    public void TwoContinuationsInsideAKeyword_AreTwo()
    {
        var continuations = ContinuationsIn("    If True Then\r\n    End _\r\n _\r\n    If");

        Assert.HasCount(2, continuations);
        Assert.IsTrue(continuations.All(continuation => continuation.Keyword == "End If"));
        Assert.AreEqual(2, continuations[0].SourceLocation.Range.Start.Line);
        Assert.AreEqual(3, continuations[1].SourceLocation.Range.Start.Line);
    }

    [TestMethod]
    public void AContinuationOfTheProcedureEnd_IsTrivia_OfTheModule()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), "Public Sub S()\r\nEnd _\r\nSub\r\n");
        Assert.IsEmpty(parse.SyntaxErrors);

        Assert.AreEqual("End Sub", Assert.ContainsSingle(parse.Trivia.OfType<LineContinuationTriviaNode>()).Keyword);
    }

    [TestMethod]
    [DataRow("    x = 1 ' a comment _\r\n    ' that goes on", DisplayName = "a comment that goes on")]
    [DataRow("    Rem a remark _\r\n    that goes on", DisplayName = "a Rem that goes on")]
    [DataRow("    x = 1 ' End _\r\n    If", DisplayName = "a keyword of two words in a comment")]
    [DataRow("    x = \"a _ b\"", DisplayName = "an underscore in a string")]
    [DataRow("    x = 1", DisplayName = "no continuation")]
    public void WhatIsNoContinuationOfTheCode_IsNone(string body)
        => Assert.IsEmpty(ContinuationsIn(body));

    [TestMethod]
    public void AContinuation_IsTriviaOnce_InAnExpressionAStatementCapturesAgain()
        => Assert.ContainsSingle(ContinuationsIn("    If x = _\r\n        1 Then y = 2"));
}
