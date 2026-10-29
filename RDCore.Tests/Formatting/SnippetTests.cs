using RDCore.SDK.Formatting;

namespace RDCore.Tests.Formatting;

/// <summary>
/// A snippet as <strong>LSP 3.17</strong> writes it is the text it is inserted as, and the places of its tab stops in that text.
/// </summary>
[TestClass]
public sealed class SnippetTests
{
    [TestMethod]
    public void TextWithNoTabStop_IsTheText()
    {
        var snippet = Snippet.Parse("Debug.Print x");

        Assert.AreEqual("Debug.Print x", snippet.Text);
        Assert.IsEmpty(snippet.TabStops);
    }

    [TestMethod]
    public void ATabStop_IsAPlace_AndAPlaceholder_IsItsText()
    {
        var snippet = Snippet.Parse("Dim ${1:x} As $2$0");

        Assert.AreEqual("Dim x As ", snippet.Text);
        CollectionAssert.AreEqual(new[] { new SnippetTabStop(1, 4, 1), new SnippetTabStop(2, 9, 0), new SnippetTabStop(0, 9, 0) }, snippet.TabStops.ToArray());
    }

    [TestMethod]
    public void ATabStopInSeveralPlaces_HasTheTextOfItsPlaceholder_InEach()
    {
        var snippet = Snippet.Parse("For ${1:i} = 1 To 10: Debug.Print $1: Next");

        Assert.AreEqual("For i = 1 To 10: Debug.Print i: Next", snippet.Text);
        CollectionAssert.AreEqual(new[] { new SnippetTabStop(1, 4, 1), new SnippetTabStop(1, 29, 1) }, snippet.TabStops.ToArray());
    }

    [TestMethod]
    public void AChoice_IsItsFirstOption()
    {
        var snippet = Snippet.Parse("Sub Foo(${1|ByVal,ByRef|} x)");

        Assert.AreEqual("Sub Foo(ByVal x)", snippet.Text);
        Assert.AreEqual(new SnippetTabStop(1, 8, 5), snippet.TabStops.Single());
    }

    [TestMethod]
    public void AVariable_IsItsDefault_OrNothing()
        => Assert.AreEqual("' in Module1: ", Snippet.Parse("' in ${TM_FILENAME_BASE:Module1}: $TM_SELECTED_TEXT").Text);

    [TestMethod]
    public void ABackslash_EscapesWhatWouldBeSyntax()
    {
        var snippet = Snippet.Parse(@"x = ""\$1 \} \\""");

        Assert.AreEqual(@"x = ""$1 } \""", snippet.Text);
        Assert.IsEmpty(snippet.TabStops);
    }

    [TestMethod]
    public void APlaceholderInAPlaceholder_IsATabStopInItsText()
    {
        var snippet = Snippet.Parse("${1:Dim ${2:x}} = 1");

        Assert.AreEqual("Dim x = 1", snippet.Text);
        CollectionAssert.AreEqual(new[] { new SnippetTabStop(1, 0, 5), new SnippetTabStop(2, 4, 1) }, snippet.TabStops.ToArray());
    }

    [TestMethod]
    [DataRow("$", "$")]
    [DataRow("${", "${")]
    [DataRow("a $ b", "a $ b")]
    [DataRow("${1|x", "${1|x")]
    public void WhatIsNoTabStop_IsTheTextItIs(string written, string text)
        => Assert.AreEqual(text, Snippet.Parse(written).Text);

    [TestMethod]
    public void ASnippetOfSeveralLines_SaysSo()
    {
        Assert.IsTrue(Snippet.Parse("If ${1:x} Then\r\n\t$0\r\nEnd If").IsMultiline);
        Assert.IsFalse(Snippet.Parse("x = ${1:1}").IsMultiline);
    }
}
