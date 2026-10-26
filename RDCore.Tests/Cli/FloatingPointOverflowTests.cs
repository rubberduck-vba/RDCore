using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.6.9.3: a <c>Single</c> or <c>Double</c> operation of finite operands whose result is not finite overflows, unless an operand is a <c>Variant</c>
/// and a <c>Double</c> holds the result; <c>^</c> raises 5 for a power that is no real number. Both ways: the payload evaluation declines what the boxed one
/// raises (the suite is also run with <c>RDCORE_UNBOXED=0</c>). Every expected line was recorded in Excel (grid <c>oracle\s16</c> D01-D10, E01-E04).
/// </summary>
[TestClass]
public sealed class FloatingPointOverflowTests
{
    private static string Run(string body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n" + string.Join("\r\n", body.Split(" ;; ")) + "\r\nEnd Sub\r\n";
        return string.Join("/", RunAsync([], program).GetAwaiter().GetResult());
    }

    [TestMethod]
    [DataRow("Dim s As Single ;; s = 3E+38 ;; s = s * 10 ;; Debug.Print Err.Number", "6", DisplayName = "D01 a Single product")]
    [DataRow("Dim s As Single, d As Double ;; d = 1E+39 ;; s = d ;; Debug.Print Err.Number", "6", DisplayName = "D02 a Double past a Single, assigned to one")]
    [DataRow("Dim d As Double ;; d = 1E+308 ;; d = d * 10 ;; Debug.Print Err.Number", "6", DisplayName = "D03 a Double product")]
    [DataRow("Dim s As Single ;; s = 3E+38 ;; s = s + s ;; Debug.Print Err.Number", "6", DisplayName = "D04 a Single sum")]
    [DataRow("Dim s As Single ;; s = -3E+38 ;; s = s * 10 ;; Debug.Print Err.Number", "6", DisplayName = "D07 a negative Single product")]
    [DataRow("Dim s As Single ;; s = 1 ;; s = s / 0 ;; Debug.Print Err.Number", "11", DisplayName = "D08 a Single divided by zero")]
    [DataRow("Dim s As Single, i As Long ;; s = 3E+38 ;; i = 2 ;; s = s * i ;; Debug.Print Err.Number", "6", DisplayName = "D09 a Single by a Long")]
    [DataRow("Dim s As Single ;; s = 3E+38 ;; Debug.Print s * 10 ;; Debug.Print Err.Number", "6", DisplayName = "D10 not assigned: nothing printed")]
    [DataRow("Dim d As Double ;; d = 1E+308 ;; d = d + d ;; Debug.Print Err.Number", "6", DisplayName = "E05 a Double sum")]
    [DataRow("Dim d As Double ;; d = 1E+308 ;; d = d / 0.1 ;; Debug.Print Err.Number", "6", DisplayName = "E06 a Double quotient")]
    public void AFloatingPointResultThatIsNotFinite_Overflows(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    [TestMethod]
    public void AVariantSingleProductPastASingle_IsADouble()
        => Assert.AreEqual("0|Double|3.00000000549776E+39", Run("Dim v As Variant ;; v = CSng(3E+38) ;; v = v * 10 ;; Debug.Print Err.Number & \"|\" & TypeName(v) & \"|\" & v"));

    [TestMethod]
    [DataRow("Dim d As Double ;; d = 10 ;; d = d ^ 400 ;; Debug.Print Err.Number & \"|\" & d", "6|10", DisplayName = "E01 past a Double, the variable unchanged")]
    [DataRow("Dim d As Double ;; d = -8 ;; d = d ^ (1 / 3) ;; Debug.Print Err.Number & \"|\" & d", "5|-8", DisplayName = "E02 a negative number to a fraction")]
    [DataRow("Dim s As Single ;; s = 10 ;; s = s ^ 50 ;; Debug.Print Err.Number", "6", DisplayName = "E03 a Double power past a Single, assigned to one")]
    [DataRow("Dim v As Variant ;; v = 10 ;; v = v ^ 400 ;; Debug.Print Err.Number & \"|\" & TypeName(v)", "6|Integer", DisplayName = "E04 a Variant: nothing wider than Double")]
    [DataRow("Dim d As Double ;; d = 0 ;; d = d ^ 0 ;; Debug.Print Err.Number & \"|\" & d", "0|1", DisplayName = "0 ^ 0 is 1")]
    public void APowerThatIsNoFiniteNumber_RaisesAnError(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    // MS-VBA assigns the IEEE result of +, -, * and / - and of a Double let-coerced to a Single - to the Single or Double variable it is assigned to,
    // before it raises the error; ^ leaves it unchanged (E01-E03), and so does an assignment to a variable of any other type (F01-F15).
    [TestMethod]
    [DataRow("Dim s As Single ;; s = 3E+38 ;; s = s * 10 ;; Debug.Print Err.Number & \"|\" & s", "6|inf", DisplayName = "D01 a Single product")]
    [DataRow("Dim s As Single, d As Double ;; d = 1E+39 ;; s = d ;; Debug.Print Err.Number & \"|\" & s", "6|inf", DisplayName = "D02 a Double past a Single, assigned to one")]
    [DataRow("Dim d As Double ;; d = 1E+308 ;; d = d * 10 ;; Debug.Print Err.Number & \"|\" & d", "6|inf", DisplayName = "D03 a Double product")]
    [DataRow("Dim s As Single, t As Single ;; s = 3E+38 ;; t = s * 10 ;; Debug.Print Err.Number & \"|\" & t", "6|inf", DisplayName = "D06 another variable")]
    [DataRow("Dim s As Single ;; s = -3E+38 ;; s = s * 10 ;; Debug.Print Err.Number & \"|\" & s", "6|-inf", DisplayName = "D07 a negative Single product")]
    [DataRow("Dim s As Single ;; s = 1 ;; s = s / 0 ;; Debug.Print Err.Number & \"|\" & s", "11|inf", DisplayName = "D08 a Single divided by zero")]
    [DataRow("Dim s As Single, t As Single ;; s = 3E+38 ;; t = s * 10 + 1 ;; Debug.Print Err.Number & \"|\" & t", "6|inf", DisplayName = "F01 an operation of the overflow")]
    [DataRow("Dim d As Double ;; d = 0 ;; d = d / 0 ;; Debug.Print Err.Number & \"|\" & d", "6|-nan(ind)", DisplayName = "F05 zero divided by zero")]
    [DataRow("Dim d As Double ;; d = -1 ;; d = d / 0 ;; Debug.Print Err.Number & \"|\" & d", "11|-inf", DisplayName = "F06 a negative number divided by zero")]
    [DataRow("Dim s As Single, d As Double ;; d = -1E+39 ;; s = d ;; Debug.Print Err.Number & \"|\" & s", "6|-inf", DisplayName = "F07 a negative Double past a Single")]
    [DataRow("Dim a(1 To 2) As Single, s As Single ;; s = 3E+38 ;; a(1) = s * 10 ;; Debug.Print Err.Number & \"|\" & a(1)", "6|inf", DisplayName = "F08 an element of an array")]
    [DataRow("Dim s As Single, d As Double ;; d = 1E+308 ;; s = d * 10 ;; Debug.Print Err.Number & \"|\" & s", "6|inf", DisplayName = "F15 a Double product, assigned to a Single")]
    public void AnOverflowingFloatingPointAssignment_StoresWhatIeeeArithmeticGave(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    [TestMethod]
    [DataRow("Dim d As Double, l As Long ;; d = 1E+308 ;; l = 5 ;; l = d * 10 ;; Debug.Print Err.Number & \"|\" & l", "6|5", DisplayName = "F03 a Long")]
    [DataRow("Dim d As Double, v As Variant ;; d = 1E+308 ;; v = 5 ;; v = d * 10 ;; Debug.Print Err.Number & \"|\" & TypeName(v) & \"|\" & v", "6|Integer|5", DisplayName = "F04 a Variant")]
    [DataRow("Dim c As Currency, d As Double ;; d = 1E+308 ;; c = 3 ;; c = d * 10 ;; Debug.Print Err.Number & \"|\" & c", "6|3", DisplayName = "F10 a Currency")]
    public void AnOverflowingFloatingPointAssignment_ToAnotherType_LeavesTheVariable(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    // an operation of an infinity is IEEE 754's, with no error - unless it is no number: infinity less infinity is an overflow (F11, F12, F19, F20).
    [TestMethod]
    [DataRow("Dim s As Single ;; s = 3E+38 ;; s = s * 10 ;; Err.Clear ;; s = s + 1 ;; Debug.Print Err.Number & \"|\" & s", "0|inf", DisplayName = "F11 infinity plus one")]
    [DataRow("Dim s As Single ;; s = 3E+38 ;; s = s * 10 ;; Err.Clear ;; s = s - s ;; Debug.Print Err.Number & \"|\" & s", "6|-nan(ind)", DisplayName = "F12 infinity less infinity")]
    [DataRow("Dim s As Single ;; s = 3E+38 ;; s = s * 10 ;; Debug.Print (s > 0) & \"|\" & (s = s) & \"|\" & Sgn(s)", "True|True|1", DisplayName = "F19 compared")]
    [DataRow("Dim s As Single, d As Double ;; s = 3E+38 ;; s = s * 10 ;; Err.Clear ;; d = s ;; Debug.Print Err.Number & \"|\" & d", "0|inf", DisplayName = "F20 a Single infinity, assigned to a Double")]
    [DataRow("Dim s As Single, l As Long ;; s = 3E+38 ;; s = s * 10 ;; Err.Clear ;; l = CLng(s) ;; Debug.Print Err.Number", "6", DisplayName = "F20 a Single infinity, converted to a Long")]
    [DataRow("Dim s As Single ;; s = 3E+38 ;; s = s * 10 ;; Debug.Print CStr(s) & \"|\" & CStr(-s) & \"|\" & Format(s)", "inf|-inf|inf", DisplayName = "F13 the text of an infinity")]
    public void AnInfinity_IsAValueOfItsType(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    // a NaN is a value of a Single, a Double and a Variant, copied and operated on with no error; compared, given a sign or made a Long, it is 6. Its text
    // shows its sign, which Abs clears (G01-G10, H06).
    private const string NaN = "Dim d As Double ;; d = 0 ;; d = d / 0 ;; Err.Clear ;; ";

    [TestMethod]
    [DataRow(NaN + "Dim e As Double ;; e = 7 ;; e = d ;; Debug.Print Err.Number & \"|\" & e", "0|-nan(ind)", DisplayName = "G01 assigned to a Double")]
    [DataRow(NaN + "Dim s As Single ;; s = 7 ;; s = d ;; Debug.Print Err.Number & \"|\" & s", "0|-nan(ind)", DisplayName = "G02 assigned to a Single")]
    [DataRow(NaN + "Dim v As Variant ;; v = 7 ;; v = d ;; Debug.Print Err.Number & \"|\" & TypeName(v) & \"|\" & v", "0|Double|-nan(ind)", DisplayName = "G03 assigned to a Variant")]
    [DataRow(NaN + "Dim e As Double ;; e = 7 ;; e = d + 1 ;; Debug.Print Err.Number & \"|\" & e", "0|-nan(ind)", DisplayName = "G04 plus one")]
    [DataRow(NaN + "Debug.Print (d = d) ;; Debug.Print Err.Number", "6", DisplayName = "G05 compared")]
    [DataRow(NaN + "Dim l As Long ;; l = 7 ;; l = d ;; Debug.Print Err.Number & \"|\" & l", "6|7", DisplayName = "G06 assigned to a Long")]
    [DataRow(NaN + "Debug.Print CStr(d) & \"|\" & Format(d)", "-nan(ind)|-nan(ind)", DisplayName = "G07 its text")]
    [DataRow(NaN + "Debug.Print Sgn(d) ;; Debug.Print Err.Number", "6", DisplayName = "G10 its sign")]
    [DataRow(NaN + "Debug.Print Abs(d) & \"|\" & Int(d) & \"|\" & Err.Number", "nan|-nan(ind)|0", DisplayName = "H06 Abs and Int")]
    public void ANaN_IsAValueOfItsType(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    // MS-VBA's Variant arithmetic: a Double that is not finite is an overflow, an infinity's included, and assigns nothing; a Single that is not finite is a
    // Double, with no error (H01-H04).
    [TestMethod]
    [DataRow("Dim s As Single, v As Variant ;; s = 3E+38 ;; s = s * 10 ;; Err.Clear ;; v = s ;; v = v + 1 ;; Debug.Print Err.Number & \"|\" & TypeName(v) & \"|\" & v", "0|Double|inf",
        DisplayName = "H01 a Single infinity plus one")]
    [DataRow("Dim d As Double, v As Variant ;; d = 1E+308 ;; d = d * 10 ;; Err.Clear ;; v = d ;; v = v * 2 ;; Debug.Print Err.Number & \"|\" & v", "6|inf",
        DisplayName = "H02 a Double infinity times two")]
    [DataRow(NaN + "Dim v As Variant, e As Double ;; v = d ;; e = v + 1 ;; Debug.Print Err.Number & \"|\" & e", "6|0", DisplayName = "H03 a NaN plus one, assigned to a Double")]
    [DataRow(NaN + "Dim v As Variant ;; v = d ;; v = -v ;; Debug.Print Err.Number", "0", DisplayName = "H04 a NaN negated")]
    public void TheVariantArithmeticOfAnInfinityOrANaN_IsMsVbas(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    // MS-VBAL §5.6.9.3 lets an implementation "perform floating point operations with a higher-precision than the effective value type (such as an
    // 'extended' or 'long double' type)", and MS-VBA does: an intermediate result past the range of a Double is no overflow until it is stored or made a
    // Double (F02, F18) - so does RD-VBA (ExtendedIntermediates; ExtendedIntermediateTests for the rest of it).
    [TestMethod]
    public void TwoIntermediateResultsPastADouble_SubtractedToZero_AreNoOverflow()
        => Assert.AreEqual("0|0", Run("Dim d As Double, e As Double ;; d = 1E+308 ;; e = (d * 10) - (d * 10) ;; Debug.Print Err.Number & \"|\" & e"));

    [TestMethod]
    public void AnIntermediateResultPastADouble_Compared_IsNoOverflow()
        => Assert.AreEqual("gt/0", Run("Dim d As Double ;; d = 1E+308 ;; If d * 10 > 0 Then Debug.Print \"gt\" ;; Debug.Print Err.Number"));
}
