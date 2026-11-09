using RDCore.Runtime.Execution;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.10</strong> The <c>Math</c> module, run through the whole platform: the language server's composition, the host's session, the
/// module's code.
/// </summary>
/// <remarks>
/// Each expected value is what MS-VBA printed for the same expression, in Excel (64-bit, en-US): the specification leaves the type of most results, the
/// errors, and the whole of <c>Rnd</c> and <c>Randomize</c> to the implementation, and it is what MS-VBA does that RD-VBA does. A row is the expression and
/// what it gave: its <c>TypeName</c> and its value (a <c>Date</c> as its serial number), or the error it raised.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.10 Math")]
public sealed class MathFunctionTests
{
    private static Task AssertRows(params string[] rows) => ValueRows.AssertRows(rows);

    [TestMethod]
    public Task Abs()
        => AssertRows(
            "Abs(Null) => Null Null",
            "Abs(Empty) => Integer 0",
            "Abs(CInt(-5)) => Integer 5",
            "Abs(CLng(-5)) => Long 5",
            "Abs(CByte(5)) => Byte 5",
            "Abs(CLngLng(-5)) => LongLong 5",
            "Abs(CCur(-2.5)) => Currency 2.5",
            "Abs(CDec(-2.5)) => Decimal 2.5",
            "Abs(CSng(-2.5)) => Single 2.5",
            "Abs(-2.5) => Double 2.5",
            "Abs(CDate(-2.5)) => Date 2.5",
            "Abs(CDate(-1)) => Date 1",
            "Abs(True) => Integer 1",
            "Abs(False) => Integer 0",
            "Abs(\"-3\") => Double 3",
            "Abs(\"-3.5\") => Double 3.5",
            "Abs(\"abc\") => err 13",
            "Abs(vbNullString) => err 13",
            "Abs(CVErr(5)) => err 13",
            "Abs(Array(1)) => err 13",
            "Abs(CInt(-32767) - 1) => err 6",
            "Abs(CLngLng(\"-9223372036854775807\") - 1) => err 6",
            "Abs(CCur(\"-922337203685477.5808\")) => err 6",
            "Abs(CDec(\"-79228162514264337593543950335\")) => Decimal 79228162514264337593543950335",
            "Abs(CSng(-3.402823E+38)) => Single 3.402823E+38");

    [TestMethod]
    // MS-VBA answers the smallest Long with itself, a negative absolute value, where every other type overflows (RD-VBAL 6.1.2.10).
    public Task Abs_OfTheSmallestLong_IsItself()
        => AssertRows("Abs(CLng(-2147483647) - 1) => Long -2147483648");

    [TestMethod]
    public Task Sgn()
        => AssertRows(
            "Sgn(Null) => err 94",
            "Sgn(Empty) => Integer 0",
            "Sgn(CInt(-5)) => Integer -1",
            "Sgn(0) => Integer 0",
            "Sgn(2.5) => Integer 1",
            "Sgn(CCur(-1)) => Integer -1",
            "Sgn(CCur(0)) => Integer 0",
            "Sgn(CDec(3)) => Integer 1",
            "Sgn(CLngLng(-5)) => Integer -1",
            "Sgn(True) => Integer -1",
            "Sgn(\"-3\") => Integer -1",
            "Sgn(\"abc\") => err 13",
            "Sgn(CDate(1)) => Integer 1",
            "Sgn(CDate(-1)) => Integer -1",
            "Sgn(CByte(0)) => Integer 0",
            "Sgn(CSng(-0.1)) => Integer -1",
            "Sgn(-0#) => Integer 0",
            "Sgn(CVErr(5)) => err 13");

    [TestMethod]
    public Task TrigonometricFunctions()
        => AssertRows(
            "Atn(1) => Double 0.785398163397448",
            "Atn(Null) => err 94",
            "Atn(Empty) => Double 0",
            "Atn(\"1\") => Double 0.785398163397448",
            "Atn(True) => Double -0.785398163397448",
            "Atn(1E+300) => Double 1.5707963267949",
            "Atn(-1E+300) => Double -1.5707963267949",
            "Cos(0) => Double 1",
            "Sin(1) => Double 0.841470984807897",
            "Tan(1) => Double 1.5574077246549",
            // an angle of 2^63 radians or more is refused.
            "Cos(9.2E+18) => Double " + System.Math.Cos(9.2E+18).ToString("G15", System.Globalization.CultureInfo.InvariantCulture),
            "Cos(1E+19) => err 5",
            "Sin(-1E+19) => err 5",
            "Tan(1E+19) => err 5",
            "Sin(1E+300) => err 5",
            "Cos(1E+300) => err 5",
            "Tan(1E+300) => err 5");

    [TestMethod]
    public Task ExpLogAndSqr()
        => AssertRows(
            "Exp(1) => Double 2.71828182845905",
            "Exp(709.782712893) => Double 1.7976931341721E+308",
            "Exp(709.782712893384) => Double 1.79769313486227E+308",
            "Exp(709.79) => err 6",
            "Exp(-1000) => Double 0",
            "Exp(\"x\") => err 13",
            "Exp(CCur(1)) => Double 2.71828182845905",
            "Log(0) => err 5",
            "Log(-1) => err 5",
            "Log(1) => Double 0",
            "Log(Exp(2)) => Double 2",
            "Log(1E-320) => Double -736.827240890974",
            "Sqr(-1) => err 5",
            "Sqr(0) => Double 0",
            "Sqr(4) => Double 2",
            "Sqr(Null) => err 94",
            "Sqr(Empty) => Double 0",
            "Sqr(\"4\") => Double 2",
            "Sqr(CInt(4)) => Double 2");

    [TestMethod]
    public Task Round_OfADouble()
        => AssertRows(
            "Round(2.5) => Double 2",
            "Round(3.5) => Double 4",
            "Round(-2.5) => Double -2",
            "Round(0.5) => Double 0",
            "Round(1.5) => Double 2",
            "Round(-0.5) => Double -0",
            "Round(-0.4) => Double 0",
            // a tie is a tie as the Double scaled to the digits is.
            "Round(2.675, 2) => Double 2.68",
            "Round(0.285, 2) => Double 0.28",
            "Round(1.005, 2) => Double 1",
            "Round(2.345, 2) => Double 2.35",
            "Round(0.125, 2) => Double 0.12",
            "Round(0.375, 2) => Double 0.38",
            "Round(1.15, 1) => Double 1.2",
            "Round(1.25, 1) => Double 1.2",
            "Round(12345.6789, 3) => Double 12345.679",
            "Round(123456789012345.67, 1) => Double 123456789012346",
            "Round(1E+20, 2) => Double 1E+20",
            "Round(1E+300, 22) => Double 1E+300",
            "Round(-1E+300, 5) => Double -1E+300",
            "Round(1.7976931348623E+308) => Double 1.7976931348623E+308",
            "Round(1.23456, 22) => Double 1.23456",
            "Round(1.23456, 23) => err 5",
            "Round(1E-20, 25) => err 5",
            "Round(1.5, 400) => err 5",
            "Round(1.23456, -1) => err 5",
            "Round(2.5, 2147483647) => err 5",
            "Round(2.5, -2147483647 - 1) => err 5");

    [TestMethod]
    public Task Round_OfAnyOtherValue()
        => AssertRows(
            "Round(CCur(2.5)) => Currency 2",
            "Round(CCur(-2.5)) => Currency -2",
            "Round(CCur(1.23456), 2) => Currency 1.23",
            "Round(CCur(1.23456), 5) => Currency 1.2346",
            "Round(CCur(2.345), 2) => Currency 2.34",
            "Round(CCur(1.5), 100) => Currency 1.5",
            "Round(CCur(922337203685477.5807)) => err 6",
            "Round(CDec(\"2.5\")) => Decimal 2",
            "Round(CDec(\"-2.5\")) => Decimal -2",
            "Round(CDec(\"1.23456789\"), 3) => Decimal 1.235",
            "Round(CDec(\"2.675\"), 2) => Decimal 2.68",
            "Round(CDec(\"1.23456\"), 2147483647) => Decimal 1.23456",
            "Round(CInt(5)) => Integer 5",
            "Round(CInt(5), 2147483647) => Integer 5",
            "Round(CLng(7)) => Long 7",
            "Round(CByte(250), 1) => Byte 250",
            "Round(CLngLng(5), 2) => LongLong 5",
            "Round(CSng(2.5)) => Single 2",
            "Round(CSng(1.2345), 2) => Single 1.23",
            "Round(CSng(0.125), 2) => Single 0.12",
            "Round(CSng(2.345), 2) => Single 2.35",
            "Round(CSng(1.5), 23) => err 5",
            "Round(CDate(2.5)) => Date 2",
            "Round(CDate(2.75), 1) => Date 2.8",
            "Round(CDate(-2.75), 1) => Date -2.8",
            "Round(CDate(1), 23) => err 5",
            "Round(Null) => Null Null",
            "Round(Null, 23) => Null Null",
            "Round(Null, -1) => err 5",
            "Round(Empty) => Integer 0",
            "Round(Empty, -1) => err 5",
            "Round(True) => Integer -1",
            "Round(True, 23) => Integer -1",
            "Round(\"2.5\") => Double 2",
            "Round(\"2.675\", 2) => Double 2.68",
            "Round(\"2.5\", 23) => err 5",
            "Round(\"abc\") => err 13",
            "Round(CVErr(5)) => err 13",
            "Round(2.5, Null) => err 94",
            "Round(2.5, Empty) => Double 2",
            "Round(2.567, \"1\") => Double 2.6",
            "Round(2.567, 1.6) => Double 2.57");

    [TestMethod]
    public async Task Rnd_IsMSVBAsGenerator()
    {
        // each number as the 24-bit seed it is, which a Single holds exactly.
        string[] expected =
        [
            // a session no program has seeded starts where a new MS-VBA process does.
            "11837123", "8949370",
            // zero repeats the most recent number; any other positive number, or none, generates the next one.
            "8949370", "9722709",
            // a negative number seeds the generator with itself, the same number every time.
            "3758214", "601393", "1448752", "3758214",
            "5676163", "7499666", "8027627", "3779446",
            // a Boolean, or a string, is Let-coerced to Single.
            "3758214", "3758214",
        ];

        var printed = await RunAsync([], """
            Attribute VB_Name = "Program"
            Option Explicit
            Private Sub Show(ByVal x As Single)
                Debug.Print CDbl(x) * 16777216#
            End Sub
            Public Sub Main()
                Show Rnd
                Show Rnd
                Show Rnd(0)
                Show Rnd(5)
                Show Rnd(-1)
                Show Rnd
                Show Rnd
                Show Rnd(-1)
                Show Rnd(-2.5)
                Show Rnd(-1E+30)
                Show Rnd(-0.0000001)
                Show Rnd(-123456789)
                Show Rnd(True)
                Show Rnd("-1")
            End Sub
            """.ReplaceLineEndings("\r\n"));

        CollectionAssert.AreEqual(expected, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public void AnEnd_ResetsTheGenerator_SoThatTheNextProgramGeneratesTheSequenceOfANewSession()
    {
        var output = new RuntimeOutputBuffer();
        var (session, outcome) = RuntimeSourceHarness.Run(null, [], output, standardLibrary: true, "Debug.Print Rnd(-1)", "Randomize 5", "Debug.Print Rnd");
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);

        SessionWipe.End(session);

        var first = new StdMath(session).Rnd();
        Assert.IsTrue(first.IsSuccess, first.ErrorInfo?.Verbose);
        Assert.AreEqual(11837123d, (double)first.Result!.Value * 16777216d);
    }

    [TestMethod]
    public async Task Randomize_SeedsTheGeneratorAsMSVBADoes()
    {
        string[] expected =
        [
            "1 5596465", "0.5 1414449", "-3.25 12392241", "1E+300 7541809", "0 5584177", "123456789 11352625",
            // a seed is the same seed whatever its type, and an Empty is 0: MS-VBA uses the timer only when the argument is missing.
            "CSng 5596465", "CInt 5596465", "String 5596465", "True 13985073", "Empty 5584177",
            // Randomize keeps the low byte of the seed it replaces.
            "twice 5596465", "after Rnd(-2) 6134074",
            "Rnd(0) 4194438",
            "Null 94", "x 13", "Rnd(Null) 94", "Rnd(x) 13", "Rnd(CVErr) 13", "Rnd(1E+300) 6", "Rnd(Empty) 4194438",
        ];

        var printed = await RunAsync([], """
            Attribute VB_Name = "Program"
            Option Explicit
            Private Function Seed() As String
                Seed = CStr(CDbl(Rnd) * 16777216#)
            End Function
            Public Sub Main()
                Dim x As Single
                x = Rnd(-1): Randomize 1: Debug.Print "1 " & Seed
                x = Rnd(-1): Randomize 0.5: Debug.Print "0.5 " & Seed
                x = Rnd(-1): Randomize -3.25: Debug.Print "-3.25 " & Seed
                x = Rnd(-1): Randomize 1E+300: Debug.Print "1E+300 " & Seed
                x = Rnd(-1): Randomize 0: Debug.Print "0 " & Seed
                x = Rnd(-1): Randomize 123456789: Debug.Print "123456789 " & Seed
                x = Rnd(-1): Randomize CSng(1): Debug.Print "CSng " & Seed
                x = Rnd(-1): Randomize CInt(1): Debug.Print "CInt " & Seed
                x = Rnd(-1): Randomize "1": Debug.Print "String " & Seed
                x = Rnd(-1): Randomize True: Debug.Print "True " & Seed
                x = Rnd(-1): Randomize Empty: Debug.Print "Empty " & Seed
                x = Rnd(-1): Randomize 1: Randomize 1: Debug.Print "twice " & Seed
                x = Rnd(-2): Randomize 1: Debug.Print "after Rnd(-2) " & Seed
                x = Rnd(-1): Randomize 2: Debug.Print "Rnd(0) " & CDbl(Rnd(0)) * 16777216#
                On Error Resume Next
                Err.Clear: Randomize Null: Debug.Print "Null " & Err.Number
                Err.Clear: Randomize "x": Debug.Print "x " & Err.Number
                Err.Clear: x = Rnd(Null): Debug.Print "Rnd(Null) " & Err.Number
                Err.Clear: x = Rnd("x"): Debug.Print "Rnd(x) " & Err.Number
                Err.Clear: x = Rnd(CVErr(5)): Debug.Print "Rnd(CVErr) " & Err.Number
                Err.Clear: x = Rnd(1E+300): Debug.Print "Rnd(1E+300) " & Err.Number
                Err.Clear: x = Rnd(Empty): Debug.Print "Rnd(Empty) " & CDbl(x) * 16777216#
            End Sub
            """.ReplaceLineEndings("\r\n"));

        CollectionAssert.AreEqual(expected, printed, string.Join(" | ", printed));
    }
}
