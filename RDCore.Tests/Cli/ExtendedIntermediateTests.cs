using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.6.9.3: MS-VBA computes an expression of Single, Double and Date operations of typed operands in the x87's 80-bit extended format, and rounds
/// it where it is stored (ExtendedIntermediates) - in range and in precision. Both ways: on payloads, and boxed (the suite is also run with
/// <c>RDCORE_UNBOXED=0</c>). Every expected line was observed in Excel.
/// </summary>
[TestClass]
public sealed class ExtendedIntermediateTests
{
    // the program of a case: its declarations, P - which writes what it is given, as a Variant - and Bits, which writes the 8 bytes of a Double in hex.
    private static async Task<string> Run(string declarations, string body)
    {
        string[] lines =
        [
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Private Type D8", "    V As Double", "End Type",
            "Private Type L8", "    Lo As Long", "    Hi As Long", "End Type",
            "Private acc As String",
            .. (declarations.Length == 0 ? [] : declarations.Split(" ;; ")),
            "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub",
            "Private Function Bits(ByVal x As Double) As String",
            "    Dim a As D8, b As L8",
            "    a.V = x",
            "    LSet b = a",
            "    Bits = Right$(\"0000000\" & Hex$(b.Hi), 8) & Right$(\"0000000\" & Hex$(b.Lo), 8)",
            "End Function",
            "Public Sub Main()",
            "    On Error Resume Next",
            "    " + string.Join("\r\n    ", body.Split(" ;; ")),
            "    Debug.Print acc",
            "End Sub",
        ];
        return string.Join("/", await RunAsync([], string.Join("\r\n", lines) + "\r\n"));
    }
    // An intermediate past the range of a Double overflows only where it is stored as one; one past the extended range overflows where it is computed; a division by zero is error 11, before an overflow.
    [TestMethod]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = d * 10 / 10 ;; P Err.Number ;; P e", "0|1E+308|",
        DisplayName = "times 10, then divided by 10")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = (d * 10) - (d * 10) ;; P Err.Number ;; P e", "0|0|",
        DisplayName = "a difference of two past a Double")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = d * 10 * 10 / 100 ;; P Err.Number ;; P e", "0|1E+308|",
        DisplayName = "twice past a Double and back")]
    [DataRow("", "Dim s As Single, t As Single ;; s = 3E+38 ;; t = s * 10 / 10 ;; P Err.Number ;; P t", "0|3E+38|",
        DisplayName = "a Single past its range and back")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 3E+38 ;; e = s * 10 ;; P Err.Number ;; P e", "0|3.00000000549776E+39|",
        DisplayName = "a Single past its range, as a Double")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 3E+38 ;; l = s * 10 / 1E+35 ;; P Err.Number ;; P l", "0|30000|",
        DisplayName = "a Single past its range, as a Long")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E-300 ;; e = d * d * 1E+300 ;; P Err.Number ;; P e", "0|1E-300|",
        DisplayName = "below the least Double and back")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E-308 ;; e = d / 1E+10 * 1E+10 ;; P Err.Number ;; P e", "0|1E-308|",
        DisplayName = "a subnormal divided, multiplied back")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = d * d / d / d ;; P Err.Number ;; P e", "0|1|",
        DisplayName = "squared, then divided back twice")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = d * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 ;; P Err.Number ;; P e", "0|1|",
        DisplayName = "sixteen factors of 1E+308, divided back")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 0 ;; e = (1 / d) - (1 / d) ;; P Err.Number ;; P e", "11|-nan(ind)|", DisplayName = "a division by zero")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = (d * 10) / (d * 10) ;; P Err.Number ;; P e", "0|1|",
        DisplayName = "a quotient of two past a Double")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = d * 10 ;; P Err.Number ;; P e", "6|inf|", DisplayName = "stored past a Double")]
    [DataRow("", "Dim d As Double, i As Integer ;; d = 1E+308 ;; i = 5 ;; i = d * 10 / 1E+308 ;; P Err.Number ;; P i", "0|10|",
        DisplayName = "back in range, as an Integer")]
    [DataRow("", "Dim d As Double, z As Double, e As Double ;; d = 1E+308 ;; z = 0 ;; e = CDbl(d * 10) + 1 / z ;; P Err.Number ;; P e", "11|inf|",
        DisplayName = "an overflow, then a division by zero")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E-300 ;; e = d * 1E-10 ;; P Err.Number ;; P Bits(e)", "0|000012688B70E62B|",
        DisplayName = "below the least Double, stored subnormal")]
    public async Task AnIntermediatePastADouble_OverflowsOnlyWhereItIsStored(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // Abs, Int, Fix and Sgn compute on the extended value; CDbl rounds it to a Double, CSng to a Double and then to 24 bits, in the range of a Double; Sqr, Log and Atn are given it stored, and raise at once.
    [TestMethod]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = Abs(d * 10) / 10 ;; P Err.Number ;; P e", "0|1E+308|", DisplayName = "Abs past a Double")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = Sgn(d * 10) ;; P Err.Number ;; P e", "0|1|", DisplayName = "Sgn past a Double")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = Int(d * 10 / 10) ;; P Err.Number ;; P e", "0|1E+308|", DisplayName = "Int back in range")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = Fix(d * 10) / 10 ;; P Err.Number ;; P e", "0|1E+308|", DisplayName = "Fix past a Double")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+200 ;; e = Sqr(d * d) ;; P Err.Number ;; P e", "6|0|", DisplayName = "Sqr of a square past a Double")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = CDbl(d * 10) / 10 ;; P Err.Number ;; P e", "6|inf|", DisplayName = "CDbl past a Double")]
    [DataRow("", "Dim s As Single, t As Single ;; s = 3E+38 ;; t = CSng(s * 10) / 10 ;; P Err.Number ;; P t", "0|3E+38|", DisplayName = "CSng past a Single")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; e = CDbl(d + t) - d ;; P e", "0|",
        DisplayName = "CDbl rounds to a Double")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 16777216 ;; e = CSng(s + 1) - s ;; P e", "0|", DisplayName = "CSng rounds to 24 bits")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = CSng(d * 10) ;; P Err.Number ;; P e", "6|inf|", DisplayName = "CSng past a Double")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 3E+38 ;; e = CSng(s * 10) ;; P Err.Number ;; P e", "0|3.00000008662739E+39|",
        DisplayName = "CSng of a Single past its range")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 3E+38 ;; e = CDbl(s * 10) ;; P Err.Number ;; P e", "0|3.00000000549776E+39|",
        DisplayName = "CDbl of a Single past its range")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 3 ;; t = 1 / 2 ^ 60 ;; e = Int(d - t) ;; P e", "2|", DisplayName = "Int just below 3")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; e = Abs(d + t) - d ;; P e", "8.67361737988404E-19|",
        DisplayName = "Abs keeps the extended precision")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 3 ;; t = 1 / 2 ^ 60 ;; e = Fix(d - t) ;; P e", "2|", DisplayName = "Fix just below 3")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = Log(d * 10) ;; P Err.Number ;; P e", "6|0|", DisplayName = "Log past a Double")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = Int(d * 10) / 10 ;; P Err.Number ;; P e", "0|1E+308|", DisplayName = "Int past a Double")]
    [DataRow("", "Dim z As Double, e As Double ;; z = 0 ;; e = 5 ;; e = Abs(z / z) ;; P Err.Number ;; P e", "6|nan|", DisplayName = "Abs of 0 / 0")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; e = Sgn(d + t - d) ;; P e", "1|",
        DisplayName = "Sgn of a tiny difference")]
    [DataRow("", "Dim a As Double, b As Double ;; a = 1E-200 ;; b = 1E-200 ;; P CStr(Sgn(a * b))", "1|",
        DisplayName = "Sgn of a product too small for a Double")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 3E+38 ;; e = Int(s * 10) ;; P Err.Number ;; P e", "0|3.00000000549776E+39|",
        DisplayName = "Int past a Single")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 3E+38 ;; e = Abs(s * 10) ;; P Err.Number ;; P e", "0|3.00000000549776E+39|",
        DisplayName = "Abs past a Single")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 16777216 ;; l = Fix(s + 1) ;; P l", "16777217|", DisplayName = "Fix of a Single sum past 24 bits")]
    public async Task TheFunctionsOfANumber_ComputeOnTheExtendedValue_OrAreGivenItStored(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // Each operation is rounded to 64 bits - a Single's too - and the value again where it is stored: twice.
    [TestMethod]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; e = (d + t) - d ;; P e", "8.67361737988404E-19|",
        DisplayName = "1 plus 2^-60, less 1, in parentheses")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 1 ;; t = 1 / 2 ^ 53 ;; e = d + t - d ;; P e", "1.11022302462516E-16|",
        DisplayName = "1 plus 2^-53, less 1")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 1 ;; t = 1 / 2 ^ 53 ;; e = d + t + t - d ;; P e", "2.22044604925031E-16|",
        DisplayName = "1 plus 2^-53 twice, less 1")]
    [DataRow("", "Dim a As Double, b As Double, c As Double, e As Double ;; a = 0.1 ;; b = 0.2 ;; c = 0.3 ;; e = a + b - c ;; P e", "2.77555756156289E-17|",
        DisplayName = "0.1 + 0.2 - 0.3 of Doubles")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 0.1 ;; e = s * 3 ;; P e", "0.300000004470348|", DisplayName = "a Single times 3")]
    [DataRow("", "Dim s As Single, t As Single ;; s = 16777216 ;; t = s + 1 - s ;; P t", "1|", DisplayName = "a Single sum past 24 bits, as a Single")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 16777216 ;; e = s + 1 ;; P e", "16777217|", DisplayName = "a Single sum past 24 bits, as a Double")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 3 ;; e = 1 / d * d - 1 ;; P e", "0|", DisplayName = "1 / 3 * 3 - 1")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 0.1 ;; l = s * 3 * 1000000000 ;; P l", "300000004|", DisplayName = "a Single product, as a Long")]
    [DataRow("", "Dim s As Single, i As Integer, e As Double ;; s = 0.1 ;; i = 3 ;; e = s * i ;; P e", "0.300000004470348|", DisplayName = "times an Integer")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 1 ;; e = s / 3 ;; P e", "0.333333333333333|", DisplayName = "a Single divided by 3")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 0.1 ;; e = s * s ;; P e", "1.00000002980232E-02|", DisplayName = "a Single squared")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 3E+38 ;; e = s + s ;; P Err.Number ;; P e", "0|6.00000001099551E+38|",
        DisplayName = "a Single sum past its range")]
    [DataRow("", "Dim d As Double, b As Double, e As Double ;; d = 1 ;; b = 1 / 2 ^ 53 + 1 / 2 ^ 78 ;; e = d + b ;; P Bits(e) ;; P Bits(b)", "3FF0000000000000|3CA0000008000000|",
        DisplayName = "rounded to 64 bits, then to a Double")]
    [DataRow("", "Dim c As Currency, d As Double, e As Double ;; c = 1.1 ;; d = 3 ;; e = c * d ;; P Bits(e)", "400A666666666666|",
        DisplayName = "a Currency times a Double")]
    [DataRow("", "Dim c As Currency, e As Double ;; c = 0.3 ;; e = c / 3 ;; P Bits(e)", "3FB999999999999A|", DisplayName = "a Currency divided by 3")]
    [DataRow("", "Dim e As Double ;; e = 0.1 + 0.2 - 0.3 ;; P e", "2.77555756156289E-17|", DisplayName = "0.1 + 0.2 - 0.3 of literals")]
    [DataRow("", "Dim dt As Date, e As Double ;; dt = 0.1 ;; e = dt + 0.2 - 0.3 ;; P e", "2.77555756156289E-17|", DisplayName = "a Date plus 0.2, less 0.3")]
    [DataRow("", "Dim s As Single, t As Single ;; s = 0.1 ;; t = s * 3 ;; P Bits(t)", "3FD3333340000000|", DisplayName = "a Single times 3, as a Single")]
    [DataRow("", "Dim l As Long, d As Double, e As Double ;; l = 3 ;; d = 0.1 ;; e = l * d - 0.3 ;; P e", "2.77555756156289E-17|",
        DisplayName = "a Long times a Double")]
    [DataRow("", "Dim a As Double, b As Double, e As Double ;; a = 1E+16 ;; b = 1 ;; e = (a + b) - a ;; P e", "1|", DisplayName = "1E+16 plus 1, less 1E+16")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 0.1 ;; e = s + 0.5 ;; P e", "0.600000001490116|", DisplayName = "a Single plus 0.5")]
    public async Task EachOperation_IsRoundedToSixtyFourBits_AndTheValueAgainWhereItIsStored(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // Let-coerced to a numeric type - a variable, an element, a field, a For bound, a subscript, a ReDim bound, an operand of \, Mod, And, Or, Not - the value is converted from its 80 bits; written as a String, made a Decimal or held by a Variant, it is stored as its own type first.
    [TestMethod]
    [DataRow("", "Dim d As Double, t As Double, l As Long ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; l = d + t ;; P l", "3|", DisplayName = "a Long, from 2.5 + 2^-60")]
    [DataRow("", "Dim d As Double, t As Double, l As Long ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; l = CLng(d + t) ;; P l", "3|", DisplayName = "CLng of 2.5 + 2^-60")]
    [DataRow("", "Dim d As Double, t As String ;; d = 1E+308 ;; t = \"\" & (d * 10 / 10) ;; P Err.Number ;; P t", "0|1E+308|",
        DisplayName = "a concatenation, back in range")]
    [DataRow("", "Dim z As Double, b As Boolean ;; z = 0 ;; b = z / z ;; P Err.Number ;; P b", "6|False|", DisplayName = "a Boolean, from 0 / 0")]
    [DataRow("", "Dim z As Double, b As Boolean ;; z = 0 ;; b = 1 / z ;; P Err.Number ;; P b", "11|False|", DisplayName = "a Boolean, from 1 / 0")]
    [DataRow("", "Dim d As Double, c As Currency ;; d = 1E+308 ;; c = d * 10 / 1E+308 ;; P Err.Number ;; P c", "0|10|",
        DisplayName = "a Currency, back in range")]
    [DataRow("", "Dim d As Double, t As Double, c As Currency ;; d = 0.00005 ;; t = 1 / 2 ^ 70 ;; c = d + t ;; P c", "0.0001|",
        DisplayName = "a Currency, from 0.00005 + 2^-70")]
    [DataRow("", "Dim d As Double, t As String ;; d = 1E+308 ;; t = d * 10 / 10 ;; P Err.Number ;; P t", "0|1E+308|", DisplayName = "a String, back in range")]
    [DataRow("", "Dim d As Double, t As String ;; d = 1E+308 ;; t = \"x\" ;; t = d * 10 ;; P Err.Number ;; P t", "6|x|",
        DisplayName = "a String, past a Double")]
    [DataRow("", "Dim d As Double, b As Boolean ;; d = 1E+308 ;; b = d * 10 ;; P Err.Number ;; P b", "0|True|", DisplayName = "a Boolean, past a Double")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 16777216 ;; l = CLng(s + 1) ;; P l", "16777217|", DisplayName = "CLng of a Single sum past 24 bits")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 16777216 ;; l = s + 1 ;; P l", "16777217|", DisplayName = "a Long, from a Single sum past 24 bits")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 16777216 ;; l = (s + 1) Mod 16777218 ;; P l", "16777217|", DisplayName = "Mod, a Single sum past 24 bits")]
    [DataRow("", "Dim s As Single, c As Currency ;; s = 16777216 ;; c = s + 1 ;; P c", "16777217|", DisplayName = "a Currency, from a Single sum past 24 bits")]
    [DataRow("", "Dim s As Single, t As String ;; s = 16777216 ;; t = s + 1 ;; P t", "1.677722E+07|", DisplayName = "a String, from a Single sum past 24 bits")]
    [DataRow("", "Dim s As Single ;; s = 16777216 ;; P CStr(s + 1)", "1.677722E+07|", DisplayName = "CStr of a Single sum past 24 bits")]
    [DataRow("", "Dim s As Single, b As Boolean ;; s = 3E+38 ;; b = s * 10 ;; P Err.Number ;; P b", "0|True|", DisplayName = "a Boolean, past a Single")]
    [DataRow("", "Dim s As Single, i As Integer ;; s = 3E+38 ;; i = 3 ;; i = s * 10 / 1E+35 ;; P Err.Number ;; P i", "0|30000|",
        DisplayName = "an Integer, past a Single and back")]
    [DataRow("", "Dim s As Single, v As Variant ;; s = 3E+38 ;; v = s * 10 ;; P Err.Number ;; P TypeName(v)", "6|Empty|",
        DisplayName = "a Variant, past a Single")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 16777216 ;; e = s + 1 + CDbl(0) ;; P e", "16777217|", DisplayName = "a Single sum, plus a Double")]
    [DataRow("", "Dim s As Single, a(0 To 1) As Double ;; s = 16777216 ;; a(0) = s + 1 ;; P a(0)", "16777217|", DisplayName = "an element, from a Single sum")]
    [DataRow("Private Type R ;; X As Double ;; End Type", "Dim s As Single, r1 As R ;; s = 16777216 ;; r1.X = s + 1 ;; P r1.X", "16777217|",
        DisplayName = "a field, from a Single sum")]
    // a loop ends one step past its limit, wherever it starts (MS-VBAL §5.4.2.3). The program observed in Excel starts at 1; D31 starts just below the
    // limit, as 16 million passes of an empty loop take the boxed interpreter more than a minute on a busy machine. Its expected line is the one observed.
    [DataRow("", "Dim s As Single, l As Long ;; s = 16777216 ;; For l = 16777210 To s + 1 ;; Next ;; P l", "16777218|",
        DisplayName = "a For limit, a Single sum")]
    [DataRow("", "Dim d As Double, n As Long ;; d = 1E+308 ;; For n = 1 To d * 10 / 1E+308 ;; Next ;; P Err.Number ;; P n", "0|11|",
        DisplayName = "a For limit, back in range")]
    [DataRow("", "Dim d As Double, t As Double, l As Long ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; l = Not (d + t) ;; P l", "-4|", DisplayName = "Not of 2.5 + 2^-60")]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; P CBool(d * 10) ;; P Err.Number", "True|0|", DisplayName = "CBool past a Double")]
    [DataRow("", "Dim a(0 To 3) As Long, d As Double, t As Double ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; a(d + t) = 7 ;; P a(2) ;; P a(3)", "0|7|",
        DisplayName = "a subscript, 2.5 + 2^-60")]
    [DataRow("", "Dim a() As Long, d As Double, t As Double ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; ReDim a(d + t) ;; P UBound(a)", "3|",
        DisplayName = "a ReDim bound, 2.5 + 2^-60")]
    [DataRow("", "Dim d As Double, t As Double ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; P Mid$(\"abcdef\", d + t)", "cdef|", DisplayName = "a Mid$ start, 2.5 + 2^-60")]
    [DataRow("", "Dim d As Double, t As Double, l As Long ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; l = (d + t) Or 0 ;; P l", "3|", DisplayName = "Or of 2.5 + 2^-60")]
    [DataRow("", "Dim d As Double, t As Double ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; P Left$(\"abcdef\", d + t)", "abc|",
        DisplayName = "a Left$ length, 2.5 + 2^-60")]
    public async Task LetCoercedToANumericType_TheValueIsConvertedFromItsEightyBits(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // A parameter of a numeric type, ByVal or ByRef, is given the value converted to its type; a Variant parameter, stored as the expression's.
    [TestMethod]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; P d * 10 / 10 ;; P Err.Number", "1E+308|0|", DisplayName = "a Variant parameter, back in range")]
    [DataRow("Private Function F(ByVal x As Double) As Double ;; F = x * 10 / 10 ;; End Function", "P F(1E+308) ;; P Err.Number", "1E+308|0|",
        DisplayName = "a Double parameter, back in range")]
    [DataRow("Private Function F(ByVal x As Double) As Double ;; F = x * 10 ;; End Function", "P F(1E+308) ;; P Err.Number", "6|",
        DisplayName = "a Double parameter, past its range")]
    [DataRow("Private Function G(ByVal x As Double) As Double ;; G = x - 1 ;; End Function", "Dim d As Double, t As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; P G(d + t)", "0|",
        DisplayName = "a ByVal Double, 1 + 2^-60")]
    [DataRow("Private Function H(x As Double) As Double ;; H = x - 1 ;; End Function", "Dim d As Double, t As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; P H(d + t)", "0|",
        DisplayName = "a ByRef Double, 1 + 2^-60")]
    [DataRow("Private Function V1(ByVal x As Variant) As Double ;; V1 = x - 1 ;; End Function", "Dim d As Double, t As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; P V1(d + t)", "0|",
        DisplayName = "a ByVal Variant, 1 + 2^-60")]
    [DataRow("Private Function G(ByVal x As Double) As Double ;; G = x ;; End Function", "Dim s As Single ;; s = 3E+38 ;; P G(s * 10) ;; P Err.Number", "3.00000000549776E+39|0|",
        DisplayName = "a Double parameter, past a Single")]
    [DataRow("Private Function G(ByVal x As Double) As Double ;; G = x ;; End Function", "Dim s As Single ;; s = 16777216 ;; P G(s + 1)", "16777217|",
        DisplayName = "a Double parameter, a Single sum")]
    [DataRow("", "Dim s As Single ;; s = 3E+38 ;; P s * 10 ;; P Err.Number", "6|", DisplayName = "a Variant parameter, past a Single")]
    [DataRow("Private Function F2(ByVal s As Single) As Double ;; F2 = s * 10 ;; End Function", "P F2(3E+38) ;; P Err.Number", "3.00000000549776E+39|0|",
        DisplayName = "a Single parameter, multiplied past it")]
    [DataRow("Private Sub Q(ByRef x As Double) ;; P x ;; End Sub", "Dim s As Single ;; s = 16777216 ;; Q s + 1", "16777217|",
        DisplayName = "a ByRef Double, a Single sum")]
    [DataRow("", "Dim d As Double, t As Double ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; P Round(d + t)", "2|", DisplayName = "Round of 2.5 + 2^-60")]
    public async Task AnArgument_IsConvertedToItsParametersType(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // A comparison rounds each operand to the precision of its effective type, in the extended range, and raises what is pending; a Select Case selector is stored.
    [TestMethod]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; Select Case d * 10 ;; Case Is > 0 ;; P \"gt\" ;; End Select ;; P Err.Number", "6|",
        DisplayName = "a Select Case selector past a Double")]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; If d * 10 = d * 10 Then P \"eq\" ;; P Err.Number", "eq|0|", DisplayName = "equal, past a Double")]
    [DataRow("", "Dim s As Single ;; s = 3E+38 ;; If s * 10 > s Then P \"gt\" ;; P Err.Number", "gt|0|", DisplayName = "a Single past its range, compared")]
    [DataRow("", "Dim s As Single ;; s = 0.1 ;; If s * 3 = CSng(0.3) Then P \"eq\" Else P \"ne\"", "eq|", DisplayName = "a Single times 3, equal to CSng(0.3)")]
    [DataRow("", "Dim s As Single ;; s = 16777216 ;; If s + 1 > s Then P \"gt\" Else P \"ng\"", "ng|", DisplayName = "a Single sum past 24 bits, compared")]
    [DataRow("", "Dim d As Double ;; d = 1 ;; If d + 1 / 2 ^ 60 > d Then P \"gt\" Else P \"ng\"", "ng|", DisplayName = "1 + 2^-60, compared to 1")]
    [DataRow("", "Dim z As Double ;; z = 0 ;; If 1 / z > 0 Then P \"gt\" ;; P Err.Number", "gt|11|", DisplayName = "1 / 0, compared")]
    [DataRow("", "Dim z As Double ;; z = 0 ;; If z / z = z / z Then P \"eq\" Else P \"ne\" ;; P Err.Number", "eq|6|", DisplayName = "0 / 0, equal to itself")]
    [DataRow("", "Dim l As Long, d As Double ;; l = 1 ;; d = 1 ;; If l + 1 / 2 ^ 60 > d Then P \"gt\" Else P \"ng\"", "ng|", DisplayName = "a Long plus 2^-60")]
    [DataRow("", "Dim d As Double, t As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; Select Case d + t ;; Case Is > d ;; P \"gt\" ;; Case Else ;; P \"ng\" ;; End Select", "ng|",
        DisplayName = "a Select Case selector, 1 + 2^-60")]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; If CDbl(d * 10) > 0 Then P \"gt\" ;; P Err.Number", "gt|6|", DisplayName = "CDbl past a Double, compared")]
    [DataRow("", "Dim s As Single ;; s = 16777216 ;; Select Case s + 1 ;; Case 16777217 ;; P \"is\" ;; Case Else ;; P \"not\" ;; End Select", "not|",
        DisplayName = "a Select Case selector, a Single sum")]
    [DataRow("", "Dim d As Double, t As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; P IIf(d + t > d, \"gt\", \"ng\")", "ng|", DisplayName = "IIf of 1 + 2^-60 > 1")]
    [DataRow("", "Dim d As Double, t As Double, b As Boolean ;; d = 1 ;; t = 1 / 2 ^ 60 ;; b = d + t > d ;; P b", "False|", DisplayName = "as a Boolean")]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; If d * 10 Then P \"t\" ;; P Err.Number", "t|0|", DisplayName = "an If condition past a Double")]
    [DataRow("", "Dim s As Single ;; s = 16777216 ;; If s + 1 = 16777217 Then P \"eq\" Else P \"ne\"", "eq|", DisplayName = "a Single sum, equal to a literal")]
    [DataRow("", "Dim s As Single, v As Variant ;; s = 16777216 ;; v = 16777217 ;; If s + 1 = v Then P \"eq\" Else P \"ne\"", "ne|",
        DisplayName = "a Single sum, equal to a Variant")]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; Select Case 5 ;; Case Is < d * 10 ;; P \"lt\" ;; End Select ;; P Err.Number", "lt|6|",
        DisplayName = "a Case Is expression past a Double")]
    [DataRow("", "Dim d As Double, n As Long ;; d = 1E+308 ;; Do While d * 10 > 0 And n < 1 ;; n = n + 1 ;; Loop ;; P n ;; P Err.Number", "1|0|",
        DisplayName = "a Do While condition past a Double")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 16777216 ;; If s + 1 > 16777216 Then P \"gt\" Else P \"ng\"", "gt|",
        DisplayName = "a Single sum, greater than a literal")]
    [DataRow("", "Dim d As Double, t As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; P (d + t > d) And True", "False|", DisplayName = "a comparison And True")]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; P IIf(d * 10 > 0, \"gt\", \"ng\") ;; P Err.Number", "gt|0|", DisplayName = "IIf, past a Double")]
    [DataRow("", "Dim s As Single, i As Integer ;; s = 16777216 ;; i = 1 ;; If s + i = 16777217 Then P \"eq\" Else P \"ne\"", "eq|",
        DisplayName = "a Single plus an Integer, equal")]
    public async Task AComparison_RoundsToThePrecisionOfItsType_InTheExtendedRange(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // An operation with a Variant operand is the Variant arithmetic, which rounds each result to its type: a typed operand is stored as its type for it.
    [TestMethod]
    [DataRow("", "Dim d As Double, v As Variant, e As Double ;; d = 1E+308 ;; v = 10 ;; e = d * v / v ;; P Err.Number ;; P e", "6|0|",
        DisplayName = "a Double times a Variant, past a Double")]
    [DataRow("", "Dim v As Variant, e As Double ;; v = CDbl(1E+308) ;; e = v * 10 / 10 ;; P Err.Number ;; P e", "6|0|",
        DisplayName = "a Variant Double, past its range")]
    [DataRow("", "Dim s As Single, v As Variant ;; s = 16777216 ;; v = s + 1 ;; P TypeName(v) ;; P v - s", "Single|0|",
        DisplayName = "a Single sum past 24 bits, held by a Variant")]
    [DataRow("", "Dim v As Variant, e As Double ;; v = 0.1 ;; e = v + 0.2 - 0.3 ;; P e", "5.55111512312578E-17|", DisplayName = "a Variant plus 0.2, less 0.3")]
    [DataRow("", "Dim v As Variant, a As Double, e As Double ;; v = 0.2 ;; a = 0.1 ;; e = a + v - 0.3 ;; P e", "5.55111512312578E-17|",
        DisplayName = "a Double plus a Variant, less 0.3")]
    [DataRow("", "Dim v As Variant, e As Double ;; v = CSng(0.1) ;; e = v * 3 ;; P e", "0.300000011920929|", DisplayName = "a Variant Single times 3")]
    [DataRow("", "Dim v As Variant, w As Variant ;; v = CSng(16777216) ;; w = v + 1 ;; P TypeName(w) ;; P w - v", "Single|0|",
        DisplayName = "a Variant Single sum past 24 bits")]
    [DataRow("", "Dim d As Double, t As Double, v As Variant ;; d = 1 ;; t = 1 / 2 ^ 60 ;; v = d + t ;; P v - d", "0|",
        DisplayName = "1 + 2^-60, in a Variant")]
    [DataRow("", "Dim v As Variant, s As Single, e As Double ;; v = 1 ;; s = 16777216 ;; e = (s + 1) + v ;; P e", "16777216|",
        DisplayName = "a Single sum plus a Variant")]
    [DataRow("", "Dim s As Single, v As Variant, e As Double ;; s = 3E+38 ;; v = 0 ;; e = s * 10 + v ;; P Err.Number ;; P e", "6|0|",
        DisplayName = "a Single past its range plus a Variant")]
    public async Task AVariantOperation_RoundsEachResult(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // The operands of ^ are converted to Doubles from their 80 bits, and raise at once; its result is a Double, which the expression goes on with.
    [TestMethod]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = (d * 10) ^ 1 ;; P Err.Number ;; P e", "6|0|",
        DisplayName = "an operand of ^ past a Double")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = d ^ 2 / d ;; P Err.Number ;; P e", "6|0|", DisplayName = "a Double squared, divided back")]
    [DataRow("", "Dim d As Double, l As Long ;; d = 1E+308 ;; l = (d * 10 / 1E+308) Mod 3 ;; P Err.Number ;; P l", "0|1|", DisplayName = "Mod, back in range")]
    [DataRow("", "Dim d As Double, t As Double, l As Long ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; l = (d + t) \\ 1 ;; P l", "3|",
        DisplayName = "integer division of 2.5 + 2^-60")]
    [DataRow("", "Dim d As Double, t As Double, l As Long ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; l = (d + t) And 7 ;; P l", "3|", DisplayName = "And of 2.5 + 2^-60")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 16777216 ;; e = (s + 1) ^ 1 ;; P e", "16777217|", DisplayName = "^ of a Single sum past 24 bits")]
    [DataRow("", "Dim s As Single ;; s = 16777216 ;; P TypeName(s + 1 + 0.5)", "Double|", DisplayName = "a Single sum plus 0.5 is a Double")]
    [DataRow("", "Dim d As Double, e As Double, i As Integer ;; d = 1E+308 ;; i = 2 ;; e = d * 10 / 10 ^ i ;; P Err.Number ;; P e", "0|1E+307|",
        DisplayName = "divided by a power, back in range")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = (d * 10) / (5 + 5) ;; P Err.Number ;; P e", "0|1E+308|",
        DisplayName = "divided by a sum, back in range")]
    public async Task TheOperandsOfAnotherOperator_AreConvertedFromTheirEightyBits(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // A constant expression - of a Const, or of literals - is computed in the extended format too.
    [TestMethod]
    [DataRow("", "Dim e As Double ;; e = 1E+308 * 10 / 10 ;; P Err.Number ;; P e", "0|1E+308|", DisplayName = "literals past a Double and back")]
    [DataRow("Const K As Double = 1E+308", "Dim e As Double ;; e = K * 10 / 10 ;; P Err.Number ;; P e", "0|1E+308|",
        DisplayName = "a Const past a Double and back")]
    [DataRow("Const K = 1E+308 * 10 / 10", "P K ;; P Err.Number", "1E+308|0|", DisplayName = "a Const expression past a Double and back")]
    [DataRow("Const K = 0.1 + 0.2 - 0.3", "P K", "2.77555756156289E-17|", DisplayName = "a Const expression of 0.1 + 0.2 - 0.3")]

    public async Task AConstantExpression_IsComputedInTheExtendedFormat(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));
}
