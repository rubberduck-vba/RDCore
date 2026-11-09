using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Date</c> whose eight bytes are no day from January 1, 100 to December 31, 9999 (<strong>MS-VBAL §2.1</strong>), which only its bytes can make: an
/// <c>LSet</c> of a record over another (<strong>§5.4.3.6</strong>), a <c>Get</c>. It is a <c>Date</c> still, read as the <c>Double</c> it holds. What reads it
/// as a day raises run-time error 5, and arithmetic that gives a <c>Date</c> outside the range raises error 6, as MS-VBA does (recorded in Excel).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 2.1 Data Values")]
[TestCategory("MS-VBAL 5.5.1.2.3 Let-coercion to and from Date")]
public sealed class UnrepresentableDateTests
{
    private const string Types =
        "Private Type TD\r\n    D As Double\r\nEnd Type\r\n" +
        "Private Type TL\r\n    Lo As Long\r\n    Hi As Long\r\nEnd Type\r\n" +
        "Private Type TT\r\n    T As Date\r\nEnd Type\r\n";

    // every statement after `fill` reports the error it raised, if any: what MS-VBA raises is the point of each line.
    private static string Program(string fill, params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + Types +
           "Public Sub Main()\r\n    Dim X As TD, L As TL, Y As TT, D2 As Date, V As Variant, S As String\r\n" +
           $"    {fill}\r\n    On Error Resume Next\r\n" +
           string.Join("\r\n", body.Select(line => "    " + line)) + "\r\nEnd Sub\r\n";

    private static readonly string[] Reads =
    [
        "Debug.Print Y.T",
        "Debug.Print \"print \" & Err.Number: Err.Clear",
        "S = \"at \" & Y.T",
        "Debug.Print \"amp \" & Err.Number: Err.Clear",
        "S = CStr(Y.T)",
        "Debug.Print \"cstr \" & Err.Number: Err.Clear",
        "Debug.Print \"cdbl \" & CDbl(Y.T)",
        "D2 = Y.T",
        "Debug.Print \"copy \" & CDbl(D2) & \" \" & Err.Number: Err.Clear",
        "V = Y.T",
        "Debug.Print \"vt \" & VarType(V) & \" \" & TypeName(Y.T)",
        "S = Year(Y.T)",
        "Debug.Print \"year \" & Err.Number: Err.Clear",
        "S = Format$(Y.T, \"yyyy\")",
        "Debug.Print \"fmt \" & Err.Number: Err.Clear",
        "Debug.Print \"isdate \" & IsDate(Y.T)",
        "D2 = Y.T + 1",
        "Debug.Print \"plus \" & Err.Number: Err.Clear",
        "Debug.Print \"cmp \" & (Y.T > #1/1/2000#)",
    ];

    private static async Task AssertInBothEnginesAsync(string program, params string[] expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var output = await RunAsync([], program, engine);
            CollectionAssert.AreEqual(expected, output, $"{engine}: {string.Join(" | ", output)}");
        }
    }

    // each place that reads the Date as a day (Print, &, CStr, Year, Format) raises error 5 itself: none lets .NET's "Not a legal OleAut date" reach the host.
    [TestMethod]
    [DataRow("X.D = 1E+300", "1E+300", "True", "5", DisplayName = "Far past the last day")]
    [DataRow("X.D = -1E+300", "-1E+300", "False", "5", DisplayName = "Far before the first day")]
    [DataRow("X.D = 2958466", "2958466", "True", "5", DisplayName = "The day after December 31, 9999")]
    [DataRow("X.D = -657435", "-657435", "False", "0", DisplayName = "The day before January 1, 100")]
    public async Task ADateNoDayIs_IsError5WhereItIsReadAsADay_AndTheDoubleItHoldsElsewhere(string fill, string serial, string later, string plus)
        => await AssertInBothEnginesAsync(Program($"{fill}: LSet Y = X", Reads),
            "print 5", "amp 5", "cstr 5", $"cdbl {serial}", $"copy {serial} 0", "vt 7 Date", "year 5", "fmt 5", "isdate True",
            // the day before the first, plus one, is the first day: only the day after the last stays outside the range.
            $"plus {(plus == "0" ? "0" : "6")}", $"cmp {later}");

    [TestMethod]
    public async Task AnInfiniteDate_IsError5WhereItIsReadAsADay()
        => await AssertInBothEnginesAsync(Program("L.Hi = &H7FF00000: LSet Y = L",
            "Debug.Print Y.T", "Debug.Print \"print \" & Err.Number: Err.Clear", "S = CStr(Y.T)", "Debug.Print \"cstr \" & Err.Number: Err.Clear"),
            "print 5", "cstr 5");

    [TestMethod]
    public async Task TheLastDay_IsADay()
        => await AssertInBothEnginesAsync(Program("X.D = 2958465.99998843: LSet Y = X",
            "S = CStr(Y.T)", "Debug.Print \"cstr \" & Err.Number: Err.Clear", "Debug.Print \"year \" & Year(Y.T)"),
            "cstr 0", "year 9999");

    [TestMethod]
    public async Task ADateNoDayIs_InAnErrorHandler_IsError5()
        => await AssertInBothEnginesAsync(
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + Types +
            "Public Sub Main()\r\n    Dim X As TD, Y As TT\r\n    X.D = 1E+300\r\n    LSet Y = X\r\n    On Error GoTo Handler\r\n" +
            "    Debug.Print Y.T\r\n    Exit Sub\r\nHandler:\r\n    Debug.Print \"handled \" & Err.Number\r\nEnd Sub\r\n",
            "handled 5");
}
