using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Let</c> of a <c>String</c> or of <c>Null</c> to a <c>Variant</c> variable is compiled (MS-VBAL §5.5.1.2.12): the Variant holds a copy of the
/// String, or <c>Null</c>, as the interpreter's assignment makes it. Over an object, the interpreter runs the assignment, which lets go of the object.
/// </summary>
[TestClass]
public sealed class CompiledVariantTextLetTests
{
    private static readonly string[] Shown =
        ["Private Function Shown(ByVal V As Variant) As String", "    Shown = TypeName(V) & \"(\" & V & \")\"", "End Function"];

    // a String literal and Null, each given to a Variant.
    [TestMethod]
    public async Task AStringLiteralAndNull_AreCompiled()
        => await AssertCompiledAsync(Program([
            "Public Sub Main()",
            "    Dim V As Variant, U As Variant",
            "    Fill V, U",
            "    Debug.Print Shown(V) & \" \" & TypeName(U) & \" \" & IsNull(U)",
            "End Sub",
            "Private Sub Fill(ByRef V As Variant, ByRef U As Variant)",
            "    V = \"abc\"",
            "    U = Null",
            "End Sub",
            .. Shown]), [], "| String(abc) Null True |", ("Fill", 0));

    // a String variable, Strings joined with a whole number, vbNullString and a zero-length String.
    [TestMethod]
    public async Task AStringExpression_IsCompiled()
        => await AssertCompiledAsync(Program([
            "Public Sub Main()",
            "    Dim A As Variant, B As Variant, C As Variant, D As Variant",
            "    Fill \"b\", A, B, C, D",
            "    Debug.Print Shown(A) & \" \" & Shown(B) & \" \" & Shown(C) & Len(C) & \" \" & Shown(D) & Len(D)",
            "End Sub",
            "Private Sub Fill(ByVal S As String, ByRef A As Variant, ByRef B As Variant, ByRef C As Variant, ByRef D As Variant)",
            "    A = S",
            "    B = \"a\" & S & 1",
            "    C = vbNullString",
            "    D = \"\"",
            "End Sub",
            .. Shown]), [], "| String(b) String(ab1) String()0 String()0 |", ("Fill", 0));

    // a Variant that held a number, then a String, then Null, then a number again: each Let gives it the value's own type.
    [TestMethod]
    public async Task AVariantGivenANumberAStringAndNull_HoldsEachInTurn()
        => await AssertCompiledAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Turns()",
            "End Sub",
            "Private Function Turns() As Long",
            "    Dim V As Variant, Kinds As Long",
            "    V = 1",
            "    Kinds = VarType(V)",
            "    V = \"x\"",
            "    Kinds = Kinds * 100 + VarType(V)",
            "    V = Null",
            "    Kinds = Kinds * 100 + VarType(V)",
            "    V = 2.5",
            "    Kinds = Kinds * 100 + VarType(V)",
            "    Turns = Kinds",
            "End Function"]), [], "| 2080105 |", ("Turns", 0));

    // a Variant that holds an object, given a String: the interpreter runs the Let, which lets go of the object, and compiled code goes on after it.
    [TestMethod]
    public async Task AVariantHoldingAnObject_GivenAString_IsTheInterpretersToAssign()
        => await AssertCompiledAsync(Program([
            "Public Sub Main()",
            "    Dim V As Variant",
            "    Fill V",
            "    Debug.Print Shown(V)",
            "End Sub",
            "Private Sub Fill(ByRef V As Variant)",
            "    Set V = New Box",
            "    V = \"abc\"",
            "End Sub",
            .. Shown]),
            [("Box", ModuleWorkspace.ClassModule("Box", "Option Explicit", "Private Sub Class_Terminate()", "    Debug.Print \"gone\"", "End Sub"))],
            "| gone ; String(abc) |", ("Fill", 1));
}
