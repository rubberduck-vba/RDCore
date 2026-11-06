using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>ReDim</c> of a parameter that shares a declared dynamic array, and of a Variant that holds a copy of one (<strong>MS-VBAL §5.4.3.3</strong>, RD-VBAL
/// 5.4.3.3): an upper bound below its lower, and an array too big to hold.
/// </summary>
/// <remarks>
/// Each expected line is what MS-VBA printed for the same code in Excel (64-bit, en-US, <c>oracle\s68</c>): the shape of what the ReDim was given, and then of
/// the caller's array, each <c>TypeName (bounds) [1]=element</c>, <c>erased</c> for an array with no dimensions, and <c>?</c> for an element that can't be read.
/// The caller's array is <c>a() As Long</c>, <c>(0 To 3)</c> with <c>a(1) = 7</c>, before each.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim")]
public sealed class RedimThroughAParameterTests
{
    // the bounds each kind is given, in the order of the grid: two that fit, four one below the lower, three further below, and one too big.
    private static readonly string[] Bounds = ["5", "1 To 2, 0 To 3", "5 To 4", "-1", "5 To 2, 5 To 4", "0 To 1, 5 To 4", "5 To 2", "0 To -5", "0 To 1, 5 To 2", "2147483647"];

    private static readonly Dictionary<string, string> MsVba = new()
    {
        ["V01"] = "G 0 Long() (0 To 5) [1]=0 | a Long() (0 To 5) [1]=0 |",
        ["V02"] = "G 0 Long() (1 To 2, 0 To 3) [1]=? | a Long() (1 To 2, 0 To 3) [1]=? |",
        ["V03"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["V04"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["V05"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["V06"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["V07"] = "G 7 Long() erased [1]=? | a Long() erased [1]=? |",
        ["V08"] = "G 7 Long() erased [1]=? | a Long() erased [1]=? |",
        ["V09"] = "G 7 Long() erased [1]=? | a Long() erased [1]=? |",
        ["V10"] = "G 7 Long() erased [1]=? | a Long() erased [1]=? |",
        ["VV11"] = "G 0 Long() (0 To 5) [1]=0 | a Long() (0 To 5) [1]=0 |",
        ["VV12"] = "G 0 Long() (1 To 2, 0 To 3) [1]=? | a Long() (1 To 2, 0 To 3) [1]=? |",
        ["VV13"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["VV14"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["VV15"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["VV16"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["VV17"] = "G 7 Long() erased [1]=? | a Long() erased [1]=? |",
        ["VV18"] = "G 7 Long() erased [1]=? | a Long() erased [1]=? |",
        ["VV19"] = "G 7 Long() erased [1]=? | a Long() erased [1]=? |",
        ["VV20"] = "G 7 Long() erased [1]=? | a Long() erased [1]=? |",
        ["BV21"] = "G 0 Long() (0 To 5) [1]=0 | a Long() (0 To 3) [1]=7 |",
        ["BV22"] = "G 0 Long() (1 To 2, 0 To 3) [1]=? | a Long() (0 To 3) [1]=7 |",
        ["BV23"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["BV24"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["BV25"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["BV26"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["BV27"] = "G 7 Empty [1]=? | a Long() (0 To 3) [1]=7 |",
        ["BV28"] = "G 7 Empty [1]=? | a Long() (0 To 3) [1]=7 |",
        ["BV29"] = "G 7 Empty [1]=? | a Long() (0 To 3) [1]=7 |",
        ["BV30"] = "G 7 Empty [1]=? | a Long() (0 To 3) [1]=7 |",
        ["LA31"] = "G 0 Long() (0 To 5) [1]=0 | a Long() (0 To 5) [1]=0 |",
        ["LA32"] = "G 0 Long() (1 To 2, 0 To 3) [1]=? | a Long() (1 To 2, 0 To 3) [1]=? |",
        ["LA33"] = "G 9 Long() erased [1]=? | a Long() erased [1]=? |",
        ["LA34"] = "G 9 Long() erased [1]=? | a Long() erased [1]=? |",
        ["LA35"] = "G 9 Long() erased [1]=? | a Long() erased [1]=? |",
        ["LA36"] = "G 9 Long() erased [1]=? | a Long() erased [1]=? |",
        ["LA37"] = "G 9 Long() erased [1]=? | a Long() erased [1]=? |",
        ["LA38"] = "G 9 Long() erased [1]=? | a Long() erased [1]=? |",
        ["LA39"] = "G 9 Long() erased [1]=? | a Long() erased [1]=? |",
        ["LA40"] = "G 7 Long() erased [1]=? | a Long() erased [1]=? |",
        ["VW41"] = "G 0 Long() (0 To 5) [1]=0 | a Long() (0 To 3) [1]=7 | w Long() (0 To 5) [1]=0 |",
        ["VW42"] = "G 0 Long() (1 To 2, 0 To 3) [1]=? | a Long() (0 To 3) [1]=7 | w Long() (1 To 2, 0 To 3) [1]=? |",
        ["VW43"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 | w Long() (0 To 3) [1]=7 |",
        ["VW44"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 | w Long() (0 To 3) [1]=7 |",
        ["VW45"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 | w Long() (0 To 3) [1]=7 |",
        ["VW46"] = "G 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 | w Long() (0 To 3) [1]=7 |",
        ["VW47"] = "G 7 Empty [1]=? | a Long() (0 To 3) [1]=7 | w Empty [1]=? |",
        ["VW48"] = "G 7 Empty [1]=? | a Long() (0 To 3) [1]=7 | w Empty [1]=? |",
        ["VW49"] = "G 7 Empty [1]=? | a Long() (0 To 3) [1]=7 | w Empty [1]=? |",
        ["VW50"] = "G 7 Empty [1]=? | a Long() (0 To 3) [1]=7 | w Empty [1]=? |",
        ["W51"] = "R 0 Long() (0 To 5) [1]=0 | a Long() (0 To 3) [1]=7 | w Long() (0 To 5) [1]=0 |",
        ["W52"] = "R 0 Long() (1 To 2, 0 To 3) [1]=? | a Long() (0 To 3) [1]=7 | w Long() (1 To 2, 0 To 3) [1]=? |",
        ["W53"] = "R 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 | w Long() (0 To 3) [1]=7 |",
        ["W54"] = "R 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 | w Long() (0 To 3) [1]=7 |",
        ["W55"] = "R 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 | w Long() (0 To 3) [1]=7 |",
        ["W56"] = "R 9 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 | w Long() (0 To 3) [1]=7 |",
        ["W57"] = "R 7 Empty [1]=? | a Long() (0 To 3) [1]=7 | w Empty [1]=? |",
        ["W58"] = "R 7 Empty [1]=? | a Long() (0 To 3) [1]=7 | w Empty [1]=? |",
        ["W59"] = "R 7 Empty [1]=? | a Long() (0 To 3) [1]=7 | w Empty [1]=? |",
        ["W60"] = "R 7 Empty [1]=? | a Long() (0 To 3) [1]=7 | w Empty [1]=? |",
    };

    // the parameter each kind is: none for W, whose ReDim is of a Variant local that holds a copy of the array, and VW's is given that Variant.
    private static readonly Dictionary<string, string> Parameters = new()
    {
        ["V"] = "v",
        ["VV"] = "ByRef v As Variant",
        ["BV"] = "ByVal v",
        ["LA"] = "v() As Long",
        ["VW"] = "v",
    };

    private static string Program(string kind)
    {
        List<string> lines =
        [
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Private a() As Long",
            "Private Trace As String",
            "Private Sub P(ByVal x As Variant)", "    Trace = Trace & CStr(x) & \" | \"", "End Sub",
            "Private Function Shape(ByVal x As Variant) As String",
            "    Dim n As Long, l As Long, u As Long, s As String",
            "    On Error Resume Next",
            "    For n = 1 To 60",
            "        Err.Clear: l = LBound(x, n): If Err.Number <> 0 Then Exit For",
            "        u = UBound(x, n)",
            "        If n > 1 Then s = s & \", \"",
            "        s = s & l & \" To \" & u",
            "    Next",
            "    If n = 1 Then s = \"erased\" Else s = \"(\" & s & \")\"",
            "    If IsArray(x) Then Shape = TypeName(x) & \" \" & s Else Shape = TypeName(x)",
            "End Function",
            "Private Function First(ByVal x As Variant) As String",
            "    On Error Resume Next",
            "    First = \"[1]=?\": First = \"[1]=\" & x(1)",
            "End Function",
            "Private Sub Fresh()", "    Erase a: ReDim a(3): a(1) = 7", "End Sub",
        ];
        List<string> main = ["Public Sub Main()"];
        for (var index = 0; index < Bounds.Length; index++)
        {
            var bounds = Bounds[index];
            if (Parameters.TryGetValue(kind, out var parameter))
            {
                lines.AddRange([$"Private Sub G{index}({parameter})", "    On Error Resume Next", $"    ReDim v({bounds})",
                    "    P \"G \" & Err.Number & \" \" & Shape(v) & \" \" & First(v)", "End Sub"]);
            }

            lines.AddRange([$"Private Sub Case{index}()", "    Dim w As Variant", "    On Error Resume Next", "    Fresh"]);
            lines.AddRange(kind switch
            {
                "VW" => ["    w = a", $"    G{index} w"],
                "W" => ["    w = a", $"    ReDim w({bounds})", "    P \"R \" & Err.Number & \" \" & Shape(w) & \" \" & First(w)"],
                _ => [$"    G{index} a"],
            });
            lines.Add("    P \"a \" & Shape(a) & \" \" & First(a)");
            if (kind is "VW" or "W")
            {
                lines.Add("    P \"w \" & Shape(w) & \" \" & First(w)");
            }

            lines.Add("End Sub");
            main.AddRange(["    Trace = \"\"", $"    Case{index}", "    Debug.Print Trace"]);
        }

        main.Add("End Sub");
        return string.Join("\r\n", [.. lines, .. main, string.Empty]);
    }

    // V and VV: a Variant parameter given the array is a Variant, whose ReDim one below raises 9 and keeps the array, and further below raises 7 and erases it.
    // BV: a ByVal Variant holds a copy, which is a Variant's. LA: a parameter declared as an array is the array's, which raises 9 and is erased. VW and W: a
    // Variant that holds a copy, given to a Variant parameter or re-dimensioned itself. And whichever it is, an array too big to hold raises 7 and is let go of.
    [TestMethod]
    [DataRow("V", 1, DisplayName = "V01-V10 an untyped parameter given the array")]
    [DataRow("VV", 11, DisplayName = "VV11-VV20 a ByRef v As Variant given the array")]
    [DataRow("BV", 21, DisplayName = "BV21-BV30 a ByVal v given the array")]
    [DataRow("LA", 31, DisplayName = "LA31-LA40 a v() As Long given the array")]
    [DataRow("VW", 41, DisplayName = "VW41-VW50 an untyped parameter given a Variant that holds a copy")]
    [DataRow("W", 51, DisplayName = "W51-W60 a Variant that holds a copy")]
    public async Task AReDimOfAParameterOrAVariantThatSharesOrCopiesADeclaredArray_IsMSVBAs(string kind, int first)
    {
        var printed = (await RunAsync([], Program(kind))).TakeLast(Bounds.Length).Select(line => line.TrimEnd()).ToArray();

        var expected = Bounds.Select((bounds, index) => $"{kind}{first + index:00} ({bounds}): {MsVba[$"{kind}{first + index:00}"]}").ToArray();
        CollectionAssert.AreEqual(expected, printed.Select((line, index) => $"{kind}{first + index:00} ({Bounds[index]}): {line}").ToArray());
    }
}
