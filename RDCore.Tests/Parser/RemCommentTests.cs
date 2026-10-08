using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;

namespace RDCore.Tests.Parser;

/// <summary>
/// A <c>Rem</c> comment is a comment wherever a statement could begin (<strong>MS-VBAL §3.3.1</strong>): it is trivia, and so there is no statement of it
/// to run. <c>Rem</c> is a reserved identifier - never the name of a procedure, which is what <c>Rem hello</c> would be a call of.
/// </summary>
/// <remarks>
/// It was a comment at the start of a line and after a colon, and a call of a procedure named <c>Rem</c> after a line number or a label, when what
/// followed was no more than one word: the only reading of <c>10 REM hello</c> that the grammar did not need a comment for.
/// </remarks>
[TestClass]
public sealed class RemCommentTests
{
    private static ModuleParseResult Parse(string body)
        => new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{body}\r\nEnd Sub\r\n");

    // what the procedure is made of, without the line numbers and the labels that only say where a statement is.
    private static string[] Statements(string body)
    {
        var parse = Parse(body);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        return [.. parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children
            .Select(node => node.GetType().Name)
            .Where(name => name is not ("LineNumberNode" or "LineLabelNode"))];
    }

    [TestMethod]
    [DataRow("Rem hello")]
    [DataRow("REM")]
    [DataRow("rem hello world")]
    [DataRow("10 REM hello")]
    [DataRow("10 Rem hello")]
    [DataRow("10 rem hello")]
    [DataRow("10 REM")]
    [DataRow("10 REM hello world")]
    [DataRow("A: REM hello")]
    [DataRow("A: Rem hello world")]
    public void ARemComment_IsNoStatement_WhateverComesBeforeItOnTheLine_AndWhateverFollowsIt(string body)
        => CollectionAssert.AreEqual(Array.Empty<string>(), Statements(body));

    [TestMethod]
    public void ARemComment_RunsToTheEndOfTheLine_ColonIncluded()
    {
        CollectionAssert.AreEqual(Array.Empty<string>(), Statements("10 REM : Debug.Print 1"));
        CollectionAssert.AreEqual(Array.Empty<string>(), Statements("Rem hello world : Debug.Print 1"));
    }

    [TestMethod]
    public void ARemCommentAfterAStatementAndAColon_LeavesTheStatement()
    {
        CollectionAssert.AreEqual(new[] { "DebugPrintStatementNode" }, Statements("Debug.Print 1 : REM x"));
        CollectionAssert.AreEqual(new[] { "DebugPrintStatementNode" }, Statements("10 Debug.Print 1 : Rem hello world"));
    }

    [TestMethod]
    public void ARemCommentAfterThen_IsTheEndOfTheLineOfABlockIf_AsAnApostropheIs()
    {
        var withRem = Statements("If True Then REM nothing\r\nDebug.Print 1\r\nEnd If");
        var withApostrophe = Statements("If True Then ' nothing\r\nDebug.Print 1\r\nEnd If");

        CollectionAssert.AreEqual(new[] { "IfBlockStatementNode" }, withRem);
        CollectionAssert.AreEqual(withApostrophe, withRem);
    }

    [TestMethod]
    public void ARemCommentInsideABlock_IsNoStatementOfIt()
    {
        var parse = Parse("If True Then\r\n20 REM inside\r\n30 Debug.Print 1\r\nEnd If");

        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
    }

    [TestMethod]
    public void RemIsStillAMemberName_AfterADot()
    {
        // reserved words are members of an object all the same; it is only a statement that cannot begin with one.
        var parse = Parse("x.Rem = 1");

        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
    }

    [TestMethod]
    public void RemIsNoProcedureName_TheLineAfterACallIsNotASecondCall()
        => CollectionAssert.AreEqual(new[] { "CallStatementNode" }, Statements("Call Foo\r\n10 Rem Foo"));
}
