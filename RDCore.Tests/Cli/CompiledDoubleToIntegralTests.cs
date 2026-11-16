using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Single</c> or a <c>Double</c> let-coerced to an integer type is compiled (MS-VBAL §5.5.1.2.1): rounded to the nearest whole number, ties to even
/// (§5.5.1.2.1.1), when the whole number it rounds to is in the range of the type. Past it, the interpreter is given the statement, and raises error 6.
/// </summary>
[TestClass]
public sealed class CompiledDoubleToIntegralTests
{
    // each integer type, given halves and numbers near them: a half rounds to the even neighbor, anything else to the nearest.
    [TestMethod]
    [DataRow("Byte", DisplayName = "a Byte")]
    [DataRow("Integer", DisplayName = "an Integer")]
    [DataRow("Long", DisplayName = "a Long")]
    [DataRow("LongLong", DisplayName = "a LongLong")]
    public async Task ADouble_IsRoundedToEven(string type)
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Rounded(0.5); Rounded(1.5); Rounded(2.5); Rounded(2.4999); Rounded(2.5001); Rounded(254.5)",
            "End Sub",
            $"Private Function Rounded(ByVal D As Double) As {type}",
            "    Rounded = D",
            "End Function"), [], "| 0  2  2  2  3  254 |", ("Rounded", 0));

    [TestMethod]
    [DataRow("Integer", DisplayName = "an Integer")]
    [DataRow("Long", DisplayName = "a Long")]
    [DataRow("LongLong", DisplayName = "a LongLong")]
    public async Task ANegativeDouble_IsRoundedToEven(string type)
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Rounded(-0.5); Rounded(-1.5); Rounded(-2.5); Rounded(-2.6)",
            "End Sub",
            $"Private Function Rounded(ByVal D As Double) As {type}",
            "    Rounded = D",
            "End Function"), [], "| 0 -2 -2 -3 |", ("Rounded", 0));

    // the range is that of the whole number the value rounds to: 32767.4 is an Integer, and so are -32768.5 and, for a Byte, -0.5.
    [TestMethod]
    public async Task TheRange_IsTheWholeNumbersRoundedTo()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer()",
            "End Sub",
            "Private Function Outer() As String",
            "    Dim I As Integer, J As Integer, B As Byte, D As Double",
            "    D = 32767.4",
            "    I = D",
            "    D = -32768.5",
            "    J = D",
            "    D = -0.5",
            "    B = D",
            "    Outer = I & \" \" & J & \" \" & B",
            "End Function"), [], "| 32767 -32768 0 |", ("Outer", 0));

    // a value whose whole number is past the range of the type: the interpreter is given the statement, and raises error 6 (Overflow).
    [TestMethod]
    [DataRow("Byte", "255.5", DisplayName = "a Byte of 255.5")]
    [DataRow("Byte", "-0.6", DisplayName = "a Byte of -0.6")]
    [DataRow("Integer", "32767.5", DisplayName = "an Integer of 32767.5")]
    [DataRow("Integer", "-32768.6", DisplayName = "an Integer of -32768.6")]
    [DataRow("Long", "2147483647.5", DisplayName = "a Long of 2147483647.5")]
    [DataRow("LongLong", "9.2233720368547758E+18", DisplayName = "a LongLong of 2^63")]
    public async Task AWholeNumberPastTheRange_RaisesError6(string type, string value)
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer()",
            "End Sub",
            "Private Function Outer() As Long",
            $"    Dim X As {type}, D As Double",
            $"    D = {value}",
            "    On Error Resume Next",
            "    X = D",
            "    Outer = Err.Number",
            "End Function"), [], "| 6 |", ("Outer", 1));

    [TestMethod]
    public async Task ASingle_IsRoundedToEven()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer()",
            "End Sub",
            "Private Function Outer() As String",
            "    Dim S As Single, L As Long, M As Long",
            "    S = 2.5!",
            "    L = S",
            "    S = 3.5!",
            "    M = S",
            "    Outer = L & \" \" & M",
            "End Function"), [], "| 2 4 |", ("Outer", 0));

    // a Variant that holds a Double, let-coerced to a Long.
    [TestMethod]
    public async Task AVariantHoldingADouble_IsRoundedToEven()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(2.5); Outer(3.5)",
            "End Sub",
            "Private Function Outer(ByVal D As Double) As Long",
            "    Dim V As Variant, L As Long",
            "    V = D",
            "    L = V",
            "    Outer = L",
            "End Function"), [], "| 2  4 |", ("Outer", 0));

    // the operands of an integer division are let-coerced to an integer type (MS-VBAL §5.6.9.3.5), each rounded to even.
    [TestMethod]
    public async Task TheOperandsOfAnIntegerDivision_AreRoundedToEven()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(2.5); Outer(3.5); Outer(9.5)",
            "End Sub",
            "Private Function Outer(ByVal D As Double) As Long",
            "    Outer = D \\ 2",
            "End Function"), [], "| 1  2  5 |", ("Outer", 0));

    // and so are those of Mod and of a logical operator: 2.5 Mod 2 is 2 Mod 2, and 3.5 And 7 is 4 And 7.
    [TestMethod]
    public async Task TheOperandsOfModAndOfAnd_AreRoundedToEven()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(2.5); Outer(3.5)",
            "End Sub",
            "Private Function Outer(ByVal D As Double) As Long",
            "    Outer = (D Mod 2) * 10 + (D And 7)",
            "End Function"), [], "| 2  4 |", ("Outer", 0));

    // the value of a Case is let-coerced to the type of a Long selector (RD-VBAL §5.4.2.10): 2.5 is the Long 2.
    [TestMethod]
    public async Task TheValueOfACase_IsRoundedToTheSelectorsType()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(2); Outer(3)",
            "End Sub",
            "Private Function Outer(ByVal L As Long) As Long",
            "    Dim D As Double",
            "    D = 2.5",
            "    Select Case L",
            "        Case D",
            "            Outer = 1",
            "        Case Else",
            "            Outer = 0",
            "    End Select",
            "End Function"), [], "| 1  0 |", ("Outer", 0));

    // a Double given to a Long parameter of a Property Let, rounded as it is passed.
    [TestMethod]
    public async Task ADoubleGivenToALongParameter_IsRoundedToEven()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(2.5); Outer(3.5)",
            "End Sub",
            "Private Function Outer(ByVal D As Double) As Long",
            "    Dim B As Box",
            "    Set B = New Box",
            "    B.Size = D",
            "    Outer = B.Size",
            "End Function"),
            [("Box", ModuleWorkspace.ClassModule("Box", "Option Explicit", "Private mSize As Long",
                "Public Property Get Size() As Long", "    Size = mSize", "End Property",
                "Public Property Let Size(ByVal Value As Long)", "    mSize = Value", "End Property"))],
            "| 2  4 |", ("Outer", 0));
}
