using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A division by zero, under <c>On Error Resume Next</c>: the error each shape raises, and what its target holds after it (RD-VBAL §5.6.9.3, §5.4.3.8). A
/// floating-point division of typed operands assigns a <c>Single</c> or <c>Double</c> target the infinity or NaN it made before raising, whatever the
/// operands' types; MS-VBAL §5.6.9.3.5 says so of two operands declared <c>Double</c> only. Every expected line was recorded in Excel (64-bit VBA7). Each
/// target is given 5 first, so that a target left as it was is told from one assigned zero.
/// </summary>
[TestClass]
public sealed class DivisionByZeroTests
{
    // the program of a case: P writes what it is given, as a Variant, and what was written is printed at the end.
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
    [DataRow("Dim x As Double ;; x = 5 ;; x = 1 / 0 ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Double = literal / literal 0")]
    [DataRow("Dim x As Double ;; x = 5 ;; x = 1 / 0 + 1 ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Double = literal / literal 0 + 1")]
    [DataRow("Dim x As Double ;; x = 5 ;; x = 1# / 0# ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Double = Double literal / Double literal 0")]
    [DataRow("Dim x As Double, z As Integer ;; x = 5 ;; z = 0 ;; x = 1 / z ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Double = literal / Integer 0")]
    [DataRow("Dim x As Double, z As Long ;; x = 5 ;; z = 0 ;; x = 1 / z ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Double = literal / Long 0")]
    [DataRow("Dim x As Double, z As Double ;; x = 5 ;; z = 0 ;; x = 1 / z ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Double = literal / Double 0")]
    [DataRow("Dim x As Double, a As Double, z As Double ;; x = 5 ;; a = 1 ;; z = 0 ;; x = a / z ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Double = Double / Double 0")]
    [DataRow("Dim x As Double, a As Integer, z As Double ;; x = 5 ;; a = 1 ;; z = 0 ;; x = a / z ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Double = Integer / Double 0")]
    [DataRow("Dim x As Double, a As Integer, z As Integer ;; x = 5 ;; a = 1 ;; z = 0 ;; x = a / z ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Double = Integer / Integer 0")]
    [DataRow("Dim x As Double, a As Single, z As Single ;; x = 5 ;; a = 1 ;; z = 0 ;; x = a / z ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Double = Single / Single 0")]
    [DataRow("Dim x As Double, a As Currency, z As Currency ;; x = 5 ;; a = 1 ;; z = 0 ;; x = a / z ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Double = Currency / Currency 0")]
    [DataRow("Dim x As Single ;; x = 5 ;; x = 1 / 0 ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Single = literal / literal 0")]
    [DataRow("Dim x As Single, z As Single ;; x = 5 ;; z = 0 ;; x = 1 / z ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Single = literal / Single 0")]
    [DataRow("Dim x As Single, z As Double ;; x = 5 ;; z = 0 ;; x = 1 / z ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Single = literal / Double 0")]
    [DataRow("Dim x As Single, a As Single, z As Single ;; x = 5 ;; a = 1 ;; z = 0 ;; x = a / z ;; P Err.Number ;; P x", "11|inf|", DisplayName = "Single = Single / Single 0")]
    [DataRow("Dim x As Double ;; x = 5 ;; x = 0 / 0 ;; P Err.Number ;; P x", "6|-nan(ind)|", DisplayName = "Double = literal 0 / literal 0")]
    [DataRow("Dim x As Double ;; x = 5 ;; x = 0# / 0# ;; P Err.Number ;; P x", "6|-nan(ind)|", DisplayName = "Double = Double literal 0 / Double literal 0")]
    [DataRow("Dim x As Double, z As Double ;; x = 5 ;; z = 0 ;; x = z / z ;; P Err.Number ;; P x", "6|-nan(ind)|", DisplayName = "Double = Double 0 / Double 0")]
    public void AFloatingPointDivisionByZero_AssignsASingleOrDoubleTargetWhatItMade_ThenRaises(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    [TestMethod]
    [DataRow("Dim x As Double, z As Variant ;; x = 5 ;; z = 0 ;; x = 1 / z ;; P Err.Number ;; P x", "11|5|", DisplayName = "Double = literal / Variant 0")]
    [DataRow("Dim x As Double, a As Variant, z As Variant ;; x = 5 ;; a = 1 ;; z = 0 ;; x = a / z ;; P Err.Number ;; P x", "11|5|", DisplayName = "Double = Variant / Variant 0")]
    [DataRow("Dim x As Variant ;; x = 5 ;; x = 1 / 0 ;; P Err.Number ;; P x", "11|5|", DisplayName = "Variant = literal / literal 0")]
    [DataRow("Dim x As Variant, z As Double ;; x = 5 ;; z = 0 ;; x = 1 / z ;; P Err.Number ;; P x", "11|5|", DisplayName = "Variant = literal / Double 0")]
    [DataRow("Dim x As Variant, z As Variant ;; x = 5 ;; z = 0 ;; x = 1 / z ;; P Err.Number ;; P x", "11|5|", DisplayName = "Variant = literal / Variant 0")]
    [DataRow("Dim x As Variant, a As Double, z As Double ;; x = 5 ;; a = 1 ;; z = 0 ;; x = a / z ;; P Err.Number ;; P x", "11|5|", DisplayName = "Variant = Double / Double 0")]
    [DataRow("Dim l As Long ;; l = 5 ;; l = 1 / 0 ;; P Err.Number ;; P l", "11|5|", DisplayName = "Long = literal / literal 0")]
    [DataRow("Dim l As Long, z As Double ;; l = 5 ;; z = 0 ;; l = 1 / z ;; P Err.Number ;; P l", "11|5|", DisplayName = "Long = literal / Double 0")]
    [DataRow("Dim s As String ;; s = \"s\" ;; s = 1 / 0 ;; P Err.Number ;; P s", "11|s|", DisplayName = "String = literal / literal 0")]
    [DataRow("Dim s As String, z As Double ;; s = \"s\" ;; z = 0 ;; s = \"x=\" & 1 / z ;; P Err.Number ;; P s", "11|s|", DisplayName = "String = text & literal / Double 0")]
    public void ADivisionByZero_OfAVariantOperand_OrAssignedToAnythingButASingleOrDouble_LeavesTheTargetAsItWas(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    [TestMethod]
    [DataRow("Dim x As Double ;; x = 5 ;; x = 1 \\ 0 ;; P Err.Number ;; P x", "11|5|", DisplayName = "Double = literal \\ literal 0")]
    [DataRow("Dim x As Double, z As Double ;; x = 5 ;; z = 0 ;; x = 1 \\ z ;; P Err.Number ;; P x", "11|5|", DisplayName = "Double = literal \\ Double 0")]
    [DataRow("Dim x As Double ;; x = 5 ;; x = 1 Mod 0 ;; P Err.Number ;; P x", "11|5|", DisplayName = "Double = literal Mod literal 0")]
    [DataRow("Dim x As Double, z As Double ;; x = 5 ;; z = 0 ;; x = 1 Mod z ;; P Err.Number ;; P x", "11|5|", DisplayName = "Double = literal Mod Double 0")]
    [DataRow("Dim x As Variant, z As Variant ;; x = 5 ;; z = 0 ;; x = 1 \\ z ;; P Err.Number ;; P x", "11|5|", DisplayName = "Variant = literal \\ Variant 0")]
    [DataRow("Dim x As Variant, z As Variant ;; x = 5 ;; z = 0 ;; x = 1 Mod z ;; P Err.Number ;; P x", "11|5|", DisplayName = "Variant = literal Mod Variant 0")]
    [DataRow("Dim l As Long, z As Long ;; l = 5 ;; z = 0 ;; l = 1 \\ z ;; P Err.Number ;; P l", "11|5|", DisplayName = "Long = literal \\ Long 0")]
    [DataRow("Dim l As Long, z As Long ;; l = 5 ;; z = 0 ;; l = 1 Mod z ;; P Err.Number ;; P l", "11|5|", DisplayName = "Long = literal Mod Long 0")]
    public void AnIntegralDivisionOrModByZero_Raises11_AndAssignsNothing(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    [TestMethod]
    [DataRow("P 1 / 0 ;; P Err.Number", "11|", DisplayName = "an argument: literal / literal 0")]
    [DataRow("Dim z As Double ;; z = 0 ;; P CStr(1 / z) ;; P Err.Number", "11|", DisplayName = "CStr of literal / Double 0")]
    public void ADivisionByZero_GivenAsAnArgument_RaisesBeforeTheCall(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    // an infinity and a NaN a Double holds, as text: as MS-VBA writes them, and not MS-VBAL §5.5.1.2.4's 1.#INF and -1.#IND.
    [TestMethod]
    public void AnInfiniteOrNaNDouble_AsText_IsInfMinusInfOrMinusNanInd()
        => Assert.AreEqual("x=inf|x=-inf|x=-nan(ind)|",
            Run("Dim x As Double, z As Double ;; z = 0 ;; x = 1 / z ;; P \"x=\" & x ;; x = -1 / z ;; P \"x=\" & x ;; x = z / z ;; P \"x=\" & x"));
}
