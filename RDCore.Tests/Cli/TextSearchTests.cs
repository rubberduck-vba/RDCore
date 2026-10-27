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

    // MS-VBA takes a Compare that is a locale identifier as a text comparison - 3, 4, 100, 1031, 1033, and not 5000, 32767 or 65535 - and Replace takes 2 as
    // one, where InStr and StrComp raise 5; Danish and Norwegian do not expand æ (oracle\instr-text C01-C03, D01-D02). RD-VBA raises 5 for any Compare but 0
    // and 1.
    [TestMethod]
    [Ignore("A Compare that is a locale identifier (1033) is a text comparison in MS-VBA, and error 5 in RD-VBA (oracle\\instr-text C01-C03, D01-D02)")]
    public async Task ACompareThatIsALocaleIdentifier_IsATextComparison()
        => CollectionAssert.AreEqual("2|0|x-x|2".Split('|'), await RunAsync("Binary",
            "InStr(1, 'xAx', 'a', 1033) ;; StrComp('A', 'a', 1033) ;; Replace('xAx', 'a', '-', 1, -1, 1033) ;; InStrRev('xAx', 'a', -1, 1033)"));
}
