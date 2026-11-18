using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Variant</c> variable let-coerced to <c>String</c>, compiled: in a <c>String</c> Let and given to a <c>String</c> parameter by value, it is the
/// text the interpreter's let-coercion writes of it (<strong>MS-VBAL §5.5.1.2.4</strong>, <strong>§5.5.1.2.11</strong>): a <c>String</c> as it is,
/// <c>Empty</c> as the zero-length string, and a whole number as its digits. A Variant that holds anything else is given to the interpreter. A program
/// runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledVariantTextTests
{
    // a String, Empty and whole numbers, each held by a Variant parameter, and a Variant variable assigned in the body.
    [TestMethod]
    public async Task AVariantOfAStringEmptyOrAWholeNumber_IsItsText()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Texts(\"ab\"); \"|\"; Texts(Empty); \"|\"; Texts(-12); \"|\"; Texts(CByte(7)); \"|\"; Texts(123456789); \"|\"; Held()",
            "End Sub",
            "Private Function Texts(ByVal V As Variant) As String",
            "    Dim R As String",
            "    R = V",
            "    Texts = R",
            "End Function",
            "Private Function Held() As String",
            "    Dim V As Variant",
            "    Dim R As String",
            "    V = 40 + 2",
            "    R = V",
            "    Held = R",
            "End Function"), [], "ab||-12|7|123456789|42", ("Texts", 0), ("Held", 0));

    // a Variant given to a String parameter by value is the text it is let-coerced to.
    [TestMethod]
    public async Task AVariantGivenToAStringParameter_IsItsText()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Passes(\"ab\"); Passes(5)",
            "End Sub",
            "Private Function Passes(ByVal V As Variant) As String",
            "    Passes = Shown(V)",
            "End Function",
            "Private Function Shown(ByVal S As String) As String",
            "    Shown = \"[\" & S & \"]\"",
            "End Function"), [], "[ab][5]", ("Passes", 0), ("Shown", 0));

    // a Double, a Boolean and a Date are written in the regional settings of the session, which the interpreter writes them in: each Let is handed to it.
    [TestMethod]
    public async Task AVariantOfAnyOtherValue_IsTheInterpreters()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Texts(1.5) = CStr(1.5)",
            "    Debug.Print Texts(True) = CStr(True)",
            "    Debug.Print Texts(DateSerial(2003, 1, 2)) = CStr(DateSerial(2003, 1, 2))",
            "End Sub",
            "Private Function Texts(ByVal V As Variant) As String",
            "    Dim R As String",
            "    R = V",
            "    Texts = R",
            "End Function"), [], "True ; True ; True", ("Texts", 3));

    // Null let-coerced to String is error 94 (MS-VBAL §5.5.1.2.10), raised where the interpreter raises it.
    [TestMethod]
    public async Task AVariantOfNull_IsError94()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    On Error Resume Next",
            "    Debug.Print Texts(Null)",
            "    Debug.Print \"err\"; Err.Number",
            "End Sub",
            "Private Function Texts(ByVal V As Variant) As String",
            "    Dim R As String",
            "    R = V",
            "    Texts = R",
            "End Function"), [], "err 94", [], ("Texts", 0, 0));

    // a Case value that is a Variant is compared as its value type says (MS-VBAL §5.6.9.5), which the interpreter does: the Case is its own, 1.
    [TestMethod]
    public async Task AVariantCaseValue_IsComparedAsItsValueType()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Picked(\"5\", 5)",
            "End Sub",
            "Private Function Picked(ByVal S As String, ByVal V As Variant) As String",
            "    Select Case S",
            "        Case V",
            "            Picked = \"five\"",
            "        Case Else",
            "            Picked = \"other\"",
            "    End Select",
            "End Function"), [], "five", ["VariantOperand ="], ("Picked", 1, 0));
}
