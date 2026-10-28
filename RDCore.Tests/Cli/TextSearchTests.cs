namespace RDCore.Tests.Cli;

/// <summary>
/// The text-mode search of <c>InStr</c>, <c>InStrRev</c>, <c>Replace</c>, <c>Split</c> and <c>Filter</c>, end to end, as MS-VBA does it: the letters of both
/// texts, folded to one case and with <c>æ</c>, <c>œ</c>, <c>ß</c> and <c>þ</c> expanded, and nothing ignored. Every row was run in Excel (oracle\instr-text),
/// and the batches keep their names there.
/// </summary>
/// <remarks>
/// A row is one module: its <c>Option Compare</c>, then expressions separated by <c>;;</c>, with <c>'</c> for <c>"</c>. Each prints its value, or <c>e</c> and
/// the number of the error it raised.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11 Strings")]
public sealed class TextSearchTests
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

    private const string Ae = "ChrW(230)";

    [TestMethod]
    [DataRow("Binary", $"InStr(1, 'xaex', {Ae}, 1) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 1) ;; InStr(1, 'x' & {Ae} & 'x', 'a', 1) ;; InStr(1, 'x' & {Ae} & 'x', 'e', 1) ;; InStr(1, 'x' & {Ae} & 'x', 'ex', 1) ;; InStr(1, 'x' & {Ae} & 'x', 'xa', 1) ;; InStr(1, 'a' & {Ae}, 'aa', 1) ;; InStr(1, 'x' & {Ae} & 'x', 'xae', 1) ;; InStr(1, 'x' & {Ae} & 'x', 'aex', 1)",
        "2|2|2|2|2|1|1|1|2", DisplayName = "N01 an occurrence in part of an expanded character")]
    [DataRow("Binary", "InStr(1, 'x' & ChrW(223) & 'x', 's', 1) ;; InStr(1, 'x' & ChrW(223) & 'x', 'ss', 1) ;; InStr(1, 'x' & ChrW(223) & 'x', 'sx', 1) ;; InStr(1, 'x' & ChrW(223) & 'x', 'xs', 1) ;; InStr(1, 'xssx', ChrW(223), 1) ;; InStr(1, 'xsssx', ChrW(223), 1) ;; InStr(1, 'Stra' & ChrW(223) & 'e', 'SS', 1) ;; InStr(1, 'x' & ChrW(223) & ChrW(223) & 'x', 'sss', 1)",
        "2|2|2|1|2|2|5|2", DisplayName = "N02 ss")]
    [DataRow("Binary", "InStr(1, 'x' & ChrW(307) & 'x', 'i', 1) ;; InStr(1, 'x' & ChrW(307) & 'x', 'ij', 1) ;; InStr(1, 'x' & ChrW(64257) & 'x', 'f', 1) ;; InStr(1, 'x' & ChrW(64257) & 'x', 'fi', 1) ;; InStr(1, 'x' & ChrW(454) & 'x', 'd', 1) ;; InStr(1, 'x' & ChrW(254) & 'x', 't', 1) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 1) ;; InStr(1, 'x' & ChrW(339) & 'x', 'o', 1) ;; InStr(1, 'xthx', ChrW(254), 1) ;; InStr(1, 'xffix', ChrW(64259), 1)",
        "0|0|0|0|0|2|2|2|2|0", DisplayName = "N03 which characters expand")]
    [DataRow("Binary", "InStr(1, 'xAx', 'a', 1) ;; InStr(1, 'x' & ChrW(65313) & 'x', 'a', 1) ;; InStr(1, 'x' & ChrW(12450) & 'x', ChrW(12354), 1) ;; InStr(1, 'x' & ChrW(65393) & 'x', ChrW(12450), 1) ;; InStr(1, 'x' & ChrW(228) & 'x', 'a', 1) ;; InStr(1, 'x' & ChrW(233) & 'x', 'e', 1) ;; InStr(1, 'xEx', ChrW(233), 1) ;; InStr(1, 'x' & ChrW(&H212A) & 'x', 'k', 1)",
        "2|0|0|0|0|0|0|0", DisplayName = "N04 case only: no width, kana or accent")]
    [DataRow("Binary", "InStr(1, 'xa' & ChrW(173) & 'bx', 'ab', 1) ;; InStr(1, 'xabx', 'a' & ChrW(173) & 'b', 1) ;; InStr(1, 'a' & ChrW(173) & 'b', ChrW(173), 1) ;; InStr(1, 'ab', ChrW(173), 1) ;; InStr(1, 'ab', '', 1) ;; InStr(1, 'xa' & ChrW(0) & 'bx', 'ab', 1) ;; InStr(1, 'xa' & ChrW(8205) & 'bx', 'ab', 1) ;; InStr(1, 'xa' & ChrW(8203) & 'bx', 'ab', 1) ;; InStr(1, 'x' & ChrW(173) & 'ab', 'ab', 1) ;; InStr(1, ChrW(173) & 'ab', 'a', 1)",
        "0|0|2|0|1|0|0|0|3|2", DisplayName = "N05 nothing ignored")]
    [DataRow("Binary", $"InStr(3, 'x' & {Ae} & 'x' & {Ae}, 'a', 1) ;; InStr(2, 'x' & {Ae} & 'x', 'e', 1) ;; InStr(3, 'x' & {Ae} & 'x', 'e', 1) ;; InStrRev('x' & {Ae} & 'x', 'e', -1, 1) ;; InStrRev('xaex', {Ae}, -1, 1) ;; InStrRev({Ae}, 'a', -1, 1) ;; InStrRev('x' & {Ae} & 'x' & {Ae}, 'a', -1, 1) ;; InStrRev('x' & {Ae} & 'x', 'a', 2, 1)",
        "4|2|0|2|2|1|4|2", DisplayName = "N06 starts, and InStrRev")]
    [DataRow("Binary", $"Replace('x' & {Ae} & 'x', 'a', '-', 1, -1, 1) = 'x' & {Ae} & 'x' ;; Replace('x' & {Ae} & 'x', 'e', '-', 1, -1, 1) = 'x' & {Ae} & 'x' ;; Replace('x' & ChrW(223) & 'x', 's', '-', 1, -1, 1) = 'x' & ChrW(223) & 'x' ;; Replace('xaex', {Ae}, '-', 1, -1, 1) ;; UBound(Split('a' & {Ae} & 'b', 'e', -1, 1)) ;; Join(Split('a' & {Ae} & 'b', 'e', -1, 1), '|') = 'a' & {Ae} & 'b' ;; UBound(Filter(Array({Ae}, 'b'), 'a', True, 1)) ;; Replace('xa' & ChrW(173) & 'bx', 'ab', '-', 1, -1, 1) = 'xa' & ChrW(173) & 'bx'",
        "True|True|True|x-x|0|True|0|True", DisplayName = "N07 Replace and Split take whole characters, Filter finds parts")]
    [DataRow("Text", $"InStr('x' & {Ae} & 'x', 'e') ;; InStr(1, 'x' & {Ae} & 'x', 'e') ;; InStr(1, 'x' & {Ae} & 'x', 'e', 0) ;; InStr('xAx', 'a') ;; Replace('xAx', 'a', '-') ;; InStrRev('xAx', 'a')",
        "2|2|0|2|x-x|2", DisplayName = "N08 Option Compare Text")]
    [DataRow("Binary", $"InStr(1, 'x' & {Ae} & 'x', 'a', 0) ;; InStr(1, 'x' & {Ae} & 'x', 'e') ;; InStr(1, 'xAx', 'a') ;; InStr(1, 'x' & {Ae} & 'x', 'e', 2)",
        "0|0|0|e5", DisplayName = "N09 binary mode, and vbDatabaseCompare")]
    public async Task ATextSearch_FindsWhatMsVbaFinds(string compare, string expressions, string expected)
        => CollectionAssert.AreEqual(expected.Split('|'), await RunAsync(compare, expressions));

    [TestMethod]
    public async Task ACompareThatIsALocaleIdentifier_IsATextComparison()
        => CollectionAssert.AreEqual("2|0|x-x|2".Split('|'), await RunAsync("Binary",
            "InStr(1, 'xAx', 'a', 1033) ;; StrComp('A', 'a', 1033) ;; Replace('xAx', 'a', '-', 1, -1, 1033) ;; InStrRev('xAx', 'a', -1, 1033)"));

    // a Compare that is a locale identifier is that locale's text comparison, one that is no locale's identifier is error 5: what MS-VBA takes is what Windows
    // knows of as a locale - a neutral language, a locale, one with a sort order, and the default locales' identifiers.
    [TestMethod]
    [DataRow("InStr(1, 'xAx', 'a', 3) ;; InStr(1, 'xAx', 'a', 4) ;; InStr(1, 'xAx', 'a', 100) ;; InStr(1, 'xAx', 'a', 1031) ;; InStr(1, 'xAx', 'a', 1033) ;; InStr(1, 'xAx', 'a', 65535) ;; InStr(1, 'xAx', 'a', 65536) ;; InStr(1, 'xAx', 'a', -1) ;; InStr(1, 'xAx', 'a', -2)",
        "2|2|2|2|2|e5|e5|e5|e5", DisplayName = "neutral languages and locales, and values that are none")]
    [DataRow("InStr(1, 'xAx', 'a', 32767) ;; InStr(1, 'xAx', 'a', 32768) ;; InStr(1, 'xAx', 'a', 49151) ;; InStr(1, 'xAx', 'a', 65534) ;; InStr(1, 'xAx', 'a', 5000) ;; InStr(1, 'xAx', 'a', 20000) ;; InStr(1, 'xAx', 'a', 40000)",
        "e5|e5|e5|e5|e5|e5|e5", DisplayName = "the identifiers of no locale")]
    [DataRow("InStr(1, 'xAx', 'a', 5) ;; InStr(1, 'xAx', 'a', 19) ;; InStr(1, 'xAx', 'a', 25) ;; InStr(1, 'xAx', 'a', 128) ;; InStr(1, 'xAx', 'a', 129) ;; InStr(1, 'xAx', 'a', 1024) ;; InStr(1, 'xAx', 'a', 2048) ;; InStr(1, 'xAx', 'a', 3072)",
        "2|2|2|2|2|2|2|2", DisplayName = "more neutral languages, and the default locales")]
    [DataRow("InStr(1, 'xAx', 'a', 4096) ;; InStr(1, 'xAx', 'a', 5120) ;; InStr(1, 'xAx', 'a', 66567) ;; InStr(1, 'xAx', 'a', 31748) ;; InStr(1, 'xAx', 'a', 20490) ;; InStr(1, 'xAx', 'a', 127) ;; InStr(1, 'xAx', 'a', 240) ;; InStr(1, 'xAx', 'a', 255)",
        "2|2|2|2|2|2|e5|e5", DisplayName = "a sort order, the invariant locale, no language")]
    [DataRow("InStr(1, 'xAx', 'a', 58378) ;; InStr(1, 'xAx', 'a', 9999) ;; StrComp('A', 'a', 66567) ;; Replace('xAx', 'a', '-', 1, -1, 66567) ;; StrComp('A', 'a', 240) ;; Replace('xAx', 'a', '-', 1, -1, 240)",
        "e5|e5|0|x-x|e5|e5", DisplayName = "StrComp and Replace with a sort order, and with no locale")]
    [DataRow("InStr(1, 'xAx', 'a', 2.6) ;; InStr(1, 'xAx', 'a', 1033.4) ;; InStr(1, 'xAx', 'a', '1033') ;; InStr(1, 'xAx', 'a', 1.5) ;; InStr(1, 'xAx', 'a', 0.5) ;; InStr(1, 'xAx', 'a', True) ;; InStr(1, 'xAx', 'a', 4294968329#) ;; InStr(1, 'xAx', 'a', -64503)",
        "2|2|2|e5|0|e5|e6|e5", DisplayName = "Compare let-coerced to a Long")]
    [DataRow("StrComp('A', 'a', 1033) ;; StrComp('A', 'a', 3) ;; Replace('xAx', 'a', '-', 1, -1, 1033) ;; UBound(Split('xAx', 'a', -1, 1033)) ;; InStrRev('xAx', 'a', -1, 1033) ;; UBound(Filter(Array('A'), 'a', True, 1033))",
        "0|0|x-x|1|2|0", DisplayName = "every member under a locale")]
    public async Task ACompareThatIsALocaleIdentifier_IsThatLocalesComparison_AndAnyOtherIsError5(string expressions, string expected)
        => CollectionAssert.AreEqual(expected.Split('|'), await RunAsync("Binary", expressions));

    // Compare is let-coerced as a Long argument is: Null is 94, a String that is no number 13, and Empty is 0, a binary comparison.
    [TestMethod]
    [DataRow("InStr(1, 'xAx', 'a', Null) ;; InStr(1, 'xAx', 'a', Empty) ;; InStr(1, 'xAx', 'a', 'abc') ;; StrComp('A', 'a', Null) ;; StrComp('A', 'a', Empty) ;; StrComp('A', 'a', 'abc') ;; InStrB(1, 'xAx', 'a', Null) ;; InStrB(1, 'xAx', 'A', Empty)",
        "e94|0|e13|e94|-1|e13|e94|3", DisplayName = "InStr, StrComp and InStrB")]
    [DataRow("Replace('xAx', 'a', '-', 1, -1, Null) ;; Replace('xAx', 'a', '-', 1, -1, Empty) ;; Replace('xAx', 'a', '-', 1, -1, 'abc') ;; InStrRev('xAx', 'a', -1, Null) ;; InStrRev('xAx', 'a', -1, 'abc') ;; UBound(Split('xAx', 'a', -1, Null)) ;; UBound(Split('xAx', 'a', -1, 'abc')) ;; UBound(Filter(Array('A'), 'a', True, Null))",
        "e94|xAx|e13|e94|e13|e94|e13|e94", DisplayName = "Replace, InStrRev, Split and Filter")]
    public async Task ACompareThatIsNullEmptyOrNoNumber_IsCoercedAsALongIs(string expressions, string expected)
        => CollectionAssert.AreEqual(expected.Split('|'), await RunAsync("Binary", expressions));

    // InStrB compares bytes whatever Compare is: a text comparison, a locale, 2, and a value no other member takes.
    [TestMethod]
    [DataRow("InStrB(1, 'xAx', 'a', 0) ;; InStrB(1, 'xAx', 'a', 1) ;; InStrB(1, 'xAx', 'a', 2) ;; InStrB(1, 'xAx', 'A', 2) ;; InStrB(1, 'xAx', 'a', 1033) ;; InStrB(1, 'xAx', 'A', 1033) ;; InStrB(1, 'xAx', 'a', 240) ;; InStrB(1, 'xAx', 'a', -1)",
        "0|0|0|3|0|3|0|0", DisplayName = "every Compare searches the bytes")]
    [DataRow($"InStrB(1, 'x' & {Ae} & 'x', 'ae', 1030) ;; InStrB(1, 'x' & {Ae} & 'x', 'ae', 1033) ;; InStrB(1, 'x' & {Ae} & 'x', 'ae', 1) ;; InStrB(1, 'xAx', 'a', 7) ;; InStrB(1, 'xAx', 'A', 0) ;; InStrB(1, 'x' & {Ae} & 'x', 'ae', 2) ;; InStrB(1, 'xbAx', 'a', 2) ;; InStrB(1, 'xbAx', 'a', 1)",
        "0|0|0|0|3|0|0|0", DisplayName = "nothing is folded or expanded")]
    public async Task InStrB_SearchesTheBytes_WhateverItsCompareIs(string expressions, string expected)
        => CollectionAssert.AreEqual(expected.Split('|'), await RunAsync("Binary", expressions));

    [TestMethod]
    public async Task InStrB_UnderOptionCompareText_SearchesTheBytes()
        => CollectionAssert.AreEqual("0|0|2|3|0".Split('|'), await RunAsync("Text",
            "InStrB(1, 'xAx', 'a') ;; InStrB('xAx', 'a') ;; InStr(1, 'xAx', 'a') ;; InStrB(1, 'xAx', 'A') ;; InStrB(1, 'xAx', 'a', 1)"));

    // a locale's comparison: case folded the same way in every locale, æ and þ letters of their own where the locale's alphabet has them, œ and ß expanded
    // everywhere, and the order of the locale.
    [TestMethod]
    [DataRow($"InStr(1, 'x' & {Ae} & 'x', 'ae', 1030) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 1044) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 1033) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 1031) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 1035) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 1053) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 1039) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 1036)",
        "0|0|2|2|2|2|0|2", DisplayName = "æ, by locale")]
    [DataRow("InStr(1, 'x' & ChrW(339) & 'x', 'oe', 1030) ;; InStr(1, 'x' & ChrW(339) & 'x', 'oe', 1044) ;; InStr(1, 'x' & ChrW(339) & 'x', 'oe', 1033) ;; InStr(1, 'x' & ChrW(339) & 'x', 'oe', 1039) ;; InStr(1, 'x' & ChrW(223) & 'x', 'ss', 1030) ;; InStr(1, 'x' & ChrW(223) & 'x', 'ss', 1044) ;; InStr(1, 'x' & ChrW(223) & 'x', 'ss', 1039) ;; InStr(1, 'x' & ChrW(223) & 'x', 'ss', 1053)",
        "2|2|2|2|2|2|2|2", DisplayName = "œ and ß, expanded in every locale")]
    [DataRow("InStr(1, 'x' & ChrW(254) & 'x', 'th', 1030) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 1044) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 1033) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 1031) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 1035) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 1053) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 1039) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 1036)",
        "0|0|2|2|2|2|0|2", DisplayName = "þ, by locale")]
    [DataRow($"InStr(1, 'x' & {Ae} & 'x', 'ae', 2068) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 1080) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 1135) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 1083) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 2077) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 6) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 20) ;; InStr(1, 'x' & {Ae} & 'x', 'ae', 15)",
        "0|0|0|0|0|0|0|0", DisplayName = "the other locales that keep æ")]
    [DataRow("InStr(1, 'x' & ChrW(254) & 'x', 'th', 2068) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 1080) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 1135) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 1083) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 2077) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 6) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 20) ;; InStr(1, 'x' & ChrW(254) & 'x', 'th', 15)",
        "0|0|0|0|0|0|0|0", DisplayName = "the other locales that keep þ")]
    [DataRow("InStr(1, 'x' & ChrW(&HFF41) & 'x', 'a', 1033) ;; InStr(1, 'x' & ChrW(&H30A2) & 'x', ChrW(&H3042), 1041) ;; InStr(1, 'x' & ChrW(233) & 'x', 'e', 1033) ;; StrComp(ChrW(233), 'e', 1033) ;; StrComp('a', 'B', 1033)",
        "0|0|0|1|-1", DisplayName = "width, kana and accents are not folded")]
    [DataRow($"StrComp({Ae}, 'af', 1030) ;; StrComp({Ae}, 'af', 1033) ;; StrComp(ChrW(228), 'z', 1053) ;; StrComp(ChrW(228), 'z', 1033) ;; StrComp('ch', 'd', 1051) ;; StrComp('ch', 'd', 1033)",
        "1|-1|1|-1|1|-1", DisplayName = "the order of a locale")]
    [DataRow($"UBound(Split('x' & {Ae} & 'x', 'ae', -1, 1030)) ;; UBound(Split('x' & {Ae} & 'x', 'ae', -1, 1033)) ;; InStrRev('x' & {Ae} & 'x', 'ae', -1, 1030) ;; Replace('x' & {Ae} & 'x', 'ae', '-', 1, -1, 1033) ;; UBound(Filter(Array({Ae}), 'ae', True, 1030))",
        "0|1|0|x-x|-1", DisplayName = "the other members under Danish")]
    [DataRow("InStr(1, 'x' & ChrW(304) & 'x', 'i', 1055) ;; InStr(1, 'xIx', ChrW(305), 1055) ;; InStr(1, 'xIx', 'i', 1055) ;; InStr(1, 'x' & ChrW(931) & 'x', ChrW(962), 1032) ;; InStr(1, 'x' & ChrW(&H212A) & 'x', 'k', 1033) ;; StrComp('I', 'i', 1055) ;; StrComp(ChrW(304), 'i', 1033)",
        "0|0|2|0|0|0|1", DisplayName = "case, in Turkish and Greek")]
    public async Task ALocalesComparison_IsWhatMsVbaCompares(string expressions, string expected)
        => CollectionAssert.AreEqual(expected.Split('|'), await RunAsync("Binary", expressions));

    // ICU's Turkish collation orders the dotless and the dotted i as letters of the Turkish alphabet, and has İ as the capital of i: MS-VBA's order of them
    // differs, which is the collation's, not the case's.
    [TestMethod]
    [Ignore("under ICU, StrComp of the dotless and the dotted i in a Turkish locale gives 1 and 0, where MS-VBA gives -1 and 1: fixing it means choosing the collation engine")]
    public async Task TheDotlessAndTheDottedI_AreOtherLettersThanIAndI_InATurkishOrder()
        => CollectionAssert.AreEqual("-1|1".Split('|'), await RunAsync("Binary", "StrComp('I', ChrW(305), 1055) ;; StrComp(ChrW(304), 'i', 1055)"));

    // vbDatabaseCompare, in a host that has no database: Split, InStrRev and Replace compare as Option Compare Database does, and InStr, StrComp and Filter
    // refuse it.
    [TestMethod]
    public async Task VbDatabaseCompare_IsTakenBySplitInStrRevAndReplace_AndRefusedByTheOthers()
        => CollectionAssert.AreEqual("1|e5|2|x-x|xAx|e5|e5|e5|e5".Split('|'), await RunAsync("Binary",
            "UBound(Split('xAx', 'a', -1, 2)) ;; UBound(Filter(Array('A'), 'a', True, 2)) ;; InStrRev('xAx', 'a', -1, 2) ;; Replace('xAx', 'a', '-', 1, -1, 2) ;; Replace('xAx', 'b', '-', 1, -1, 2) ;; InStr(1, 'xAx', 'a', 1.5) ;; InStr(1, 'xAx', 'a', 2.5) ;; StrComp('A', 'a', 2) ;; InStr(1, 'xAx', 'a', 2)"));
}
