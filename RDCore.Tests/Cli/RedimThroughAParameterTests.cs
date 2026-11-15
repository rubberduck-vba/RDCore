using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>ReDim</c> of a parameter that shares a declared dynamic array, and of a Variant that holds a copy of one (<strong>MS-VBAL §5.4.3.3</strong>, RD-VBAL
/// 5.4.3.3): an upper bound below its lower, and an array too big to hold.
/// </summary>
/// <remarks>
/// Each expected line is what MS-VBA printed for the same code in Excel (64-bit, en-US): the shape of what the ReDim was given, and then of
/// the caller's array, each <c>TypeName (bounds) [1]=element</c>, <c>erased</c> for an array with no dimensions, and <c>?</c> for an element that can't be read.
/// The caller's array is <c>a() As Long</c>, <c>(0 To 3)</c> with <c>a(1) = 7</c>, before each.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim Statement")]
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

    // the module's array, the trace, and what reads it.
    private static readonly string[] Helpers =
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

    private static string Program(string kind)
    {
        List<string> lines = [.. Helpers];
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

    // G and GP re-dimension a Variant parameter, GLAP a v() As Long, with Preserve or without, to the bounds they are given; G2 passes its own
    // Variant parameter on to G.
    private static readonly string[] Parameters68b =
        [
            "Private Sub G(v, ByVal l As Long, ByVal u As Long)", "    On Error Resume Next", "    ReDim v(l To u)",
            "    P \"G \" & Err.Number & \" \" & Shape(v) & \" \" & First(v)", "End Sub",
            "Private Sub GP(v, ByVal l As Long, ByVal u As Long)", "    On Error Resume Next", "    ReDim Preserve v(l To u)",
            "    P \"G \" & Err.Number & \" \" & Shape(v) & \" \" & First(v)", "End Sub",
            "Private Sub GLAP(v() As Long, ByVal l As Long, ByVal u As Long)", "    On Error Resume Next", "    ReDim Preserve v(l To u)",
            "    P \"G \" & Err.Number & \" \" & Shape(v) & \" \" & First(v)", "End Sub",
            "Private Sub G2(v, ByVal l As Long, ByVal u As Long)", "    G v, l, u", "    P \"G2 \" & Shape(v) & \" \" & First(v)", "End Sub",
        ];

    private const string TracesTheError = "    P \"R \" & Err.Number";
    private const string TracesA = "    P \"a \" & Shape(a) & \" \" & First(a)";
    private const string TracesW = "    P \"w \" & Shape(w) & \" \" & First(w)";
    private const string TracesB = "    P \"b \" & Shape(b) & \" \" & First(b)";
    private const string ALocal = "    ReDim b(3): b(1) = 7";

    // each case, the body of its Case sub, which has a Variant w and a local b() As Long. T02, every Long subscript of Variants, is not
    // here: it is a deliberate divergence, as MS-VBA accepts the bounds and holds no elements, and ReDimSizeLimitTests has RD-VBA's error 7.
    private static readonly Dictionary<string, string[]> Cases68b = new()
    {
        ["P01"] = ["    Fresh", "    ReDim Preserve a(2147483647)", TracesTheError, TracesA],
        ["P02"] = ["    Fresh", "    GP a, 0, 2147483647", TracesA],
        ["P03"] = ["    Fresh", "    GLAP a, 0, 2147483647", TracesA],
        ["P04"] = ["    Fresh", "    w = a", "    ReDim Preserve w(2147483647)", TracesTheError, TracesA, TracesW],
        ["P05"] = ["    Fresh", "    w = a", "    GP w, 0, 2147483647", TracesA, TracesW],
        ["T01"] = ["    Dim i() As Integer", "    ReDim i(2): i(2) = 3", "    ReDim i(65535, 65535, 65535)", TracesTheError, "    P \"i \" & Shape(i) & \" \" & First(i)"],
        ["T03"] = ["    ReDim w(1) As Integer", "    w(1) = 3", "    ReDim w(2147483647)", TracesTheError, TracesW],
        ["T04"] = [ALocal, "    ReDim b(2147483647)", TracesTheError, TracesB],
        ["T05"] = ["    Fresh", "    ReDim a(2147483647)", TracesTheError, TracesA],
        ["L01"] = [ALocal, "    G b, 5, 2", TracesB],
        ["L02"] = [ALocal, "    G b, 5, 4", TracesB],
        ["L03"] = [ALocal, "    G b, 0, 2147483647", TracesB],
        ["L04"] = [ALocal, "    G b, 0, 5", TracesB],
        ["L05"] = ["    Fresh", "    G2 a, 5, 2", TracesA],
        ["L06"] = ["    Fresh", "    G2 a, 5, 4", TracesA],
        ["L07"] = ["    Fresh", "    G2 a, 0, 2147483647", TracesA],
        ["L08"] = [ALocal, "    G2 b, 5, 2", TracesB],
        ["L09"] = ["    Fresh", "    G2 a, 0, 5", TracesA],
    };

    private static readonly Dictionary<string, string> MsVba68b = new()
    {
        ["P01"] = "R 7 | a Long() (0 To 3) [1]=7 |",
        ["P02"] = "G 7 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["P03"] = "G 7 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["P04"] = "R 7 | a Long() (0 To 3) [1]=7 | w Long() (0 To 3) [1]=7 |",
        ["P05"] = "G 7 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 | w Long() (0 To 3) [1]=7 |",
        ["T01"] = "R 7 | i Integer() erased [1]=? |",
        ["T03"] = "R 7 | w Empty [1]=? |",
        ["T04"] = "R 7 | b Long() erased [1]=? |",
        ["T05"] = "R 7 | a Long() erased [1]=? |",
        ["L01"] = "G 7 Long() erased [1]=? | b Long() erased [1]=? |",
        ["L02"] = "G 9 Long() (0 To 3) [1]=7 | b Long() (0 To 3) [1]=7 |",
        ["L03"] = "G 7 Long() erased [1]=? | b Long() erased [1]=? |",
        ["L04"] = "G 0 Long() (0 To 5) [1]=0 | b Long() (0 To 5) [1]=0 |",
        ["L05"] = "G 7 Long() erased [1]=? | G2 Long() erased [1]=? | a Long() erased [1]=? |",
        ["L06"] = "G 9 Long() (0 To 3) [1]=7 | G2 Long() (0 To 3) [1]=7 | a Long() (0 To 3) [1]=7 |",
        ["L07"] = "G 7 Long() erased [1]=? | G2 Long() erased [1]=? | a Long() erased [1]=? |",
        ["L08"] = "G 7 Long() erased [1]=? | G2 Long() erased [1]=? | b Long() erased [1]=? |",
        ["L09"] = "G 0 Long() (0 To 5) [1]=0 | G2 Long() (0 To 5) [1]=0 | a Long() (0 To 5) [1]=0 |",
    };

    private static string Program68b(string[] cases)
    {
        List<string> lines = [.. Helpers, .. Parameters68b];
        List<string> main = ["Public Sub Main()"];
        foreach (var id in cases)
        {
            lines.AddRange([$"Private Sub Case{id}()", "    Dim w As Variant, b() As Long", "    On Error Resume Next", .. Cases68b[id], "End Sub"]);
            main.AddRange(["    Trace = \"\"", $"    Case{id}", "    Debug.Print Trace"]);
        }

        main.Add("End Sub");
        return string.Join("\r\n", [.. lines, .. main, string.Empty]);
    }

    // P: a ReDim Preserve too big raises 7 and keeps the array, a Variant's copy too, through a parameter or not. T: a plain ReDim too big lets go of what the
    // variable held, an element count past what a Long holds too, and a local array re-dimensioned in its own procedure. L: a caller's local array is shared
    // as the module's is, and a Variant parameter passed on to another is still the caller's array.
    [TestMethod]
    [DataRow(new[] { "P01", "P02", "P03", "P04", "P05" }, DisplayName = "P01-P05 a ReDim Preserve too big")]
    [DataRow(new[] { "T01", "T03", "T04", "T05" }, DisplayName = "T01, T03-T05 a ReDim too big, of other shapes and targets")]
    [DataRow(new[] { "L01", "L02", "L03", "L04" }, DisplayName = "L01-L04 a Variant parameter given a local array")]
    [DataRow(new[] { "L05", "L06", "L07", "L08", "L09" }, DisplayName = "L05-L09 a Variant parameter passed on to another")]
    public async Task AReDimTooBigOrThroughALocalOrTwoCalls_IsMSVBAs(string[] cases)
    {
        var printed = (await RunAsync([], Program68b(cases))).TakeLast(cases.Length).Select(line => line.TrimEnd());

        Assert.AreEqual(
            string.Join("\n", cases.Select(id => $"{id}: {MsVba68b[id]}")),
            string.Join("\n", cases.Zip(printed, (id, line) => $"{id}: {line}")));
    }
}
