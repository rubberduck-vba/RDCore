using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Let</c> to a <c>Variant</c> variable, or to the result of a function of the <c>Variant</c> type, of a Variant variable that holds a <c>String</c>
/// or <c>Null</c> is compiled (MS-VBAL §5.5.1.2.12): the target holds a copy of the value, as the interpreter's assignment makes it. Over an object, the
/// interpreter runs the assignment, which lets go of the object.
/// </summary>
[TestClass]
public sealed class CompiledVariantCopyTests
{
    private static readonly string[] Shown =
        ["Private Function Shown(ByVal V As Variant) As String", "    Shown = TypeName(V) & \"(\" & V & \")\"", "End Function"];

    private static readonly (string Name, string Source)[] Box =
        [("Box", ModuleWorkspace.ClassModule("Box", "Option Explicit", "Private Sub Class_Terminate()", "    Debug.Print \"gone\"", "End Sub"))];

    // a Variant given a String, and one given Null, each copied to another Variant: the copies keep their subtypes once the source holds a number.
    [TestMethod]
    public async Task AVariantHoldingAStringOrNull_IsCopiedToAVariant()
        => await AssertCompiledAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Kinds()",
            "End Sub",
            "Private Function Kinds() As Long",
            "    Dim V As Variant, W As Variant, U As Variant",
            "    V = \"abc\"",
            "    W = V",
            "    V = Null",
            "    U = V",
            "    V = 1",
            "    Kinds = VarType(W) * 10000 + VarType(U) * 100 + VarType(V)",
            "End Function"]), [], "| 80102 |", ("Kinds", 0));

    // the copy of a String holds its own text: the source given another String leaves it as it was.
    [TestMethod]
    public async Task ACopiedString_KeepsItsTextWhenTheSourceIsReassigned()
        => await AssertCompiledAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Shown(Copied())",
            "End Sub",
            "Private Function Copied() As Variant",
            "    Dim V As Variant, W As Variant",
            "    V = \"abc\"",
            "    W = V",
            "    V = \"x\"",
            "    Copied = W",
            "End Function",
            .. Shown]), [], "| String(abc) |", ("Copied", 0));

    // the result of a function of the Variant type given a Variant that holds a String, vbNullString or Null.
    [TestMethod]
    public async Task AFunctionResultGivenAVariantHoldingAStringOrNull_IsCompiled()
        => await AssertCompiledAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Shown(Varied(\"abc\")) & \" \" & Shown(Varied(vbNullString)) & Len(Varied(vbNullString)) & \" \" & IsNull(Nulled())",
            "End Sub",
            "Private Function Varied(ByVal S As String) As Variant",
            "    Dim V As Variant",
            "    V = S",
            "    Varied = V",
            "End Function",
            "Private Function Nulled() As Variant",
            "    Dim V As Variant",
            "    V = Null",
            "    Nulled = V",
            "End Function",
            .. Shown]), [], "| String(abc) String()0 True |", ("Varied", 0), ("Nulled", 0));

    // a Variant that holds an object, given a Variant that holds a String: the interpreter runs the Let, which lets go of the object.
    [TestMethod]
    public async Task AVariantHoldingAnObject_GivenACopy_IsTheInterpretersToAssign()
        => await AssertCompiledAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Shown(Copied())",
            "End Sub",
            "Private Function Copied() As Variant",
            "    Dim V As Variant, W As Variant",
            "    Set W = New Box",
            "    V = \"abc\"",
            "    W = V",
            "    Copied = W",
            "End Function",
            .. Shown]), Box, "| gone ; String(abc) |", ("Copied", 1));

    // a function result that holds an object, given a Variant that holds a String: the interpreter runs the Let, which lets go of the object.
    [TestMethod]
    public async Task AFunctionResultHoldingAnObject_GivenACopy_IsTheInterpretersToAssign()
        => await AssertCompiledAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Shown(Held())",
            "End Sub",
            "Private Function Held() As Variant",
            "    Dim V As Variant",
            "    Set Held = New Box",
            "    V = \"abc\"",
            "    Held = V",
            "End Function",
            .. Shown]), Box, "| gone ; String(abc) |", ("Held", 1));
}
