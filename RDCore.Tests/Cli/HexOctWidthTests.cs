using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBA writes <c>Hex</c> and <c>Oct</c> of a negative number in the width of its type - an <c>Integer</c> and a <c>Boolean</c> in 16 bits, a
/// <c>Long</c> in 32, a <c>LongLong</c> in 64 - and of any other type Let-coerced to a <c>LongLong</c>, in 64; where MS-VBAL §6.1.2.3.1.17 picks the
/// width by the value. Every expected line was recorded in Excel, 64-bit (grid <c>oracle\s43</c> X01-X16).
/// </summary>
[TestClass]
public sealed class HexOctWidthTests
{
    private static string Run(string body)
    {
        string[] lines =
        [
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Private acc As String",
            "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub",
            "Public Sub Main()",
            "    On Error Resume Next",
            "    " + string.Join("\r\n    ", body.Split(" ;; ")),
            "    Debug.Print acc",
            "End Sub",
        ];
        return string.Join("/", RunAsync([], string.Join("\r\n", lines) + "\r\n").GetAwaiter().GetResult());
    }

    [TestMethod]
    [DataRow("Dim i As Integer ;; i = -1 ;; P Hex$(i) ;; P Oct$(i) ;; i = -7 ;; P Hex$(i) ;; i = -32768 ;; P Hex$(i) ;; P Oct$(i) ;; i = 7 ;; P Hex$(i)", "FFFF|177777|FFF9|8000|100000|7|", DisplayName = "X01")]
    [DataRow("Dim l As Long ;; l = -1 ;; P Hex$(l) ;; P Oct$(l) ;; l = -32768 ;; P Hex$(l) ;; l = -32769 ;; P Hex$(l) ;; l = -2147483648# ;; P Hex$(l) ;; l = 70000 ;; P Hex$(l)", "FFFFFFFF|37777777777|FFFF8000|FFFF7FFF|80000000|11170|", DisplayName = "X02")]
    [DataRow("Dim b As Byte ;; b = 255 ;; P Hex$(b) ;; P Oct$(b) ;; b = 0 ;; P Hex$(b)", "FF|377|0|", DisplayName = "X03")]
    [DataRow("Dim f As Boolean ;; f = True ;; P Hex$(f) ;; P Oct$(f) ;; f = False ;; P Hex$(f)", "FFFF|177777|0|", DisplayName = "X04")]
    [DataRow("Dim d As Double ;; d = -7 ;; P Hex$(d) ;; P Oct$(d) ;; d = -7.5 ;; P Hex$(d) ;; d = -6.5 ;; P Hex$(d) ;; d = -70000 ;; P Hex$(d) ;; d = -32768 ;; P Hex$(d) ;; d = -32767 ;; P Hex$(d)", "FFFFFFFFFFFFFFF9|1777777777777777777771|FFFFFFFFFFFFFFF8|FFFFFFFFFFFFFFFA|FFFFFFFFFFFEEE90|FFFFFFFFFFFF8000|FFFFFFFFFFFF8001|", DisplayName = "X05")]
    [DataRow("Dim d As Double ;; d = -2147483649# ;; P Hex$(d) ;; P Err.Number ;; Err.Clear ;; d = 4294967296# ;; P Hex$(d) ;; P Err.Number ;; Err.Clear ;; d = 2147483648# ;; P Hex$(d) ;; P Err.Number", "FFFFFFFF7FFFFFFF|0|100000000|0|80000000|0|", DisplayName = "X06")]
    [DataRow("Dim t As String ;; t = \"-7\" ;; P Hex$(t) ;; P Oct$(t) ;; t = \"-70000\" ;; P Hex$(t) ;; t = \"&HFFFF\" ;; P Hex$(t) ;; t = \"-32768\" ;; P Hex$(t)", "FFFFFFFFFFFFFFF9|1777777777777777777771|FFFFFFFFFFFEEE90|FFFF|FFFFFFFFFFFF8000|", DisplayName = "X07")]
    [DataRow("Dim c As Currency ;; c = -7 ;; P Hex$(c) ;; P Oct$(c) ;; c = -70000 ;; P Hex$(c) ;; c = -32768 ;; P Hex$(c)", "FFFFFFFFFFFFFFF9|1777777777777777777771|FFFFFFFFFFFEEE90|FFFFFFFFFFFF8000|", DisplayName = "X08")]
    [DataRow("Dim s As Single ;; s = -7 ;; P Hex$(s) ;; P Oct$(s) ;; s = -70000 ;; P Hex$(s)", "FFFFFFFFFFFFFFF9|1777777777777777777771|FFFFFFFFFFFEEE90|", DisplayName = "X09")]
    [DataRow("Dim v As Variant ;; v = CInt(-7) ;; P Hex$(v) ;; v = CLng(-7) ;; P Hex$(v) ;; v = CDbl(-7) ;; P Hex$(v) ;; v = CByte(200) ;; P Hex$(v) ;; v = True ;; P Hex$(v) ;; v = \"-7\" ;; P Hex$(v) ;; v = CCur(-7) ;; P Hex$(v)", "FFF9|FFFFFFF9|FFFFFFFFFFFFFFF9|C8|FFFF|FFFFFFFFFFFFFFF9|FFFFFFFFFFFFFFF9|", DisplayName = "X10")]
    [DataRow("P Hex$(-7) ;; P Hex$(-7&) ;; P Hex$(-32768) ;; P Hex$(-32768&) ;; P Hex$(-7#) ;; P Hex$(-7@) ;; P Hex$(-7!) ;; P Hex$(-7 + 0) ;; P Hex$(CInt(-7) * 1&) ;; P Hex$(-7 * 1.5)", "FFF9|FFFFFFF9|FFFF8000|FFFF8000|FFFFFFFFFFFFFFF9|FFFFFFFFFFFFFFF9|FFFFFFFFFFFFFFF9|FFF9|FFFFFFF9|FFFFFFFFFFFFFFF6|", DisplayName = "X11")]
    [DataRow("Dim dt As Date ;; dt = -7 ;; P Hex$(dt) ;; P Oct$(dt) ;; dt = -70000 ;; P Hex$(dt)", "FFFFFFFFFFFFFFF9|1777777777777777777771|FFFFFFFFFFFEEE90|", DisplayName = "X12")]
    [DataRow("P Hex$(CDec(-7)) ;; P Oct$(CDec(-7)) ;; P Hex$(CDec(-70000))", "FFFFFFFFFFFFFFF9|1777777777777777777771|FFFFFFFFFFFEEE90|", DisplayName = "X13")]
    [DataRow("P Hex$(CLngLng(-7)) ;; P Oct$(CLngLng(-7)) ;; P Hex$(Empty) ;; P Hex(Null) ;; P Hex$(Null) ;; P Err.Number", "FFFFFFFFFFFFFFF9|1777777777777777777771|0|94|", DisplayName = "X14")]
    [DataRow("Dim l As Long ;; l = -7 ;; P Oct$(l) ;; l = -70000 ;; P Oct$(l) ;; Dim i As Integer ;; i = -7 ;; P Oct$(i) ;; Dim d As Double ;; d = -70000 ;; P Oct$(d)", "37777777771|37777567220|177771|1777777777777777567220|", DisplayName = "X15")]
    [DataRow("P Hex(CInt(-7)) ;; P TypeName(Hex(CLng(-7))) ;; P Hex(CLng(-7)) ;; P Hex(-1) ;; P Hex(-1&)", "FFF9|String|FFFFFFF9|FFFF|FFFFFFFF|", DisplayName = "X16")]
    public void HexAndOct_WriteANegativeNumberInTheWidthOfItsType(string body, string printed)
        => Assert.AreEqual(printed, Run(body));
}