using RDCore.LanguageServer.Workspace;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <strong>LSP 3.17</strong> <c>textDocument/didChange</c>: an edit is a range of the text and what replaces it, and the position of a range is a line and a character.
/// </summary>
[TestClass]
public sealed class DocumentTextTests
{
    private static Range R(int startLine, int startCharacter, int endLine, int endCharacter) => new(new Position(startLine, startCharacter), new Position(endLine, endCharacter));

    [TestMethod]
    public void ARangeWithinALine_IsReplaced()
        => Assert.AreEqual("Hello, there", DocumentText.Apply("Hello, world", R(0, 7, 0, 12), "there"));

    [TestMethod]
    public void AnEmptyRange_IsAnInsertion()
        => Assert.AreEqual("Hello, big world", DocumentText.Apply("Hello, world", R(0, 7, 0, 7), "big "));

    [TestMethod]
    public void AnEmptyReplacement_IsADeletion()
        => Assert.AreEqual("Hello world", DocumentText.Apply("Hello, world", R(0, 5, 0, 6), ""));

    [TestMethod]
    [DataRow("a\r\nb\r\nc")]
    [DataRow("a\nb\nc")]
    [DataRow("a\rb\rc")]
    public void ARangeOverSeveralLines_IsReplaced_WhateverTheLineEndingsAre(string text)
    {
        var edited = DocumentText.Apply(text, R(0, 1, 2, 0), "-");

        Assert.AreEqual("a-c", edited);
    }

    [TestMethod]
    public void TheLineEndingsThatAreNotEdited_AreLeftAsTheyWere()
        => Assert.AreEqual("one\r\nTWO\ntwo and a half\rthree", DocumentText.Apply("one\r\ntwo\ntwo and a half\rthree", R(1, 0, 1, 3), "TWO"));

    [TestMethod]
    public void ACharacterPastTheEndOfItsLine_IsTheEndOfTheLine()
        => Assert.AreEqual("abcX\r\ndef", DocumentText.Apply("abc\r\ndef", R(0, 99, 0, 99), "X"));

    [TestMethod]
    public void ALinePastTheEndOfTheDocument_IsTheEndOfTheDocument()
        => Assert.AreEqual("abc\r\ndefX", DocumentText.Apply("abc\r\ndef", R(9, 0, 9, 0), "X"));

    [TestMethod]
    public void ATextThatEndsWithALineEnding_HasAnEmptyLastLine()
        => Assert.AreEqual("abc\r\nX", DocumentText.Apply("abc\r\n", R(1, 0, 1, 0), "X"));

    [TestMethod]
    public void AnEditOfAnEmptyDocument_InsertsTheText()
        => Assert.AreEqual("Sub Foo()", DocumentText.Apply("", R(0, 0, 0, 0), "Sub Foo()"));

    [TestMethod]
    public void ARangeThatEndsBeforeItStarts_IsAnInsertionAtItsStart()
        => Assert.AreEqual("abXcd", DocumentText.Apply("abcd", R(0, 2, 0, 1), "X"));

    [TestMethod]
    public void TheOffsetOfAPosition_CountsTheLineEndingsOfTheDocument()
    {
        Assert.AreEqual(4, DocumentText.OffsetOf("ab\r\ncd", 1, 0));
        Assert.AreEqual(3, DocumentText.OffsetOf("ab\ncd", 1, 0));
    }
}
