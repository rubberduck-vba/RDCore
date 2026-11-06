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

    // the most steps a parse takes for each character of its snippet (Snippet.StepsOfLastParse): each is read, merged, collected and written, a few times. A
    // parse that read a part again for each character after it would take thousands for each of these; a count of the work, unlike a clock, is the same on
    // any machine.
    private const int StepsPerCharacter = 16;

    private static void TakesSteps(long most, string why)
        => Assert.IsLessThanOrEqualTo(most, Snippet.StepsOfLastParse, $"{why}: {Snippet.StepsOfLastParse} steps, of at most {most}");

    [TestMethod]
    public void PlaceholdersThatAreNotClosed_AreReadInOnePass()
    {
        // as many as a snippet has open in one another: each is the text it is; more are no snippet to insert, and are not read past that.
        var snippet = string.Concat(Enumerable.Repeat("${1:", Snippet.MostNested));
        Assert.AreEqual(snippet, Snippet.Parse(snippet).Text);

        var unclosed = string.Concat(Enumerable.Repeat("${1:", 2000));
        Assert.IsFalse(Snippet.TryParse(unclosed, out _));
        TakesSteps((long)StepsPerCharacter * unclosed.Length, "a placeholder that is not closed is not read again from where it opened");
    }

    [TestMethod]
    public void PlaceholdersThatGrowAsTheyAreShownInOneAnother_AreNoSnippetToInsert()
    {
        // ${1:$2$2$2$2$2$2$2$2}${2:$3$3...}...${9:x}: each level shows the next eight times, which is 8^8 places of tab stop 9.
        var snippet = string.Concat(Enumerable.Range(1, 8).Select(level => $"${{{level}:{string.Concat(Enumerable.Repeat($"${level + 1}", 8))}}}")) + "${9:x}";
        Assert.IsFalse(Snippet.TryParse(snippet, out var parsed));

        TakesSteps((long)StepsPerCharacter * Snippet.MostText, "the writing stops once it is larger than a snippet to insert");
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

    // ---- inputs no snippet to insert is ----

    private static void ReadInTime(string snippet, bool expected, string why, int stepsPerCharacter = StepsPerCharacter)
    {
        Assert.AreEqual(expected, Snippet.TryParse(snippet, out _), why);
        TakesSteps((long)stepsPerCharacter * snippet.Length, why);
    }

    [TestMethod]
    public void PlaceholdersClosedInOneAnother_DeeperThanASnippetHasThem_AreNoSnippet_AndNothingOverflows()
    {
        ReadInTime(string.Concat(Enumerable.Repeat("${1:", 10_000)) + new string('}', 10_000), false, "10,000 placeholders in one another");
        ReadInTime(string.Concat(Enumerable.Repeat("${A:", 10_000)) + new string('}', 10_000), false, "10,000 variable defaults in one another");
    }

    [TestMethod]
    public void ASnippetLongerThanOneToInsert_IsNotRead()
        => ReadInTime(new string('x', Snippet.MostText + 1), false, "the input is longer than a snippet's text can be");

    [TestMethod]
    public void UnclosedPlaceholders_AndUnfinishedTransforms_AreReadInTimeTheirLengthTakes()
    {
        // within the length a snippet can have: each of them is text, read once.
        ReadInTime(string.Concat(Enumerable.Repeat("${1:x", 13_000)), false, "more open than a snippet has in one another");
        // a transform that is not finished is read through to the most characters one has, past each of its '$': 1,024 steps for every eight characters.
        ReadInTime(string.Concat(Enumerable.Repeat("x ${1/a/", 8_000)), true, "transforms that are not finished", stepsPerCharacter: 1_024 / 8 + StepsPerCharacter);
        Assert.AreEqual("x ${1/a/x ${1/a/", Snippet.Parse("x ${1/a/x ${1/a/").Text);
    }

    [TestMethod]
    public void PlaceholdersShownInOneAnother_DeeperThanASnippetHasThem_AreNoSnippet_RatherThanEmpty()
    {
        // ${1:$2} ${2:$3} ... ${17:x}: written flat, shown seventeen deep.
        var snippet = string.Join(" ", Enumerable.Range(1, 16).Select(level => $"${{{level}:${level + 1}}}")) + " ${17:x}";

        Assert.IsFalse(Snippet.TryParse(snippet, out _), "past the depth a snippet has, no text is dropped: it is not one to insert");
        Assert.IsTrue(Snippet.TryParse("${1:$2} ${2:$3} ${3:x}", out var shallow));
        Assert.AreEqual("x x x", shallow.Text);
    }

    [TestMethod]
    public void ManyVariablesNotKnown_AreReadInTimeTheirNumberTakes()
        => ReadInTime(string.Join(" ", Enumerable.Range(1, 4_000).Select(index => $"$V{index}")), true, "each variable not known is a tab stop of its own");
}
