using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A variable let-assigned through the slot it is bound to, by the plan of its coercion (IR lowering S0.8c), is assigned as a let-assignment assigns it
/// (MS-VBAL 5.4.3.1): the value let-coerced to the variable's declared type - or to the type of the variable a Variant reference parameter is bound to - and
/// an error when the coercion fails, where the assignment is written.
/// </summary>
[TestClass]
public sealed class BoundAssignmentTests
{
    private static Task<string[]> RunModuleAsync(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n");

    private static Task<string[]> RunMainAsync(params string[] lines)
        => RunModuleAsync(["Public Sub Main()", .. lines, "End Sub"]);

    // MS-VBAL 5.5.1.2.1.1: a Double let-coerced to a Long is rounded to even.
    [TestMethod]
    public async Task AValue_IsLetCoercedToTheDeclaredTypeOfTheVariable()
        => CollectionAssert.AreEqual(new[] { "2", "4", "True" }, await RunMainAsync(
            "Dim L As Long",
            "Dim B As Boolean",
            "L = 2.5",
            "Debug.Print L",
            "L = 3.5",
            "Debug.Print L",
            "B = 7",
            "Debug.Print B"));

    [TestMethod]
    public async Task AValue_OutsideTheRangeOfTheVariable_IsAnOverflow()
        => CollectionAssert.AreEqual(new[] { "6", "0" }, await RunMainAsync(
            "Dim I As Integer",
            "On Error Resume Next",
            "I = 40000",
            "Debug.Print Err.Number",
            "Debug.Print I"));

    // MS-VBAL 5.3.1.11: a Variant reference parameter bound to a variable of another type is assigned as the type of that variable.
    [TestMethod]
    public async Task AVariantByRefParameter_IsAssignedAsTheTypeOfTheCallersVariable()
        => CollectionAssert.AreEqual(new[] { "2", "Long" }, await RunModuleAsync(
            "Public Sub Main()",
            "    Dim Value As Long",
            "    Change Value",
            "    Debug.Print Value",
            "    Debug.Print TypeName(Value)",
            "End Sub",
            "Private Sub Change(ByRef Target As Variant)",
            "    Target = 2.5",
            "End Sub"));

    // MS-VBAL 5.2.3.4: what is let-coerced to an enumeration is let-coerced to a Long.
    [TestMethod]
    public async Task AVariableOfAnEnumeration_IsAssignedALong()
        => CollectionAssert.AreEqual(new[] { "3" }, await RunModuleAsync(
            "Private Enum Size",
            "    Small = 1",
            "    Large = 2",
            "End Enum",
            "Public Sub Main()",
            "    Dim S As Size",
            "    S = 2.6",
            "    Debug.Print S",
            "End Sub"));

    // MS-VBAL 5.5.1.2.12: a Variant holds the value as it is, whatever it held before.
    [TestMethod]
    public async Task AVariant_HoldsWhatIsAssignedToIt_AsItIs()
        => CollectionAssert.AreEqual(new[] { "Integer", "Double", "String", "x" }, await RunMainAsync(
            "Dim V As Variant",
            "V = 5",
            "Debug.Print TypeName(V)",
            "V = 2.5",
            "Debug.Print TypeName(V)",
            "V = \"x\"",
            "Debug.Print TypeName(V)",
            "Debug.Print V"));

    // the object a Variant held is let go of when something else is assigned to it: its last reference gone, it terminates.
    [TestMethod]
    public async Task AVariant_ThatHeldAnObject_LetsItGo_WhenANumberIsAssignedToIt()
        => CollectionAssert.AreEqual(new[] { "terminated", "after" }, await RunAsync(
            [("Widget", ClassModule("Widget", "Private Sub Class_Terminate()", "    Debug.Print \"terminated\"", "End Sub"))],
            "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n    Dim V As Variant\r\n    Set V = New Widget\r\n    V = 5\r\n    Debug.Print \"after\"\r\nEnd Sub\r\n"));

    // MS-VBAL 5.4.2.3: the counter is let-assigned each iteration; an Integer counter past its range is an overflow at the Next.
    [TestMethod]
    public async Task AForCounter_ThatLeavesTheRangeOfItsType_IsAnOverflow()
        => CollectionAssert.AreEqual(new[] { "32766", "32767", "6" }, await RunModuleAsync(
            "Public Sub Main()",
            "    On Error GoTo Failed",
            "    Dim I As Integer",
            "    For I = 32766 To 32767",
            "        Debug.Print I",
            "    Next",
            "    Exit Sub",
            "Failed:",
            "    Debug.Print Err.Number",
            "End Sub"));

    [TestMethod]
    public async Task AForCounter_OfAFloatingPointType_IsSteppedByAFraction()
        => CollectionAssert.AreEqual(new[] { "0", "0.5", "1", "1.5" }, await RunMainAsync(
            "Dim D As Double",
            "For D = 0 To 1.5 Step 0.5",
            "    Debug.Print D",
            "Next"));
}
