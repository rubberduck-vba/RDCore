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
        CollectionAssert.AreEqual(new[] { new SnippetTabStop(1, 0, 5), new SnippetTabStop(2, 4, 1, Parent: 0) }, snippet.TabStops.ToArray(), "the inner place is in the outer one");
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

    // ---- mirrors, invalid syntax, transforms and variables, as LSP 3.17 has them ----

    [TestMethod]
    public void ATabStopInAPlaceholder_ShowsThePlaceholderGivenElsewhere()
    {
        var snippet = Snippet.Parse("${1:a $2 b} ${2:xyz}");

        Assert.AreEqual("a xyz b xyz", snippet.Text);
        CollectionAssert.AreEqual(new[] { new SnippetTabStop(1, 0, 7), new SnippetTabStop(2, 2, 3, Parent: 0), new SnippetTabStop(2, 8, 3) }, snippet.TabStops.ToArray());
    }

    [TestMethod]
    public void APlaceOfATabStopBeforeItsPlaceholder_ShowsItToo()
        => Assert.AreEqual("abab", Snippet.Parse("$1${1:ab}").Text);

    [TestMethod]
    public void EachPlaceOfATabStop_HasTheTabStopsItsPlaceholderHolds()
    {
        var snippet = Snippet.Parse("${1:Dim ${2:x}} = $1");

        Assert.AreEqual("Dim x = Dim x", snippet.Text);
        CollectionAssert.AreEqual(new[] { 4, 12 }, snippet.TabStops.Where(stop => stop.Index == 2).Select(stop => stop.Start).ToArray());
    }

    [TestMethod]
    public void PlaceholdersThatShowEachOther_AreShownToADepth_AndTheSnippetIsRead()
        => StringAssert.StartsWith(Snippet.Parse("${1:a $2} ${2:b $1}").Text, "a b");

    [TestMethod]
    [DataRow("${1:foo", "${1:foo")]
    [DataRow("x ${1:a ${2:b} c", "x ${1:a b c")]
    [DataRow("${1|a|b|}", "${1|a|b|}")]
    [DataRow("${1|a,b", "${1|a,b")]
    [DataRow("${1/(.*)/x", "${1/(.*)/x")]
    public void WhatDoesNotParse_IsText(string written, string text)
        => Assert.AreEqual(text, Snippet.Parse(written).Text);

    [TestMethod]
    public void AnUnclosedPlaceholder_IsNoTabStop()
        => Assert.IsEmpty(Snippet.Parse("${1:foo").TabStops);

    [TestMethod]
    public void AVariablesTransform_IsNotMade_AndMakesNoTabStop()
    {
        Assert.AreEqual(string.Empty, Snippet.Parse("${TM_SELECTED_TEXT/(.*)/$1/}").Text);
        Assert.IsEmpty(Snippet.Parse("${TM_SELECTED_TEXT/(.*)/$1/}").TabStops);

        var inPlaceholder = Snippet.Parse("${1:x ${TM_SELECTED_TEXT/(.*)/${1:/upcase}/g} y}");
        Assert.AreEqual("x  y", inPlaceholder.Text, "the transform's braces are its own: the placeholder ends at its own");
        Assert.AreEqual(new SnippetTabStop(1, 0, 4), inPlaceholder.TabStops.Single());
    }

    [TestMethod]
    public void ATabStopsTransform_IsNotMade_TheTabStopIs()
        => Assert.AreEqual(new SnippetTabStop(2, 2, 0), Snippet.Parse("a ${2/(.*)/[$1]/}").TabStops.Single());

    [TestMethod]
    public void AVariableThatIsNotKnown_IsItsName_ATabStopAfterTheNumberedOnes()
    {
        var snippet = Snippet.Parse("$UNKNOWN_VAR x $1");

        Assert.AreEqual("UNKNOWN_VAR x ", snippet.Text);
        CollectionAssert.AreEqual(new[] { new SnippetTabStop(2, 0, 11), new SnippetTabStop(1, 14, 0) }, snippet.TabStops.ToArray());
    }

    [TestMethod]
    public void AVariableTheCallerKnows_IsItsValue()
        => Assert.AreEqual("' Module1: x", Snippet.Parse("' $TM_FILENAME_BASE: ${NAME:y}", name => name switch { "TM_FILENAME_BASE" => "Module1", "NAME" => "x", _ => null }).Text);

    // ---- the time and the size a snippet can take ----

    [TestMethod]
    public void PlaceholdersThatAreNotClosed_AreReadInOnePass()
    {
        var snippet = string.Concat(Enumerable.Repeat("${1:", 2000));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var parsed = Snippet.Parse(snippet);

        Assert.IsLessThan(1000, watch.ElapsedMilliseconds, "a placeholder that is not closed is not read again from where it opened");
        Assert.AreEqual(snippet, parsed.Text);
    }

    [TestMethod]
    public void PlaceholdersThatGrowAsTheyAreShownInOneAnother_AreNoSnippetToInsert()
    {
        // ${1:$2$2$2$2$2$2$2$2}${2:$3$3...}...${9:x}: each level shows the next eight times, which is 8^8 places of tab stop 9.
        var snippet = string.Concat(Enumerable.Range(1, 8).Select(level => $"${{{level}:{string.Concat(Enumerable.Repeat($"${level + 1}", 8))}}}")) + "${9:x}";
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.IsFalse(Snippet.TryParse(snippet, out var parsed));

        Assert.IsLessThan(1000, watch.ElapsedMilliseconds, "the writing stops once it is larger than a snippet to insert");
        Assert.IsNull(parsed);
        Assert.ThrowsExactly<ArgumentException>(() => Snippet.Parse(snippet));
    }

    [TestMethod]
    public void AnEmptyPlaceholder_GivesWayToOneThatHasText()
        => Assert.AreEqual("x x", Snippet.Parse("${1:} ${1:x}").Text);

    [TestMethod]
    public void ATabStopNumberTooLargeForOne_IsText()
    {
        var snippet = Snippet.Parse("$99999999999 $FOO");

        Assert.AreEqual("$99999999999 FOO", snippet.Text);
        Assert.AreEqual(new SnippetTabStop(1, 13, 3), snippet.TabStops.Single(), "the variable is the first tab stop after the numbered ones, of which there are none");
    }
}
