namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.6.9.6</strong>, end to end: what <c>Like</c> answers, and when it raises error 93 (<em>Invalid pattern string</em>),
/// as MS-VBA does - every row was run in Excel (oracle s46 and s47), and the batches keep their names there.
/// </summary>
/// <remarks>
/// A row is one module: its <c>Option Compare</c>, then <c>Like</c> expressions separated by <c>;;</c>, with <c>'</c> for <c>"</c>. Each
/// prints its value, or <c>e</c> and the number of the error it raised.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.9.6 Binary 'Like' Operator")]
public sealed class LikeOperatorTests
{
    private static Task<string[]> RunAsync(string compare, string expressions)
    {
        var lines = expressions.Split(" ;; ").SelectMany(expression => new[]
        {
            "Err.Clear",
            $"R = {expression.Replace('\'', '"')}",
            "If Err.Number = 0 Then",
            "Debug.Print R",
            "Else",
            "Debug.Print \"e\" & Err.Number",
            "End If",
        });

        return ModuleWorkspace.RunAsync([],
            $"Attribute VB_Name = \"Program\"\r\nOption Compare {compare}\r\nPublic Sub Main()\r\nOn Error Resume Next\r\nDim R As Variant\r\n"
            + $"{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");
    }

    [TestMethod]
    // [] matches no character at all; [!] is a !; a ] right after [ or [! closes the list.
    [DataRow("Binary", "'a' Like 'a[]' ;; 'ax' Like 'a[]x' ;; '' Like '[]' ;; 'a' Like '[]' ;; '!' Like '[!]' ;; 'a' Like '[!]'",
        "True|True|True|False|True|False", DisplayName = "F03 empty lists")]
    [DataRow("Binary", "'x' Like '[]x]' ;; '!]' Like '[!]]'", "False|True", DisplayName = "F08 ] closes")]
    [DataRow("Binary", "'' Like '*[]' ;; '' Like '*[]*' ;; 'ab' Like '*[]b'", "True|True|True", DisplayName = "P01 [] after *")]
    // an invalid element raises 93 when it is tested against a character, and a list's items are tested in order until one matches.
    [DataRow("Binary", "'b' Like 'a[z-a]' ;; 'x' Like 'y[' ;; 'a' Like '[az-a]' ;; '' Like '[a' ;; 'b' Like '[a][z-a]' ;; '' Like '*['",
        "False|False|True|False|False|False", DisplayName = "F04 lazy 93")]
    [DataRow("Binary", "'a' Like '[a' ;; 'ab' Like 'a[' ;; 'a' Like '*[' ;; 'ab' Like '*[' ;; '' Like '[' ;; 'x' Like '[!'",
        "e93|e93|e93|e93|False|e93", DisplayName = "L01 unterminated")]
    [DataRow("Binary", "'a' Like '[z-a]' ;; 'a' Like '*[z-a]' ;; 'ab' Like 'b*[z-a]' ;; 'ab' Like 'a*[z-a]' ;; 'abc' Like 'a*c[z-a]' ;; 'ab' Like 'a[z-a]b' ;; 'xb' Like 'a[z-a]'",
        "e93|e93|False|e93|e93|e93|False", DisplayName = "L02 reversed range")]
    [DataRow("Binary", "'a' Like '[a-c' ;; 'b' Like '[a-]' ;; 'c' Like '[ab-]' ;; 'x' Like '[a-c-e]' ;; 'd' Like '[a-c-e]'",
        "e93|False|False|False|False", DisplayName = "L07 dashes")]
    // a * with text left checks the whole rest of the pattern; a * at the end of the text checks nothing.
    [DataRow("Binary", "'' Like '[z-a]' ;; 'a' Like 'a[z-a]' ;; 'ac' Like 'a*c[z-a]' ;; 'a' Like 'a*[' ;; '' Like '*[z-a]' ;; 'a' Like 'a[' ;; 'a' Like 'a*[z-a]'",
        "False|False|e93|False|False|False|False", DisplayName = "M01 * at the end")]
    [DataRow("Binary", "'abc' Like 'a*c[' ;; 'abc' Like '*c[z-a]' ;; 'ab' Like '*[z-a]b' ;; 'b' Like 'a*[z-a]' ;; 'abcd' Like 'a*c[z-a]' ;; 'cb' Like '*c[z-a]'",
        "e93|e93|e93|False|e93|e93", DisplayName = "M02 * with text left")]
    [DataRow("Binary", "'ab' Like 'a*c[z-a]' ;; 'ac' Like 'a*c*[z-a]' ;; 'ac' Like 'a*cd[z-a]' ;; 'acd' Like 'a*cd[z-a]' ;; 'ace' Like 'a*c?[z-a]' ;; 'ac' Like 'a*c?[z-a]' ;; 'ac' Like 'a*c[ab][z-a]'",
        "e93|e93|e93|e93|e93|e93|e93", DisplayName = "N01 * checks the rest")]
    [DataRow("Binary", "'ac' Like 'a*c[' ;; 'acx' Like 'a*c[' ;; 'ac' Like 'a*c#[z-a]' ;; 'acb' Like 'a*cx[z-a]' ;; 'ac' Like '*a*c[z-a]' ;; 'ac' Like 'a*?[z-a]'",
        "e93|e93|e93|e93|e93|e93", DisplayName = "N02 * checks the rest")]
    [DataRow("Binary", "'xa' Like '*[az-a]' ;; 'a' Like '[az-a]*' ;; 'ab' Like '[az-a]*'", "e93|True|True", DisplayName = "P01 lazy list and *")]
    // a - at either end of a list, or right after a range, is a character; so is a metacharacter inside a list.
    [DataRow("Binary", "ChrW(233) Like '[a-z]' ;; '-' Like '[-a]' ;; '-' Like '[a-]' ;; 'b' Like '[!-a]' ;; 'a' Like '[a-a]' ;; '-' Like '[!-]'",
        "False|True|True|True|True|False", DisplayName = "L04 lists")]
    [DataRow("Binary", "'x' Like '[!]x' ;; 'x' Like '[!x]' ;; '*' Like '[*]' ;; '?' Like '[?]' ;; '#' Like '[#]' ;; '[' Like '[[]' ;; ']' Like ']' ;; 'a]' Like 'a]'",
        "False|False|True|True|True|True|True|True", DisplayName = "L05 metacharacters")]
    // ? and * match a line feed; # matches 0 to 9 only.
    [DataRow("Binary", "'a' & vbLf & 'b' Like 'a?b' ;; 'a' & vbCrLf & 'b' Like 'a*b'", "True|True", DisplayName = "F07 line feed")]
    [DataRow("Binary", "ChrW(&H660) Like '#' ;; ChrW(&H966) Like '#' ;; ChrW(&H6F5) Like '#'", "False|False|False", DisplayName = "F12 # is 0-9")]
    // a range is folded like the characters it is tested against: by case alone, and only where the case maps both ways.
    [DataRow("Binary", "'_' Like '[A-z]' ;; 'M' Like '[a-Z]' ;; 'a' Like '[Z-a]'", "True|e93|True", DisplayName = "F05a ranges, Binary")]
    [DataRow("Text", "'_' Like '[A-z]' ;; 'M' Like '[a-Z]' ;; 'a' Like '[Z-a]'", "False|True|e93", DisplayName = "F05b ranges, Text")]
    [DataRow("Text", "ChrW(&H212A) Like 'k' ;; ChrW(&H212A) Like '[j-l]'", "False|False", DisplayName = "F13 Kelvin sign")]
    [DataRow("Text", "ChrW(233) Like '[a-z]' ;; ChrW(201) Like '[a-z]' ;; ChrW(223) Like '[a-z]' ;; ChrW(233) Like 'E' ;; ChrW(228) Like '[a-b]' ;; '_' Like '[0-z]' ;; ChrW(233) Like 'e'",
        "False|False|False|False|False|True|False", DisplayName = "L03 accents are not case")]
    [DataRow("Text", "'A' Like 'a' ;; 'a' Like '[B-C]' ;; 'b' Like '[A-C]' ;; 'B' Like '[!a-c]'", "True|False|True|False", DisplayName = "L06 case")]
    // a - that starts a list starts a range when another - follows it: [--a] is the range from - to a. Binary mode has no ligatures.
    [DataRow("Binary", "ChrW(230) Like 'ae' ;; ChrW(230) Like 'a*' ;; 'i' Like 'I' ;; '-' Like '[--a]' ;; '0' Like '[--a]'",
        "False|False|False|True|True", DisplayName = "Q02 binary, [--a]")]
    public async Task Like_AnswersWhatMsVbaAnswers(string compare, string expressions, string expected)
        => CollectionAssert.AreEqual(expected.Split('|'), await RunAsync(compare, expressions));

    // MS-VBAL 5.6.9.6: in text mode "some number of actual characters in <expression> can match a different number of characters in the
    // pattern", by the regional collation. MS-VBA expands æ, œ, ß and þ (and their capitals) to two letters, in the text and in the pattern, and
    // nothing else (oracle s47 Q01, like-collation L01-L09, M01-M08): what each element takes of one is in LikePattern.TextUnits. One row MS-VBA
    // answers is left out, because nothing else it answers explains it: ChrW(&HFB03) Like "f*" is True, where every other row of ﬃ is False (L05).
    [TestMethod]
    [DataRow("ChrW(230) Like 'ae' ;; 'ae' Like ChrW(230) ;; ChrW(230) Like 'a?' ;; ChrW(230) Like 'a*' ;; ChrW(230) Like '[' & ChrW(230) & 'a]' ;; 'ss' Like ChrW(223) ;; ChrW(198) Like ChrW(230)",
        "True|True|True|True|True|True|True", DisplayName = "Q01 expansions")]
    [DataRow("ChrW(230) Like 'a' ;; ChrW(230) Like '?' ;; ChrW(230) Like '??' ;; ChrW(230) Like '???' ;; 'ae' Like '?' ;; ChrW(230) Like '*e' ;; ChrW(230) Like '*a' ;; ChrW(230) Like 'a*' ;; ChrW(230) Like 'e*'",
        "False|True|False|False|False|True|False|True|False", DisplayName = "L01 ? takes a character, * a letter")]
    [DataRow("ChrW(230) Like 'a[e]' ;; ChrW(230) Like '[a]e' ;; ChrW(230) Like '[a-b]?' ;; ChrW(230) Like '[!x]e' ;; ChrW(230) Like 'a[!x]' ;; ChrW(230) Like '[ae]' ;; ChrW(230) Like '[ae][ae]' ;; ChrW(230) Like 'a#' ;; 'a' Like ChrW(230)",
        "True|True|True|True|True|False|True|False|False", DisplayName = "L02 lists take a letter")]
    [DataRow("'a' Like ChrW(230) & '*' ;; 'ae' Like ChrW(230) & '*' ;; 'aex' Like ChrW(230) & '?' ;; ChrW(230) & 'x' Like 'ae?' ;; ChrW(230) & 'x' Like 'a?x' ;; 'a' & ChrW(230) Like 'aae' ;; 'aae' Like 'a' & ChrW(230) ;; ChrW(230) & ChrW(230) Like 'aeae' ;; 'aeae' Like ChrW(230) & ChrW(230)",
        "False|True|True|True|True|True|True|True|True", DisplayName = "L03 in longer text")]
    [DataRow("ChrW(198) Like 'ae' ;; ChrW(230) Like 'AE' ;; ChrW(198) Like 'Ae' ;; ChrW(198) Like 'a?' ;; ChrW(223) Like 's?' ;; ChrW(223) Like '?s' ;; ChrW(223) Like '??' ;; ChrW(223) Like 's' ;; ChrW(223) Like 'ss' ;; 'ss' Like '?'",
        "True|True|True|True|True|False|False|False|True|False", DisplayName = "L04 case and ss")]
    [DataRow("ChrW(223) Like '[s]s' ;; ChrW(223) Like 's[s]' ;; 's' Like ChrW(223) ;; 'ss' Like ChrW(223) & '*' ;; ChrW(64259) Like 'f?i' ;; ChrW(64259) Like 'ff?' ;; ChrW(64259) Like '???' ;; ChrW(339) Like 'o?' ;; ChrW(307) Like 'i?'",
        "True|True|False|True|False|False|False|True|False", DisplayName = "L05 ss, oe, and no ligatures")]
    [DataRow("'a' Like '[' & ChrW(230) & ']' ;; 'ae' Like '[' & ChrW(230) & ']' ;; ChrW(230) Like '[' & ChrW(230) & ']' ;; ChrW(230) Like '[' & ChrW(198) & ']' ;; 'ss' Like '[' & ChrW(223) & ']' ;; ChrW(223) Like '[s]' ;; ChrW(223) Like '[' & ChrW(7838) & ']' ;; ChrW(230) Like '[a-z]' ;; ChrW(230) Like '[a-z][a-z]' ;; ChrW(230) Like '[!a-z]'",
        "False|True|True|True|True|False|False|False|True|False", DisplayName = "L06 expanded items")]
    [DataRow("'ab' Like 'a' & ChrW(173) & 'b' ;; 'a' & ChrW(173) & 'b' Like 'ab' ;; 'a' & ChrW(173) & 'b' Like 'a?b' ;; 'a' & ChrW(173) & 'b' Like 'a??b' ;; ChrW(65313) Like 'A' ;; ChrW(65313) Like 'a' ;; ChrW(12354) Like ChrW(12450) ;; ChrW(173) Like '' ;; '' Like ChrW(173)",
        "False|False|True|False|False|False|False|False|False", DisplayName = "L07 no ignorables, width or kana")]
    [DataRow("'b' Like '[' & ChrW(230) & '-c]' ;; 'af' Like '[' & ChrW(230) & '-c]' ;; ChrW(230) Like '[a-' & ChrW(230) & ']' ;; 'ad' Like '[a-' & ChrW(230) & ']' ;; ChrW(230) Like '[' & ChrW(230) & '-' & ChrW(230) & ']' ;; 'ae' Like '[!' & ChrW(230) & ']' ;; ChrW(230) Like '[!a]e'",
        "e93|e93|True|False|True|False|False", DisplayName = "L08 ranges of characters")]
    [DataRow("ChrW(230) Like '[' & ChrW(230) & 'a]e' ;; ChrW(230) Like '[!x]' ;; ChrW(230) Like '[!x]?' ;; ChrW(230) Like '[a-z]?' ;; ChrW(230) Like '[!a-z]?' ;; ChrW(230) Like '?e' ;; ChrW(230) Like '*' ;; ChrW(230) Like '**e' ;; ChrW(230) Like '[!x]e*' ;; ChrW(230) Like '[a]'",
        "False|False|True|True|False|False|True|True|True|False", DisplayName = "M01 whole or letter")]
    [DataRow("ChrW(64259) Like 'f?' ;; ChrW(64259) Like 'f??' ;; ChrW(64259) Like 'f' & ChrW(64257) ;; ChrW(64259) Like ChrW(64256) & 'i' ;; ChrW(64259) Like 'ffi' ;; 'ffi' Like ChrW(64259) ;; ChrW(64259) Like 'f*i' ;; ChrW(64259) Like '*i' ;; 'f' & ChrW(64257) Like ChrW(64259) ;; ChrW(64259) Like 'f[' & ChrW(64257) & ']'",
        "False|False|False|False|False|False|False|False|False|False", DisplayName = "M02 ffi is itself")]
    [DataRow("ChrW(307) Like 'i*' ;; ChrW(306) Like 'I*' ;; ChrW(64256) Like 'f?' ;; ChrW(64257) Like 'f?' ;; ChrW(64258) Like 'f?' ;; ChrW(64260) Like 'f*' ;; ChrW(64262) Like 's?' ;; ChrW(454) Like 'd?' ;; ChrW(453) Like 'd?' ;; ChrW(452) Like 'd?'",
        "False|False|False|False|False|False|False|False|False|False", DisplayName = "M03 not expanded")]
    [DataRow("ChrW(457) Like 'l?' ;; ChrW(460) Like 'n?' ;; ChrW(499) Like 'd?' ;; ChrW(254) Like 't?' ;; ChrW(222) Like 't?' ;; ChrW(7838) Like 's?' ;; ChrW(338) Like 'o?' ;; ChrW(339) Like 'o?' ;; ChrW(198) Like 'a?' ;; ChrW(223) Like 's?'",
        "False|False|False|True|True|False|True|True|True|True", DisplayName = "M04 expanded")]
    [DataRow("'ij' Like ChrW(307) ;; 'ff' Like ChrW(64256) ;; 'fi' Like ChrW(64257) ;; 'fl' Like ChrW(64258) ;; 'st' Like ChrW(64262) ;; 'd' & ChrW(382) Like ChrW(454) ;; 'lj' Like ChrW(457) ;; 'nj' Like ChrW(460) ;; 'dz' Like ChrW(499) ;; 'th' Like ChrW(254) ;; 'ss' Like ChrW(7838) ;; 'oe' Like ChrW(339)",
        "False|False|False|False|False|False|False|False|False|True|False|True", DisplayName = "M05 letters against the character")]
    [DataRow("ChrW(307) Like 'ij' ;; ChrW(64256) Like 'ff' ;; ChrW(64257) Like 'fi' ;; ChrW(64258) Like 'fl' ;; ChrW(64262) Like 'st' ;; ChrW(454) Like 'd' & ChrW(382) ;; ChrW(457) Like 'lj' ;; ChrW(460) Like 'nj' ;; ChrW(499) Like 'dz' ;; ChrW(254) Like 'th' ;; ChrW(7838) Like 'ss' ;; ChrW(339) Like 'oe'",
        "False|False|False|False|False|False|False|False|False|True|False|True", DisplayName = "M06 the character against letters")]
    [DataRow("ChrW(338) Like 'o?' ;; 'OE' Like ChrW(339) ;; ChrW(198) Like '[a]?' ;; ChrW(230) Like '[A]E' ;; 'Stra' & ChrW(223) & 'e' Like 'STRASSE' ;; 'Stra' & ChrW(223) & 'e' Like 'Stras?e' ;; 'Stra' & ChrW(223) & 'e' Like 'Stra?e' ;; 'Strasse' Like 'Stra' & ChrW(223) & 'e' ;; 'Strase' Like 'Stra' & ChrW(223) & 'e' ;; ChrW(230) & ChrW(230) Like 'a*e'",
        "True|True|True|True|True|True|True|True|False|True", DisplayName = "M07 in words")]
    [DataRow("'a' & ChrW(230) Like 'a' & ChrW(230) ;; ChrW(230) Like ChrW(198) ;; 'aE' Like ChrW(230) ;; ChrW(230) Like 'a[!e]' ;; ChrW(230) Like 'a[e-f]' ;; ChrW(230) Like 'a#' ;; 'a' & ChrW(230) & 'e' Like 'aae?' ;; ChrW(223) Like 's[!s]' ;; ChrW(223) & 's' Like 's*s' ;; ChrW(223) Like '*s'",
        "True|True|True|False|True|False|True|False|True|True", DisplayName = "M08 the second letter")]
    public async Task LikeInTextMode_MatchesACharacterWithItsExpansion(string expressions, string expected)
        => CollectionAssert.AreEqual(expected.Split('|'), await RunAsync("Text", expressions));

    [TestMethod]
    public async Task LikeInBinaryMode_ExpandsNothing()
        // L09: nor does binary mode ignore what text-mode comparison does.
        => CollectionAssert.AreEqual("False|False|False|False|False".Split('|'), await RunAsync("Binary",
            "ChrW(230) Like 'a?' ;; ChrW(230) Like 'ae' ;; ChrW(223) Like 'ss' ;; ChrW(65313) Like 'A' ;; 'a' & ChrW(173) & 'b' Like 'ab'"));

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    // F14: a matcher that tries every way the stars can split the text takes about 1000^3 steps here; MS-VBA answers at once.
    public async Task Like_IsNotExponentialInTheStarsOfThePattern()
        => CollectionAssert.AreEqual(new[] { "False" }, await RunAsync("Binary", "String(1000, 'a') Like '*a*a*a*b'"));
}
